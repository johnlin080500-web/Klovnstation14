using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._KS14.Trading;
using Content.Shared.GameTicking.Components;
using Content.Shared.Access.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Interaction;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Content.Server.Stack;
using Content.Server.MassMedia.Systems;
using Content.Server.Station.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Trading;

/// <summary>
/// One server-authoritative exchange per round. An ID card is the bearer credential for
/// its account; the account is kept on that card, never on a mutable character name.
/// All amounts are integer spesos; orders are fully paid, with no short selling.
/// </summary>
public sealed partial class TradingMarketSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private NewsSystem _newsSystem = default!;
    [Dependency] private StationSystem _stationSystem = default!;

    private sealed class Account
    {
        public int Balance;
        public readonly Dictionary<string, int> Positions = new();
    }

    private sealed class Asset
    {
        public readonly string Symbol;
        public readonly string Name;
        public int Price;
        public int Outstanding = 50000;
        public int Liquidity = 1200;
        public int Volume;
        public readonly List<int> History = new();
        public Asset(string symbol, string name, int price)
        {
            Symbol = symbol;
            Name = name;
            Price = price;
            History.Add(price);
        }
    }

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

    private sealed record PendingImpact(string Symbol, float Percent, TimeSpan Due, string Title);
    private readonly List<PendingImpact> _pendingImpacts = new();
    private TimeSpan _nextTick;
    private string _news = "Corporate exchange connected. All share prices are fictional.";
    private const string CardSlot = "trading_id";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TradingConsoleComponent, InteractUsingEvent>(OnCashInserted);
        SubscribeLocalEvent<TradingMarketImpactComponent, GameRuleStartedEvent>(OnMarketRuleStarted);
        SubscribeLocalEvent<TradingConsoleComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<TradingConsoleComponent, EntRemovedFromContainerMessage>(OnContainerChanged);

        Subs.BuiEvents<TradingConsoleComponent>(TradingConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<TradingOrderMessage>(OnOrder);
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

    /// <summary>
    /// NanoNews releases publish immediately. The market reaction is delayed, including
    /// for rules that have already ended by the time the change settles.
    /// </summary>
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
        _pendingImpacts.Add(new PendingImpact(symbol, impact, _timing.CurTime + TimeSpan.FromSeconds(delay), title));

        _news = $"NANONEWS | {title}: {body} Market reaction expected within {minDelaySeconds / 60}-{(maxDelaySeconds + 59) / 60} minutes.";
        var stations = EntityQueryEnumerator<Content.Shared.MassMedia.Components.StationNewsComponent>();
        while (stations.MoveNext(out var station, out _))
            _newsSystem.TryAddNews(station, title, body, out _, "NanoNews Corporate Desk");

        UpdateAll();
    }

    private void OnMarketRuleStarted(EntityUid uid, TradingMarketImpactComponent component, ref GameRuleStartedEvent args)
    {
        // The same rule may be dispatched to more than one subscriber.
        if (component.Triggered || !_assets.TryGetValue(component.Symbol, out var asset))
            return;

        component.Triggered = true;
        var percent = Math.Clamp(component.Percent, -80f, 80f);
        asset.Price = Math.Max(1, (int) Math.Round(asset.Price * (1 + percent / 100f)));
        asset.History.Add(asset.Price);
        if (asset.History.Count > 240)
            asset.History.RemoveAt(0);

        _news = $"MARKET ALERT: {component.Headline} ({asset.Symbol} {percent:+0.0;-0.0;0}%)";
        UpdateAll();
    }

    private void OnOpened(Entity<TradingConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent.Owner);
    }

    private void OnContainerChanged(EntityUid uid, TradingConsoleComponent component, EntInsertedIntoContainerMessage args)
    {
        UpdateUi(uid);
    }

    private void OnContainerChanged(EntityUid uid, TradingConsoleComponent component, EntRemovedFromContainerMessage args)
    {
        UpdateUi(uid);
    }

    private void OnCashInserted(EntityUid uid, TradingConsoleComponent component, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryGetAccount(uid, out var account, out _) ||
            !HasComp<CashComponent>(args.Used) ||
            !TryComp<StackComponent>(args.Used, out var stack) ||
            stack.StackTypeId != "Credit" || stack.Unlimited || stack.Count <= 0)
            return;

        if (account.Balance > int.MaxValue - stack.Count)
            return;

        account.Balance += stack.Count;
        QueueDel(args.Used);
        args.Handled = true;
        UpdateAll();
    }

    private void OnOrder(Entity<TradingConsoleComponent> ent, ref TradingOrderMessage args)
    {
        if (args.Actor is not { Valid: true } ||
            !TryGetAccount(ent.Owner, out var account, out _) ||
            args.Quantity < 1 || args.Quantity > 1000 ||
            !_assets.TryGetValue(args.Symbol, out var asset))
            return;

        var cost = (long) asset.Price * args.Quantity;
        account.Positions.TryGetValue(asset.Symbol, out var held);
        if (args.Buy && args.Quantity > asset.Liquidity)
            return;
        if (args.Buy)
        {
            if (cost > account.Balance || held > int.MaxValue - args.Quantity)
                return;
            account.Balance -= (int) cost;
            account.Positions[asset.Symbol] = held + args.Quantity;
        }
        else
        {
            if (held < args.Quantity || cost > int.MaxValue - (long) account.Balance)
                return;
            account.Positions[asset.Symbol] = held - args.Quantity;
            account.Balance += (int) cost;
        }

        asset.Volume = Math.Min(int.MaxValue - args.Quantity, asset.Volume) + args.Quantity;
        if (args.Buy)
            asset.Liquidity -= args.Quantity;
        else
            asset.Liquidity = Math.Min(5000, asset.Liquidity + args.Quantity);
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

    private void UpdateUi(EntityUid console)
    {
        var hasCard = TryGetAccount(console, out var account, out var card);
        var name = hasCard ? Comp<IdCardComponent>(card).FullName ?? Name(card) : "Insert an ID card to log in";
        var quotes = _assets.Values.Select(asset => new TradingQuote(
            asset.Symbol,
            asset.Name,
            asset.Price,
            hasCard && account.Positions.TryGetValue(asset.Symbol, out var held) ? held : 0,
            asset.Liquidity,
            asset.Volume,
            (long) asset.Price * asset.Outstanding,
            Math.Max(1, (int) Math.Ceiling(asset.Price * 0.01)),
            asset.History.ToArray())).ToArray();
        _ui.SetUiState(console, TradingConsoleUiKey.Key,
            new TradingConsoleUiState(name, hasCard ? account.Balance : 0, quotes, _news));
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
            if (!_assets.TryGetValue(news.Symbol, out var affected))
                continue;
            affected.Price = Math.Max(1, (int) Math.Round(affected.Price * (1 + news.Percent / 100f)));
            affected.History.Add(affected.Price);
            if (affected.History.Count > 240)
                affected.History.RemoveAt(0);
            _news = $"NANONEWS UPDATE | {news.Title}: {news.Symbol} moved {news.Percent:+0.0;-0.0;0}%";
            UpdateAll();
        }

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(15);
        foreach (var asset in _assets.Values)
        {
            var percent = _random.NextFloat(-0.012f, 0.012f);
            asset.Price = Math.Max(1, (int) Math.Round(asset.Price * (1 + percent)));
            asset.History.Add(asset.Price);
            asset.Liquidity = Math.Min(5000, asset.Liquidity + 5);
            if (asset.History.Count > 240)
                asset.History.RemoveAt(0);
        }
        UpdateAll();
    }
}
