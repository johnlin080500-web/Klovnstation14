using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.MassMedia.Systems;
using Content.Server.Stack;
using Content.Shared._KS14.Trading;
using Content.Shared.Access.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.GameTicking.Components;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Trading;

/// <summary>
/// Per-round, server-authoritative fictional exchange. The inserted ID is the bearer
/// credential. Cash and shares reserved for pending orders cannot be spent twice.
/// </summary>
public sealed partial class TradingMarketSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private NewsSystem _newsSystem = default!;

    private const string CardSlot = "trading_id";
    private const int PriceCeiling = 1000000;
    private const int MaxOrderQuantity = 1000;
    private const int MaxPendingPerAccount = 16;
    private const int MaxHistory = 240;
    private const int MaxNews = 36;

    private sealed class Account
    {
        public int Balance;
        public long ReservedCash;
        public long RealizedProfit;
        public readonly Dictionary<string, int> Positions = new();
        public readonly Dictionary<string, int> ReservedShares = new();
        public readonly Dictionary<string, long> CostBasis = new();
        public readonly List<PendingOrder> Orders = new();
    }

    private sealed class PendingOrder
    {
        public int Id;
        public string Symbol = string.Empty;
        public TradingOrderType Type;
        public int Quantity;
        public int TriggerPrice;
        public int ReservedCash;
        public TimeSpan ExpiresAt;
    }

    private sealed class Asset
    {
        public readonly string Symbol;
        public readonly string Name;
        public int Price;
        public int Outstanding = 50000;
        public int Liquidity = 1200;
        public int Volume;
        public TimeSpan HaltUntil;
        public readonly List<int> History = new();
        public readonly List<int> HistoryTimes = new();

        public Asset(string symbol, string name, int price)
        {
            Symbol = symbol;
            Name = name;
            Price = price;
            History.Add(price);
            HistoryTimes.Add(0);
        }
    }

    private sealed record PendingImpact(string Symbol, float Percent, TimeSpan Due, TradingNewsEntry Article);

    private readonly Dictionary<EntityUid, Account> _accounts = new();
    private readonly Dictionary<string, Asset> _assets = new()
    {
        ["NT"] = new("NT", "NanoTrasen", 100),
        ["INT"] = new("INT", "Interdyne Pharmaceutics", 75),
        ["CYB"] = new("CYB", "CyberSun Industries", 130),
        ["DONK"] = new("DONK", "Donk Corporation", 40),
        ["WAFL"] = new("WAFL", "Waffle Corporation", 32),
        ["GRLX"] = new("GRLX", "Gorlex Marauders", 58)
    };

    private readonly List<PendingImpact> _pendingImpacts = new();
    private readonly List<TradingNewsEntry> _newsHistory = new();
    private int _nextOrderId = 1;
    private TimeSpan _nextTick;
    private string _news = "NanoNews: Corporate exchange connected. All shares are fictional.";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TradingConsoleComponent, InteractUsingEvent>(OnCashInserted);
        SubscribeLocalEvent<TradingConsoleComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<TradingConsoleComponent, EntRemovedFromContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<TradingMarketImpactComponent, GameRuleStartedEvent>(OnMarketRuleStarted);

        Subs.BuiEvents<TradingConsoleComponent>(TradingConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<TradingOrderMessage>(OnOrder);
            subs.Event<TradingPlacePendingOrderMessage>(OnPlacePendingOrder);
            subs.Event<TradingCancelPendingOrderMessage>(OnCancelPendingOrder);
            subs.Event<TradingWithdrawMessage>(OnWithdraw);
        });
    }

    private bool TryGetAccount(EntityUid terminal, out Account account, out EntityUid card)
    {
        account = default!;
        card = default;
        if (!TryComp<ItemSlotsComponent>(terminal, out var slots) ||
            !slots.Slots.TryGetValue(CardSlot, out var slot) ||
            slot.Item is not { } id ||
            !HasComp<IdCardComponent>(id))
            return false;

        card = id;
        if (!_accounts.TryGetValue(card, out var existing))
            _accounts[card] = existing = new Account();
        account = existing;
        return true;
    }

    private int CurrentSecond => Math.Max(0, (int) _timing.CurTime.TotalSeconds);

    private void AddNews(TradingNewsEntry entry)
    {
        _newsHistory.Add(entry);
        if (_newsHistory.Count > MaxNews)
            _newsHistory.RemoveAt(0);
    }

    /// <summary>Publish NanoNews immediately and schedule an undisclosed settlement shock.</summary>
    public void ScheduleCorporateNews(string symbol, string title, string body,
        float minPercent, float maxPercent, int minDelaySeconds, int maxDelaySeconds)
    {
        if (!_assets.ContainsKey(symbol))
            return;

        minPercent = Math.Clamp(minPercent, -70f, 70f);
        maxPercent = Math.Clamp(maxPercent, minPercent, 70f);
        minDelaySeconds = Math.Clamp(minDelaySeconds, 60, 900);
        maxDelaySeconds = Math.Clamp(maxDelaySeconds, minDelaySeconds, 900);

        var impact = _random.NextFloat(minPercent, maxPercent);
        var delay = _random.Next(minDelaySeconds, maxDelaySeconds + 1);
        var article = new TradingNewsEntry(symbol, title, body, CurrentSecond);
        AddNews(article);
        _pendingImpacts.Add(new PendingImpact(symbol, impact,
            _timing.CurTime + TimeSpan.FromSeconds(delay), article));

        _news = $"NANONEWS | {title}: {body} Market reaction may follow within {minDelaySeconds / 60}-{(maxDelaySeconds + 59) / 60} minutes.";
        var stations = EntityQueryEnumerator<Content.Shared.MassMedia.Components.StationNewsComponent>();
        while (stations.MoveNext(out var station, out _))
            _newsSystem.TryAddNews(station, title, body, out _, "NanoNews Corporate Desk");

        UpdateAll();
    }

    private void OnMarketRuleStarted(EntityUid uid, TradingMarketImpactComponent component, ref GameRuleStartedEvent args)
    {
        if (component.Triggered || !_assets.ContainsKey(component.Symbol))
            return;

        component.Triggered = true;
        var percent = Math.Clamp(component.Percent, -70f, 70f);
        ApplyPriceMove(component.Symbol, percent);
        AddNews(new TradingNewsEntry(component.Symbol, "Station event: market alert",
            component.Headline, CurrentSecond, CurrentSecond, percent));
        _news = $"MARKET ALERT: {component.Headline} ({component.Symbol} {percent:+0.0;-0.0;0}%)";
        UpdateAll();
    }

    private void OnOpened(Entity<TradingConsoleComponent> ent, ref BoundUIOpenedEvent args) => UpdateUi(ent.Owner);

    private void OnContainerChanged(EntityUid uid, TradingConsoleComponent comp, EntInsertedIntoContainerMessage args) => UpdateUi(uid);

    private void OnContainerChanged(EntityUid uid, TradingConsoleComponent comp, EntRemovedFromContainerMessage args) => UpdateUi(uid);

    private void OnCashInserted(EntityUid uid, TradingConsoleComponent comp, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryGetAccount(uid, out var account, out _) ||
            !HasComp<CashComponent>(args.Used) ||
            !TryComp<StackComponent>(args.Used, out var stack) ||
            stack.StackTypeId != "Credit" || stack.Unlimited || stack.Count <= 0 ||
            account.Balance > int.MaxValue - stack.Count)
            return;

        account.Balance += stack.Count;
        QueueDel(args.Used);
        args.Handled = true;
        UpdateAll();
    }

    private void OnWithdraw(Entity<TradingConsoleComponent> ent, ref TradingWithdrawMessage args)
    {
        if (args.Actor is not { Valid: true } ||
            !TryGetAccount(ent.Owner, out var account, out _) ||
            args.Amount <= 0 || args.Amount > account.Balance)
            return;

        account.Balance -= args.Amount;
        _stacks.SpawnAtPosition(args.Amount, new ProtoId<StackPrototype>("Credit"), Transform(ent).Coordinates);
        UpdateAll();
    }

    private static int Spread(Asset asset) => Math.Max(1, (int) Math.Ceiling(asset.Price * 0.01));

    private static int Ask(Asset asset) => Math.Min(PriceCeiling, asset.Price + (Spread(asset) + 1) / 2);

    private static int Bid(Asset asset) => Math.Max(1, asset.Price - Spread(asset) / 2);

    private bool IsHalted(Asset asset) => _timing.CurTime < asset.HaltUntil;

    private void RecordPrice(Asset asset)
    {
        asset.History.Add(asset.Price);
        asset.HistoryTimes.Add(CurrentSecond);
        if (asset.History.Count > MaxHistory)
        {
            asset.History.RemoveAt(0);
            asset.HistoryTimes.RemoveAt(0);
        }
    }

    /// <summary>Individual-stock circuit breakers: 15/35/60% moves halt 1/3/5 minutes.</summary>
    private void ApplyPriceMove(string symbol, float percent)
    {
        if (!_assets.TryGetValue(symbol, out var asset))
            return;

        var oldPrice = asset.Price;
        asset.Price = Math.Clamp((int) Math.Round(oldPrice * (1 + percent / 100f)), 1, PriceCeiling);
        RecordPrice(asset);
        var realizedMove = 100f * Math.Abs(asset.Price - oldPrice) / Math.Max(1, oldPrice);
        var haltSeconds = realizedMove >= 60 ? 300 : realizedMove >= 35 ? 180 : realizedMove >= 15 ? 60 : 0;
        if (haltSeconds > 0)
        {
            var until = _timing.CurTime + TimeSpan.FromSeconds(haltSeconds);
            if (until > asset.HaltUntil)
                asset.HaltUntil = until;
        }
    }

    private static float Volatility(Asset asset)
    {
        var count = Math.Min(asset.History.Count - 1, 20);
        if (count <= 0)
            return 0f;
        double sumSquares = 0;
        var start = asset.History.Count - count;
        for (var i = start; i < asset.History.Count; i++)
        {
            var prev = Math.Max(1, asset.History[i - 1]);
            var change = 100d * (asset.History[i] - prev) / prev;
            sumSquares += change * change;
        }
        return (float) Math.Sqrt(sumSquares / count);
    }

    private void UpdateUi(EntityUid console)
    {
        var hasCard = TryGetAccount(console, out var account, out var card);
        var name = hasCard ? Comp<IdCardComponent>(card).FullName ?? Name(card) : "Insert an ID card to log in";
        long invested = 0;
        long profit = 0;
        var quotes = _assets.Values.Select(asset =>
        {
            var held = hasCard && account.Positions.TryGetValue(asset.Symbol, out var shares) ? shares : 0;
            var reserved = hasCard && account.ReservedShares.TryGetValue(asset.Symbol, out var locked) ? locked : 0;
            var basis = hasCard && account.CostBasis.TryGetValue(asset.Symbol, out var cost) ? cost : 0L;
            invested += (long) asset.Price * held;
            var unrealized = (long) Bid(asset) * held - basis;
            profit += unrealized;
            var history = asset.History;
            var old = history[Math.Max(0, history.Count - 2)];
            return new TradingQuote(asset.Symbol, asset.Name, asset.Price, Bid(asset), Ask(asset),
                held, reserved, asset.Liquidity, asset.Volume,
                (long) asset.Price * asset.Outstanding, Spread(asset),
                held == 0 ? 0f : (float) basis / held, unrealized,
                Volatility(asset), old == 0 ? 0 : 100f * (asset.Price - old) / old,
                Math.Max(0, (int) Math.Ceiling((asset.HaltUntil - _timing.CurTime).TotalSeconds)),
                asset.History.ToArray(), asset.HistoryTimes.ToArray());
        }).ToArray();

        var orders = hasCard
            ? account.Orders.Select(o => new TradingOrderSummary(o.Id, o.Symbol, o.Type,
                o.Quantity, o.TriggerPrice,
                Math.Max(0, (int) Math.Ceiling((o.ExpiresAt - _timing.CurTime).TotalSeconds)))).ToArray()
            : Array.Empty<TradingOrderSummary>();

        _ui.SetUiState(console, TradingConsoleUiKey.Key,
            new TradingConsoleUiState(name, hasCard, hasCard ? account.Balance : 0,
                hasCard ? account.ReservedCash : 0, hasCard ? invested + account.Balance + account.ReservedCash : 0,
                hasCard ? profit : 0, hasCard ? account.RealizedProfit : 0,
                quotes, orders, _newsHistory.ToArray(), _news));
    }

    private void UpdateAll()
    {
        var query = EntityQueryEnumerator<TradingConsoleComponent>();
        while (query.MoveNext(out var uid, out _))
            UpdateUi(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        for (var i = _pendingImpacts.Count - 1; i >= 0; i--)
        {
            var news = _pendingImpacts[i];
            if (_timing.CurTime < news.Due)
                continue;

            _pendingImpacts.RemoveAt(i);
            ApplyPriceMove(news.Symbol, news.Percent);
            news.Article.ResolvedSeconds = CurrentSecond;
            news.Article.ActualImpactPercent = news.Percent;
            _news = $"NANONEWS UPDATE | {news.Article.Title}: {news.Symbol} moved {news.Percent:+0.0;-0.0;0}%";
            UpdateAll();
        }

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(15);
        foreach (var asset in _assets.Values)
        {
            // Routine price movements are smaller than event-driven corporate shocks.
            var percent = _random.NextFloat(-0.012f, 0.012f);
            asset.Price = Math.Clamp((int) Math.Round(asset.Price * (1 + percent)), 1, PriceCeiling);
            RecordPrice(asset);
            asset.Liquidity = Math.Min(5000, asset.Liquidity + 5);
        }

        ProcessPendingOrders();
        UpdateAll();
    }
}
