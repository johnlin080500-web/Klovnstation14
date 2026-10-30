using Content.Shared._KS14.Trading;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Trading;

public sealed partial class TradingConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private TradingConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<TradingConsoleWindow>();
        _window.OnTrade += (symbol, qty, buy) => SendMessage(new TradingOrderMessage(symbol, qty, buy));
        _window.OnWithdraw += amount => SendMessage(new TradingWithdrawMessage(amount));
        _window.OnCardAction += () => SendMessage(new ItemSlotButtonPressedEvent("trading_id"));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TradingConsoleUiState trade)
            _window?.UpdateState(trade);
    }
}
