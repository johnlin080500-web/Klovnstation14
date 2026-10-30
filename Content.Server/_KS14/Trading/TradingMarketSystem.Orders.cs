using System;
using System.Linq;
using Content.Shared._KS14.Trading;

namespace Content.Server._KS14.Trading;

/// <summary>
/// Order book for the fictional market-maker exchange. Orders are all-or-nothing:
/// an order remains pending when liquidity is insufficient or the stock is halted.
/// Buy limits escrow credits; sell limits and stop-losses reserve shares.
/// </summary>
public sealed partial class TradingMarketSystem
{
    private void OnOrder(Entity<TradingConsoleComponent> ent, ref TradingOrderMessage args)
    {
        if (args.Actor is not { Valid: true } ||
            !TryGetAccount(ent.Owner, out var account, out _) ||
            !_assets.TryGetValue(args.Symbol, out var asset) ||
            args.Quantity is < 1 or > MaxOrderQuantity ||
            IsHalted(asset))
            return;

        if (args.Buy)
            BuyShares(account, asset, args.Quantity, Ask(asset));
        else
            SellShares(account, asset, args.Quantity, Bid(asset));

        UpdateAll();
    }

    private bool BuyShares(Account account, Asset asset, int quantity, int price, bool prepaid = false)
    {
        var cost = (long) price * quantity;
        account.Positions.TryGetValue(asset.Symbol, out var held);
        if (quantity > asset.Liquidity ||
            held > int.MaxValue - quantity ||
            (!prepaid && account.Balance < cost))
            return false;

        if (!prepaid)
            account.Balance -= cost;

        account.Positions[asset.Symbol] = held + quantity;
        account.CostBasis.TryGetValue(asset.Symbol, out var basis);
        account.CostBasis[asset.Symbol] = basis + cost;
        asset.Liquidity -= quantity;
        asset.Volume = (int) Math.Min(int.MaxValue, (long) asset.Volume + quantity);
        return true;
    }

    private bool SellShares(Account account, Asset asset, int quantity, int price, bool reservedOrder = false)
    {
        account.Positions.TryGetValue(asset.Symbol, out var held);
        account.ReservedShares.TryGetValue(asset.Symbol, out var locked);
        var available = held - locked;
        if (quantity <= 0 || held < quantity ||
            (!reservedOrder && available < quantity) ||
            (reservedOrder && locked < quantity))
            return false;

        var proceeds = (long) price * quantity;
        if (account.Balance > long.MaxValue - proceeds)
            return false;

        account.CostBasis.TryGetValue(asset.Symbol, out var basis);
        var deductedCost = held == quantity ? basis : basis * quantity / held;
        account.CostBasis[asset.Symbol] = basis - deductedCost;
        account.Positions[asset.Symbol] = held - quantity;
        if (reservedOrder)
            account.ReservedShares[asset.Symbol] = locked - quantity;

        account.RealizedProfit += proceeds - deductedCost;
        account.Balance += proceeds;
        asset.Liquidity = Math.Min(5000, asset.Liquidity + quantity);
        asset.Volume = (int) Math.Min(int.MaxValue, (long) asset.Volume + quantity);
        return true;
    }

    private void OnPlacePendingOrder(Entity<TradingConsoleComponent> ent, ref TradingPlacePendingOrderMessage args)
    {
        if (args.Actor is not { Valid: true } ||
            !TryGetAccount(ent.Owner, out var account, out _) ||
            !_assets.TryGetValue(args.Symbol, out var asset) ||
            args.Quantity is < 1 or > MaxOrderQuantity ||
            args.TriggerPrice is < 1 or > PriceCeiling ||
            args.DurationMinutes is < 1 or > 30 ||
            account.Orders.Count >= MaxPendingPerAccount ||
            args.OrderType is not (TradingOrderType.LimitBuy or TradingOrderType.LimitSell or TradingOrderType.StopLoss))
            return;

        var order = new PendingOrder
        {
            Id = _nextOrderId++,
            Symbol = asset.Symbol,
            Type = args.OrderType,
            TriggerPrice = args.TriggerPrice,
            Quantity = args.Quantity,
            ExpiresAt = _timing.CurTime + TimeSpan.FromMinutes(args.DurationMinutes)
        };

        if (args.OrderType == TradingOrderType.LimitBuy)
        {
            // Reserve the maximum payable at the limit. Unspent funds are refunded
            // on execution, cancellation or expiration.
            var reservation = (long) args.TriggerPrice * args.Quantity;
            if (reservation > account.Balance)
                return;
            account.Balance -= reservation;
            account.ReservedCash += reservation;
            order.ReservedCash = reservation;
        }
        else
        {
            account.Positions.TryGetValue(asset.Symbol, out var held);
            account.ReservedShares.TryGetValue(asset.Symbol, out var locked);
            if (held - locked < args.Quantity)
                return;
            account.ReservedShares[asset.Symbol] = locked + args.Quantity;
        }

        account.Orders.Add(order);
        UpdateAll();
    }

    private void OnCancelPendingOrder(Entity<TradingConsoleComponent> ent, ref TradingCancelPendingOrderMessage args)
    {
        if (args.Actor is not { Valid: true } ||
            !TryGetAccount(ent.Owner, out var account, out _))
            return;

        var order = account.Orders.FirstOrDefault(o => o.Id == args.OrderId);
        if (order == null)
            return;

        ReleaseOrder(account, order);
        account.Orders.Remove(order);
        UpdateAll();
    }

    private void ReleaseOrder(Account account, PendingOrder order)
    {
        if (order.Type == TradingOrderType.LimitBuy)
        {
            account.Balance += order.ReservedCash;
            account.ReservedCash -= order.ReservedCash;
        }
        else
        {
            account.ReservedShares.TryGetValue(order.Symbol, out var locked);
            account.ReservedShares[order.Symbol] = Math.Max(0, locked - order.Quantity);
        }
    }

    private void ProcessPendingOrders()
    {
        foreach (var account in _accounts.Values)
        {
            for (var i = account.Orders.Count - 1; i >= 0; i--)
            {
                var order = account.Orders[i];
                if (_timing.CurTime >= order.ExpiresAt)
                {
                    ReleaseOrder(account, order);
                    account.Orders.RemoveAt(i);
                    continue;
                }

                if (!_assets.TryGetValue(order.Symbol, out var asset) || IsHalted(asset))
                    continue;

                var filled = false;
                switch (order.Type)
                {
                    case TradingOrderType.LimitBuy when Ask(asset) <= order.TriggerPrice:
                    {
                        var ask = Ask(asset);
                        var actualCost = (long) ask * order.Quantity;
                        if (actualCost <= order.ReservedCash &&
                            BuyShares(account, asset, order.Quantity, ask, prepaid: true))
                        {
                            account.ReservedCash -= order.ReservedCash;
                            account.Balance += order.ReservedCash - actualCost;
                            filled = true;
                        }
                        break;
                    }
                    case TradingOrderType.LimitSell when Bid(asset) >= order.TriggerPrice:
                    case TradingOrderType.StopLoss when Bid(asset) <= order.TriggerPrice:
                        filled = SellShares(account, asset, order.Quantity, Bid(asset), reservedOrder: true);
                        break;
                }

                if (filled)
                    account.Orders.RemoveAt(i);
            }
        }
    }
}
