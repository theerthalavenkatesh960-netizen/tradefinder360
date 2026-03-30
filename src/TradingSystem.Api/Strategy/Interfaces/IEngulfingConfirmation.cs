using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface IEngulfingConfirmation
{
    /// <summary>
    /// Returns true if currentCandle body engulfs previousCandle body in the given direction.
    ///
    /// Bullish engulf (both conditions required):
    ///   currentCandle.Open  &lt; previousCandle.Low
    ///   currentCandle.Close &gt; previousCandle.High
    ///
    /// Bearish engulf (both conditions required):
    ///   currentCandle.Open  &gt; previousCandle.High
    ///   currentCandle.Close &lt; previousCandle.Low
    ///
    /// Uses candle BODY (open/close), not wicks (high/low).
    /// </summary>
    bool IsEngulfing(Candle previous, Candle current, Direction direction);
}
