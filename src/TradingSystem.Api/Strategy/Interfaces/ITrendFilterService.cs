using TradingSystem.Api.Strategy.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface ITrendFilterService
{
    /// <summary>
    /// Fetches the 20 EMA on the 15-min chart for the given symbol.
    /// Compares current 15-min EMA vs the previous 15-min candle's EMA to determine slope.
    ///
    /// Bullish pass condition (both must be true):
    ///   (1) currentPrice &gt; currentEma15
    ///   (2) currentEma15 &gt; previousEma15  (slope is upward)
    ///
    /// Bearish pass condition (both must be true):
    ///   (1) currentPrice &lt; currentEma15
    ///   (2) currentEma15 &lt; previousEma15  (slope is downward)
    ///
    /// Returns false (not throw) if EMA data is unavailable.
    /// </summary>
    Task<bool> IsAlignedAsync(
        string symbol,
        decimal currentPrice,
        Direction direction,
        CancellationToken ct = default);
}
