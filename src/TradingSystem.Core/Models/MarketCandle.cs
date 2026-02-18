namespace TradingSystem.Core.Models;

public class MarketCandle
{
    public long Id { get; set; }
    public string InstrumentKey { get; set; } = string.Empty;
    public int TimeframeMinutes { get; set; }
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public DateTime CreatedAt { get; set; }

    public Candle ToCandle() => new Candle
    {
        Timestamp = Timestamp,
        Open = Open,
        High = High,
        Low = Low,
        Close = Close,
        Volume = Volume,
        TimeframeMinutes = TimeframeMinutes
    };
}
