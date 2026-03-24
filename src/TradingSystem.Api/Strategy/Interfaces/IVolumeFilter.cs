using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface IVolumeFilter
{
    /// <summary>
    /// Returns true if breakoutCandle.Volume is strictly greater than
    /// the simple average volume of the prior [lookback] 1-min candles
    /// before breakoutCandle.Timestamp on the same symbol.
    /// Returns false (not throw) if insufficient candle history.
    /// </summary>
    Task<bool> IsConfirmedAsync(
        string symbol,
        Candle breakoutCandle,
        int lookback,
        CancellationToken ct = default);
}
