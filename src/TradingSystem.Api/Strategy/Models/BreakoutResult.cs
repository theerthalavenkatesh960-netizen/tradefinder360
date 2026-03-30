using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Models;

// Returned by IBreakoutDetector when a valid close-based breakout is confirmed
public sealed class BreakoutResult
{
    public Direction Direction { get; init; }

    // The 1-min candle whose CLOSE crossed the opening range boundary
    public required Candle BreakoutCandle { get; init; }
}
