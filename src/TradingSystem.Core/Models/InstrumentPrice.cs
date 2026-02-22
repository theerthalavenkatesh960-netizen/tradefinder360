namespace TradingSystem.Core.Models;

public class InstrumentPrice
{
    public long Id { get; set; }
    public int InstrumentId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public string Timeframe { get; set; } = "1D";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public TradingInstrument? Instrument { get; set; }
}
