namespace TradingSystem.Core.Models;

public class MarketCandle
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    public long Id { get; set; }
    public int InstrumentId { get; set; }
    public int TimeframeMinutes { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public TradingInstrument? Instrument { get; set; }
    public Candle ToCandle() => new Candle
    {
        Timestamp = TimeZoneInfo.ConvertTime(Timestamp, Ist),
        Open = Open,
        High = High,
        Low = Low,
        Close = Close,
        Volume = Volume,
        TimeframeMinutes = TimeframeMinutes
    };
}
