using System;
using System.Linq;
using System.Numerics;
using Content.Shared.Containers.ItemSlots;
using Content.Client.UserInterface.Controls;
using Content.Shared._KS14.Trading;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._KS14.Trading;

/// <summary>
/// Simple non-XAML exchange window: accounts, searchable equities and a price history
/// represented by recent sampled values. All orders are checked again on the server.
/// </summary>
public sealed class TradingConsoleWindow : FancyWindow
{
    public event Action<string, int, bool>? OnTrade;
    public event Action<int>? OnWithdraw;
    public event Action<string, TradingOrderType, int, int, int>? OnPendingOrder;
    public event Action<int>? OnCancelPendingOrder;

    private readonly Label _account = new();
    private readonly Label _balance = new();
    private readonly Label _news = new();
    private readonly Label _portfolioStats = new();
    private readonly Label _riskStatus = new();
    private readonly Label _selectedNews = new() { Text = "Choose a NanoNews article to read it." };
    private readonly BoxContainer _pendingOrders = new() { Orientation = LayoutOrientation.Vertical };
    private readonly BoxContainer _newsArchive = new() { Orientation = LayoutOrientation.Vertical };
    private readonly Label _orderStock = new();
    private readonly LineEdit _pendingQuantity = new() { Text = "1", PlaceHolder = "Shares" };
    private readonly LineEdit _pendingPrice = new() { Text = "100", PlaceHolder = "Target price in spesos" };
    private int _orderDurationMinutes = 15;
    private readonly Label _chartName = new();
    private readonly Label _marketStats = new();
    private readonly Label _inspect = new() { Text = "Click a point on the graph for time and price details." };
    private readonly TradingPriceGraphControl _graph = new() { MinSize = new Vector2(0, 185), HorizontalExpand = true };
    private string _selectedSymbol = "NT";
    private int _historySamples = 20;
    public event Action? OnCardAction;
    private readonly BoxContainer _quotes = new() { Orientation = LayoutOrientation.Vertical };
    private readonly LineEdit _search = new() { PlaceHolder = "Search company or ticker..." };
    private readonly LineEdit _amount = new() { PlaceHolder = "Amount of spesos" };
    private TradingConsoleUiState? _state;

