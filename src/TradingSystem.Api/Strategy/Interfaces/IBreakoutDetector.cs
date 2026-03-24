using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface IBreakoutDetector
{
    /// <summary>
    /// Evaluates a single 1-min candle against the opening range.
    /// Returns a BreakoutResult if:
    ///   - candle.Close > or.High  (bullish), OR
    ///   - candle.Close < or.Low   (bearish)
    /// Returns null if:
    ///   - only the wick crosses (close does not cross)
    ///   - candle time is outside TradeWindowStart–TradeWindowEnd
    /// </summary>
    BreakoutResult? Detect(Candle candle, OpeningRange or, IntraDayStrategyConfig config);
}
