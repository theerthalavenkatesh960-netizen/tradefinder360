namespace TradingSystem.Api.Strategy.Models;

public sealed class FairValueGap
{
    // Direction this FVG supports (must match breakout direction)
    public Direction Direction { get; init; }

    // Upper boundary of the gap zone
    public decimal GapHigh { get; init; }

    // Lower boundary of the gap zone
    public decimal GapLow { get; init; }

    // Timestamp of the 3rd candle (when FVG was confirmed)
    public DateTime FormedAt { get; init; }

    // Absolute size of the gap
    public decimal Size => GapHigh - GapLow;

    // Returns true if the given price is inside the gap zone (inclusive)
    public bool Contains(decimal price) => price >= GapLow && price <= GapHigh;
}
