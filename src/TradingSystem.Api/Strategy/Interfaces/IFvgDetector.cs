using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface IFvgDetector
{
    /// <summary>
    /// Scans a list of 1-min candles (all AFTER the breakout candle, in chronological order)
    /// for the first valid Fair Value Gap matching the breakout direction.
    ///
    /// Bullish FVG: candles[i-2].High &lt; candles[i].Low
    ///     GapLow  = candles[i-2].High
    ///     GapHigh = candles[i].Low
    ///
    /// Bearish FVG: candles[i-2].Low &gt; candles[i].High
    ///     GapHigh = candles[i-2].Low
    ///     GapLow  = candles[i].High
    ///
    /// A FVG is valid only if:
    ///     (GapHigh - GapLow) / candles[i-1].Close >= minGapPct
    ///
    /// Returns the FIRST valid FVG found, or null if none.
    /// i starts at index 2 (minimum 3 candles required).
    /// </summary>
    FairValueGap? Detect(
        IReadOnlyList<Candle> candlesAfterBreakout,
        Direction breakoutDirection,
        decimal minGapPct);
}
