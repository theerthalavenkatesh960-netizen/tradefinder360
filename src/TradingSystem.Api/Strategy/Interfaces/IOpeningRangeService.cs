using TradingSystem.Api.Strategy.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface IOpeningRangeService
{
    /// <summary>
    /// Fetches the single 5-min candle starting at exactly 09:15:00 IST for the given date.
    /// Returns null if:
    ///   (a) the candle is unavailable, OR
    ///   (b) OR.WidthPct is less than StrategyConfig.MinOpeningRangePct.
    /// Logs the rejection reason before returning null.
    /// </summary>
    Task<OpeningRange?> CaptureAsync(string symbol, DateOnly date, CancellationToken ct = default);
}
