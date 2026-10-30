using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Trading;

[Serializable, NetSerializable]
public enum TradingConsoleUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class TradingQuote
{
    public string Symbol;
    public string Name;
    public int Price;
    public int Held;
    public int Liquidity;
    public int Volume;
    public long MarketCap;
    public int Spread;
    public int[] History;

    public TradingQuote(string symbol, string name, int price, int held, int liquidity, int volume, long marketCap, int spread, int[] history)
    {
        Symbol = symbol;
        Name = name;
        Price = price;
        Held = held;
        Liquidity = liquidity;
        Volume = volume;
        MarketCap = marketCap;
        Spread = spread;
        History = history;
    }
}

[Serializable, NetSerializable]
public sealed class TradingConsoleUiState : BoundUserInterfaceState
{
    public string CardName;
    public int Balance;
    public TradingQuote[] Quotes;
    public string News;

    public TradingConsoleUiState(string cardName, int balance, TradingQuote[] quotes, string news)
    {
        CardName = cardName;
        Balance = balance;
        Quotes = quotes;
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
public sealed class TradingWithdrawMessage : BoundUserInterfaceMessage
{
    public readonly int Amount;

    public TradingWithdrawMessage(int amount)
    {
        Amount = amount;
    }
}
