using System;
using System.Linq;
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

    private readonly Label _account = new();
    private readonly Label _balance = new();
    private readonly Label _news = new();
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
        Contents.AddChild(root);

        var tabs = new TabContainer { VerticalExpand = true };
        root.AddChild(tabs);

        var banking = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        banking.AddChild(new Label { Text = "Insert an ID card into the terminal slot. Deposit cash by using a cash stack on the console." });
        banking.AddChild(_account);
        banking.AddChild(_balance);
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
        trading.AddChild(new Label { Text = "Share prices in spesos. Enter quantity and buy or sell; no borrowing or short selling." });
        var scroll = new ScrollContainer { VerticalExpand = true };
        scroll.AddChild(_quotes);
        trading.AddChild(scroll);
        trading.AddChild(_news);
        tabs.AddChild(trading);
        tabs.SetTabTitle(1, "Trading");

        _search.OnTextChanged += _ => RefreshQuotes();
    }

    public void UpdateState(TradingConsoleUiState state)
    {
        _state = state;
        _account.Text = $"Card: {state.CardName}";
        _balance.Text = $"Available balance: {state.Balance} spesos";
        _news.Text = state.News;
        RefreshQuotes();
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
            _quotes.AddChild(line);

            var recent = quote.History.Length > 12 ? quote.History[^12..] : quote.History;
            _quotes.AddChild(new Label { Text = "Recent quotes: " + string.Join(" / ", recent) });
        }
    }

    private void SendOrder(string symbol, string quantityText, bool buy)
    {
        if (int.TryParse(quantityText, out var qty) && qty is >= 1 and <= 1000)
            OnTrade?.Invoke(symbol, qty, buy);
    }
}
