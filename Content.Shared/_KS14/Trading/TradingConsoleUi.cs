using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Trading;

[Serializable, NetSerializable]
public enum TradingConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum TradingOrderType : byte
{
    LimitBuy,
    LimitSell,
    StopLoss
}

[Serializable, NetSerializable]
public sealed class TradingOrderSummary
{
    public int Id;
    public string Symbol;
    public TradingOrderType Type;
    public int Quantity;
    public int TriggerPrice;
    public int ExpiresInSeconds;

    public TradingOrderSummary(int id, string symbol, TradingOrderType type, int quantity, int triggerPrice, int expiresInSeconds)
    {
        Id = id;
        Symbol = symbol;
        Type = type;
        Quantity = quantity;
        TriggerPrice = triggerPrice;
        ExpiresInSeconds = expiresInSeconds;
    }
}

[Serializable, NetSerializable]
public sealed class TradingNewsEntry
{
    public string Symbol;
    public string Title;
    public string Description;
    public int PublishedSeconds;
    public int? ResolvedSeconds;
    public float? ActualImpactPercent;

    public TradingNewsEntry(string symbol, string title, string description, int publishedSeconds,
        int? resolvedSeconds = null, float? actualImpactPercent = null)
    {
        Symbol = symbol;
        Title = title;
        Description = description;
        PublishedSeconds = publishedSeconds;
        ResolvedSeconds = resolvedSeconds;
        ActualImpactPercent = actualImpactPercent;
    }
}

[Serializable, NetSerializable]
public sealed class TradingQuote
{
    public string Symbol;
    public string Name;
    public int Price;
    public int Bid;
    public int Ask;
    public int Held;
    public int ReservedShares;
    public int Liquidity;
    public int Volume;
    public long MarketCap;
    public int Spread;
    public float AverageCost;
    public long UnrealizedProfit;
    public float VolatilityPercent;
    public float ChangePercent;
    public int HaltSeconds;
    public int[] History;
    public int[] HistoryTimes;

    public TradingQuote(string symbol, string name, int price, int bid, int ask, int held, int reservedShares,
        int liquidity, int volume, long marketCap, int spread, float averageCost, long unrealizedProfit,
        float volatilityPercent, float changePercent, int haltSeconds, int[] history, int[] historyTimes)
    {
        Symbol = symbol;
        Name = name;
        Price = price;
        Bid = bid;
        Ask = ask;
        Held = held;
        ReservedShares = reservedShares;
        Liquidity = liquidity;
        Volume = volume;
        MarketCap = marketCap;
        Spread = spread;
        AverageCost = averageCost;
        UnrealizedProfit = unrealizedProfit;
        VolatilityPercent = volatilityPercent;
        ChangePercent = changePercent;
        HaltSeconds = haltSeconds;
        History = history;
        HistoryTimes = historyTimes;
    }
}

[Serializable, NetSerializable]
public sealed class TradingConsoleUiState : BoundUserInterfaceState
{
    public string CardName;
    public bool HasCard;
    public int Balance;
    public long ReservedCash;
    public long PortfolioValue;
    public long UnrealizedProfit;
    public long RealizedProfit;
    public TradingQuote[] Quotes;
    public TradingOrderSummary[] Orders;
    public TradingNewsEntry[] NewsHistory;
    public string News;

    public TradingConsoleUiState(string cardName, bool hasCard, int balance, long reservedCash,
        long portfolioValue, long unrealizedProfit, long realizedProfit, TradingQuote[] quotes,
        TradingOrderSummary[] orders, TradingNewsEntry[] newsHistory, string news)
    {
        CardName = cardName;
        HasCard = hasCard;
        Balance = balance;
        ReservedCash = reservedCash;
        PortfolioValue = portfolioValue;
        UnrealizedProfit = unrealizedProfit;
        RealizedProfit = realizedProfit;
        Quotes = quotes;
        Orders = orders;
        NewsHistory = newsHistory;
        News = news;
    }
}

[Serializable, NetSerializable]
public sealed class TradingOrderMessage : BoundUserInterfaceMessage
{
    public readonly string Symbol;
    public readonly int Quantity;
    public readonly bool Buy;

    public TradingOrderMessage(string symbol, int quantity, bool buy)
    {
        Symbol = symbol;
        Quantity = quantity;
        Buy = buy;
    }
}

[Serializable, NetSerializable]
public sealed class TradingPlacePendingOrderMessage : BoundUserInterfaceMessage
{
    public readonly string Symbol;
    public readonly TradingOrderType OrderType;
    public readonly int Quantity;
    public readonly int TriggerPrice;
    public readonly int DurationMinutes;

    public TradingPlacePendingOrderMessage(string symbol, TradingOrderType orderType, int quantity, int triggerPrice, int durationMinutes)
    {
        Symbol = symbol;
        OrderType = orderType;
        Quantity = quantity;
        TriggerPrice = triggerPrice;
        DurationMinutes = durationMinutes;
    }
}

[Serializable, NetSerializable]
public sealed class TradingCancelPendingOrderMessage : BoundUserInterfaceMessage
{
    public readonly int OrderId;

    public TradingCancelPendingOrderMessage(int orderId)
    {
        OrderId = orderId;
    }
}

[Serializable, NetSerializable]
public sealed class TradingWithdrawMessage : BoundUserInterfaceMessage
{
    public readonly int Amount;

    public TradingWithdrawMessage(int amount)
    {
        Amount = amount;
    }
}
