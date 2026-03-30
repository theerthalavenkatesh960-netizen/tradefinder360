namespace TradingSystem.Api.Strategy.Models;

public sealed class OpeningRange
{
    // Highest price of the 09:15 5-min candle
    public decimal High { get; init; }

    // Lowest price of the 09:15 5-min candle
    public decimal Low { get; init; }

    // Timestamp of the 09:15 candle (must equal 09:15:00 IST exactly)
    public DateTime CapturedAt { get; init; }

    // Absolute width of the range
    public decimal Width => High - Low;

    // Midpoint of the range
    public decimal MidPoint => (High + Low) / 2m;

    // Width as a fraction of the midpoint price
    public decimal WidthPct => MidPoint == 0m ? 0m : Width / MidPoint;
}
