using TradingSystem.Api.Strategy.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface IRsiFilter
{
    /// <summary>
    /// Fetches 14-period RSI on the 5-min chart.
    /// This is a SOFT filter — failure reduces confidence score but does NOT block the signal.
    ///
    /// Bullish pass: RSI &gt; StrategyConfig.RsiBullThreshold (default 55)
    /// Bearish pass: RSI &lt; StrategyConfig.RsiBearThreshold (default 45)
    ///
    /// Returns (passed: bool, rsiValue: decimal).
    /// Returns (false, 0) if RSI data is unavailable — do not throw.
    /// </summary>
    Task<(bool Passed, decimal RsiValue)> EvaluateAsync(
        string symbol,
        Direction direction,
        IntraDayStrategyConfig config,
        CancellationToken ct = default);
}
