using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Data.Services.Interfaces;

public interface IIndicatorService
{
    Task SaveAsync(int instrumentId, int timeframeMinutes, IndicatorValues indicators);
    Task BulkSaveAsync(List<IndicatorSnapshot> snapshots, CancellationToken cancellationToken = default);
    Task<IndicatorSnapshot?> GetLatestAsync(int instrumentId, int timeframeMinutes);
    Task<List<IndicatorSnapshot>> GetRecentAsync(int instrumentId, int timeframeMinutes, int count);

    /// <summary>
    /// Returns indicator snapshots within the given UTC date range.
    /// </summary>
    Task<List<IndicatorSnapshot>> GetByDateRangeAsync(
        int instrumentId, int timeframeMinutes,
        DateTimeOffset fromUtc, DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures indicator snapshots exist for all available candle data in the given date range.
    /// Detects missing ranges, calculates indicators from candle history (with proper warmup),
    /// persists new snapshots, and returns the full set for the requested range.
    /// </summary>
    Task<List<IndicatorSnapshot>> EnsureIndicatorsCalculatedAsync(
        int instrumentId, int timeframeMinutes,
        DateTime fromDate, DateTime toDate,
        CancellationToken cancellationToken = default);
}