    public TradingConsoleWindow()
    {
        Title = "Corporate Securities Exchange";
        MinSize = new System.Numerics.Vector2(630, 450);
        SetSize = new System.Numerics.Vector2(850, 610);

        var root = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        ContentsContainer.AddChild(root);

        var tabs = new TabContainer { VerticalExpand = true };
        root.AddChild(tabs);

        var banking = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        banking.AddChild(new Label { Text = "Insert an ID card into the terminal slot. Deposit cash by using a cash stack on the console." });
        var cardButton = new Button { Text = "Insert / Eject ID card" };
        cardButton.OnPressed += _ => OnCardAction?.Invoke();
        banking.AddChild(cardButton);
        banking.AddChild(_account);
        banking.AddChild(_balance);
        banking.AddChild(_portfolioStats);
        banking.AddChild(_amount);
        var withdrawal = new Button { Text = "Withdraw cash" };
        withdrawal.OnPressed += _ =>
        {
            if (int.TryParse(_amount.Text, out var amount) && amount > 0)
                OnWithdraw?.Invoke(amount);
        };
        banking.AddChild(withdrawal);
        tabs.AddChild(banking);
        tabs.SetTabTitle(0, "Banking");

        var trading = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        trading.AddChild(_search);
        trading.AddChild(_chartName);
        trading.AddChild(_marketStats);
        trading.AddChild(_riskStatus);
        trading.AddChild(_graph);
        trading.AddChild(_inspect);
        _graph.OnSampleSelected += (index, price) =>
        {
            var selectedTime = _graph.HistoryTimes.Length > index ? _graph.HistoryTimes[index] : index * 15;
            var candidate = _state?.NewsHistory
                .Where(n => n.Symbol == _selectedSymbol && Math.Abs(n.PublishedSeconds - selectedTime) <= 120)
                .OrderBy(n => Math.Abs(n.PublishedSeconds - selectedTime))
                .FirstOrDefault();
            var explanation = candidate == null ? "No corporate announcement within two minutes." : candidate.Title + ": " + candidate.Description;
            _inspect.Text = $"Price: {price} sp | market time {selectedTime / 60}m {selectedTime % 60}s | {explanation}";
        };
        var chartModes = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        foreach (var (caption, mode) in new (string, TradingChartType)[] { ("Line", TradingChartType.Line), ("Bar", TradingChartType.Bar), ("Candles", TradingChartType.Candle) })
        {
            var button = new Button { Text = caption };
            button.OnPressed += _ => _graph.ChartType = mode;
            chartModes.AddChild(button);
        }
        trading.AddChild(chartModes);
        var chartPeriods = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        foreach (var (caption, count) in new (string, int)[] { ("1m", 4), ("5m", 20), ("15m", 60), ("All", 0) })
        {
            var button = new Button { Text = caption };
            button.OnPressed += _ =>
            {
                _historySamples = count;
                RefreshChart();
            };
            chartPeriods.AddChild(button);
        }
        trading.AddChild(chartPeriods);
        trading.AddChild(new Label { Text = "Share prices in spesos. Enter quantity and buy or sell; no borrowing or short selling." });
        var scroll = new ScrollContainer { VerticalExpand = true };
        scroll.AddChild(_quotes);
        trading.AddChild(scroll);
        trading.AddChild(_news);
        tabs.AddChild(trading);
        tabs.SetTabTitle(1, "Trading");

        // Conditional orders are held on the server. Long buys reserve credits;
        // limit/stop sells reserve owned shares to prevent double-spending.
        var orders = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        orders.AddChild(new Label { Text = "Limit orders use bid/ask quotes. Stop-loss orders sell at the available bid, which may be worse than your trigger price." });
        orders.AddChild(_orderStock);
        orders.AddChild(new Label { Text = "Quantity of shares:" });
        orders.AddChild(_pendingQuantity);
        orders.AddChild(new Label { Text = "Limit / stop trigger price (spesos):" });
        orders.AddChild(_pendingPrice);
        orders.AddChild(new Label { Text = "Order expires after:" });
        var durationOptions = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        foreach (var minutes in new[] { 5, 15, 30 })
        {
            var expiry = new Button { Text = minutes + " minutes" };
            expiry.OnPressed += _ =>
            {
                _orderDurationMinutes = minutes;
                _orderStock.Text = $"Selected: {_selectedSymbol} | expiration: {_orderDurationMinutes}m";
            };
            durationOptions.AddChild(expiry);
        }
        orders.AddChild(durationOptions);
        var choices = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        foreach (var (text, type) in new (string, TradingOrderType)[]
        {
            ("Limit buy", TradingOrderType.LimitBuy),
            ("Limit sell", TradingOrderType.LimitSell),
            ("Stop-loss sell", TradingOrderType.StopLoss)
        })
        {
            var action = new Button { Text = text };
            action.OnPressed += _ => CreatePendingOrder(type);
            choices.AddChild(action);
        }
        orders.AddChild(choices);
        orders.AddChild(new Label { Text = "Open orders (cancel to release reserved funds or shares):" });
        var pendingScroll = new ScrollContainer { VerticalExpand = true };
        pendingScroll.AddChild(_pendingOrders);
        orders.AddChild(pendingScroll);
        tabs.AddChild(orders);
        tabs.SetTabTitle(2, "Orders");

        var newsTab = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        newsTab.AddChild(new Label { Text = "NanoNews corporate events. Actual market reactions appear only after they occur." });
        newsTab.AddChild(_selectedNews);
        var newsScroll = new ScrollContainer { VerticalExpand = true };
        newsScroll.AddChild(_newsArchive);
        newsTab.AddChild(newsScroll);
        tabs.AddChild(newsTab);
        tabs.SetTabTitle(3, "NanoNews");

        _search.OnTextChanged += _ => RefreshQuotes();
    }

    public void UpdateState(TradingConsoleUiState state)
    {
        _state = state;
        _account.Text = $"Card: {state.CardName}";
        _balance.Text = $"Available cash: {state.Balance:N0} sp | reserved by orders: {state.ReservedCash:N0} sp";
        _portfolioStats.Text = $"Portfolio value: {state.PortfolioValue:N0} sp | Un/realized P/L: {state.UnrealizedProfit:+#,0;-#,0;0} / {state.RealizedProfit:+#,0;-#,0;0} sp";
        _news.Text = state.News;
        RefreshQuotes();
        RefreshChart();
        RefreshPendingOrders();
        RefreshNewsArchive();
    }

