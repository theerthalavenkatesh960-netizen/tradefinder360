namespace TradingSystem.Upstox.Models;

public class UpstoxQuoteResponse
{
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, UpstoxQuoteData> Data { get; set; } = new();
}

public class UpstoxQuoteData
{
    public string? Symbol { get; set; }
    public string? Instrument_Token { get; set; }
    public decimal Last_Price { get; set; }
    public long Volume { get; set; }
    public DateTime Timestamp { get; set; }
    public UpstoxOhlc? Ohlc { get; set; }
}

public class UpstoxOhlc
{
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}