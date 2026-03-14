using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface IMarketCandleRepository : ICommonRepository<MarketCandle>
{
    Task<IReadOnlyList<MarketCandle>> GetByInstrumentIdAsync(
        int instrumentId,
        int timeframeMinutes,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    Task<MarketCandle?> GetLatestCandleAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default);

    Task<int> BulkUpsertAsync(
        IEnumerable<MarketCandle> candles,
        CancellationToken cancellationToken = default);

    // New method to get date ranges with missing data
    Task<List<DateRange>> GetMissingDataRangesAsync(
        int instrumentId,
        DateTime fromDate,
        DateTime toDate,
        int timeframeMinutes,
        CancellationToken cancellationToken = default);
    
    // Add to your interface and implement in your repository
    Task<bool> HasAnyDataAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default);

    Task<DateTime?> GetLatestCandleDateAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default);
}

public class DateRange : IEquatable<DateRange>
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public bool Equals(DateRange? other)
    {
        throw new NotImplementedException();
    }
}