    private void RefreshChart()
    {
        var quote = _state?.Quotes.FirstOrDefault(q => q.Symbol == _selectedSymbol);
        if (quote == null)
            return;

        _graph.History = quote.History;
        _graph.HistoryTimes = quote.HistoryTimes;
        _graph.EventTimes = _state!.NewsHistory.Where(n => n.Symbol == quote.Symbol).Select(n => n.PublishedSeconds).ToArray();
        _graph.SamplesToShow = _historySamples;
        _orderStock.Text = $"Selected: {quote.Symbol} | expiration: {_orderDurationMinutes}m";
        var samples = _historySamples == 0 ? quote.History.Length : Math.Min(_historySamples, quote.History.Length);
        var window = quote.History.Skip(Math.Max(0, quote.History.Length - samples)).ToArray();
        var low = window.Length == 0 ? quote.Price : window.Min();
        var high = window.Length == 0 ? quote.Price : window.Max();
        _chartName.Text = $"{quote.Name} ({quote.Symbol}) | {quote.Price} sp | low {low} / high {high} sp";
        _marketStats.Text = $"Bid: {quote.Bid} | Ask: {quote.Ask} | Spread: {quote.Spread} sp | Available: {quote.Liquidity} shares | Cap: {quote.MarketCap:N0} sp | Volume: {quote.Volume:N0}";
        var volatility = quote.VolatilityPercent < 1 ? "Low" : quote.VolatilityPercent < 4 ? "Moderate" : quote.VolatilityPercent < 12 ? "High" : "Extreme";
        _riskStatus.Text = $"24h change: N/A (round-only market) | Latest move: {quote.ChangePercent:+0.0;-0.0;0}% | Volatility: {volatility} ({quote.VolatilityPercent:F1}% RMS) | { (quote.HaltSeconds > 0 ? $"HALTED {quote.HaltSeconds}s" : "TRADING OPEN") }";
    }

    private void RefreshQuotes()
    {
        _quotes.RemoveAllChildren();
        if (_state == null)
            return;

        var filter = _search.Text.Trim();
        foreach (var quote in _state.Quotes.Where(x =>
                     x.Symbol.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var line = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            var ticker = new Label
            {
                Text = $"{quote.Symbol} | {quote.Name} | {quote.Price} sp | held: {quote.Held}",
                HorizontalExpand = true
            };
            line.AddChild(ticker);
            var quantity = new LineEdit
            {
                Text = "1",
                MinWidth = 50,
                PlaceHolder = "Qty"
            };
            line.AddChild(quantity);
            var buy = new Button { Text = "Buy" };
            var sell = new Button { Text = "Sell" };
            buy.OnPressed += _ => SendOrder(quote.Symbol, quantity.Text, true);
            sell.OnPressed += _ => SendOrder(quote.Symbol, quantity.Text, false);
            line.AddChild(buy);
            line.AddChild(sell);
            var chartButton = new Button { Text = "Chart" };
            chartButton.OnPressed += _ =>
            {
                _selectedSymbol = quote.Symbol;
                RefreshChart();
            };
            line.AddChild(chartButton);
            _quotes.AddChild(line);
        }
    }

    private void CreatePendingOrder(TradingOrderType type)
    {
        if (_state?.HasCard != true)
            return;

        if (!int.TryParse(_pendingQuantity.Text, out var quantity) || quantity is < 1 or > 1000 ||
            !int.TryParse(_pendingPrice.Text, out var price) || price is < 1 or > 1000000)
            return;

        OnPendingOrder?.Invoke(_selectedSymbol, type, quantity, price, _orderDurationMinutes);
    }

    private void RefreshPendingOrders()
    {
        _pendingOrders.RemoveAllChildren();
        if (_state?.HasCard != true)
        {
            _pendingOrders.AddChild(new Label { Text = "Insert an ID card to view orders." });
            return;
        }

        foreach (var order in _state.Orders)
        {
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            row.AddChild(new Label
            {
                Text = $"#{order.Id} {order.Symbol} {order.Type} x{order.Quantity} @ {order.TriggerPrice} sp | expires in {order.ExpiresInSeconds / 60}m {order.ExpiresInSeconds % 60}s",
                HorizontalExpand = true
            });
            var cancel = new Button { Text = "Cancel" };
            cancel.OnPressed += _ => OnCancelPendingOrder?.Invoke(order.Id);
            row.AddChild(cancel);
            _pendingOrders.AddChild(row);
        }
    }

    private void RefreshNewsArchive()
    {
        _newsArchive.RemoveAllChildren();
        if (_state == null)
            return;

        foreach (var item in _state.NewsHistory.Reverse())
        {
            var state = item.ResolvedSeconds == null ? "Pending reaction" :
                $"Settled {item.ActualImpactPercent:+0.0;-0.0;0}%";
            var open = new Button
            {
                Text = $"t+{item.PublishedSeconds / 60}m | {item.Symbol} | {item.Title} | {state}",
                HorizontalExpand = true
            };
            open.OnPressed += _ =>
            {
                _selectedNews.Text = $"NanoNews: {item.Title} ({item.Symbol}) — {item.Description} | {state}";
                _selectedSymbol = item.Symbol;
                RefreshChart();
            };
            _newsArchive.AddChild(open);
        }
    }

    private void SendOrder(string symbol, string quantityText, bool buy)
    {
        if (int.TryParse(quantityText, out var qty) && qty is >= 1 and <= 1000)
            OnTrade?.Invoke(symbol, qty, buy);
    }
}
