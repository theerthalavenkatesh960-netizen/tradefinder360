using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class MarketCandleRepository : CommonRepository<MarketCandle>, IMarketCandleRepository
{
    // Market constants (IST)
    private const int MarketOpenHour = 9;
    private const int MarketOpenMinute = 15;
    private const int MarketOpenMinuteOfDay = (9 * 60) + 15; // 555 minutes from midnight

    // Base stored timeframes (physically stored in partitions)
    private static readonly HashSet<int> BaseTimeframes = new() { 1, 15, 1440 };
    
    // Derived timeframes (calculated from 1m base data)
    private static readonly HashSet<int> DerivedTimeframes = new() { 5, 30, 60 };
    
    // All supported timeframes
    private static readonly HashSet<int> AllSupportedTimeframes = 
        new(BaseTimeframes.Union(DerivedTimeframes));

    public MarketCandleRepository(TradingDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<MarketCandle>> GetByInstrumentIdAsync(
        int instrumentId,
        int timeframeMinutes,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        // Validate timeframe
        if (!AllSupportedTimeframes.Contains(timeframeMinutes))
        {
            throw new ArgumentException(
                $"Unsupported timeframe: {timeframeMinutes}. " +
                $"Supported: 1m, 5m, 15m, 30m, 60m, 1440m (1d).",
                nameof(timeframeMinutes));
        }

        // If it's a base timeframe (1m, 15m, 1d), query directly from partitions
        if (BaseTimeframes.Contains(timeframeMinutes))
        {
            return await GetCandlesFromPartitionAsync(
                instrumentId,
                timeframeMinutes,
                fromDate,
                toDate,
                cancellationToken);
        }

        // For derived timeframes (5m, 30m, 60m), aggregate from 1m data
        return await AggregateFromOneMinuteAsync(
            instrumentId,
            timeframeMinutes,
            fromDate,
            toDate,
            cancellationToken);
    }

    /// <summary>
    /// Query candles directly from tiered partitions (1m, 15m, or 1d).
    /// Uses partition pruning for optimal performance.
    /// </summary>
    private async Task<IReadOnlyList<MarketCandle>> GetCandlesFromPartitionAsync(
        int instrumentId,
        int timeframeMinutes,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(c => c.InstrumentId == instrumentId
                     && c.TimeframeMinutes == timeframeMinutes
                     && c.Timestamp >= fromDate
                     && c.Timestamp <= toDate)
            .OrderBy(c => c.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Aggregate 1-minute candles into derived timeframes (5m, 30m, 60m).
    /// This avoids storing redundant data while providing flexibility.
    /// </summary>
    private async Task<IReadOnlyList<MarketCandle>> AggregateFromOneMinuteAsync(
        int instrumentId,
        int timeframeMinutes,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken)
    {
        // Fetch 1-minute base data
        var oneMinuteCandles = await _dbSet
            .Where(c => c.InstrumentId == instrumentId
                     && c.TimeframeMinutes == 1
                     && c.Timestamp >= fromDate
                     && c.Timestamp <= toDate)
            .OrderBy(c => c.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (!oneMinuteCandles.Any())
            return Array.Empty<MarketCandle>();

        // Group by timeframe bucket and aggregate
        var aggregated = oneMinuteCandles
            .GroupBy(c => new
            {
                Date = c.Timestamp.UtcDateTime.Date,
                BucketTime = GetTimeframeBucket(c.Timestamp.UtcDateTime, timeframeMinutes)
            })
            .Select(g => new MarketCandle
            {
                InstrumentId = instrumentId,
                TimeframeMinutes = timeframeMinutes,
                Timestamp = new DateTimeOffset(g.Key.BucketTime, TimeSpan.Zero),
                Open = g.OrderBy(x => x.Timestamp).First().Open,
                High = g.Max(x => x.High),
                Low = g.Min(x => x.Low),
                Close = g.OrderByDescending(x => x.Timestamp).First().Close,
                Volume = g.Sum(x => x.Volume),
                CreatedAt = DateTimeOffset.UtcNow
            })
            .OrderBy(c => c.Timestamp)
            .ToList();

        return aggregated;
    }

    /// <summary>
    /// Calculate the timeframe bucket start time for a given timestamp.
    /// Aligns to market open at 9:15 AM IST.
    /// Ensures candles from different days are never mixed.
    /// 
    /// Example for 5-min timeframe:
    /// - 2024-01-15 09:15 → 2024-01-15 09:15
    /// - 2024-01-15 09:18 → 2024-01-15 09:15
    /// - 2024-01-15 09:20 → 2024-01-15 09:20
    /// </summary>
    private static DateTime GetTimeframeBucket(DateTime timestamp, int timeframeMinutes)
    {
        // Calculate minutes from midnight for this timestamp
        var minutesFromMidnight = (timestamp.Hour * 60) + timestamp.Minute;

        // Calculate minutes from market open (9:15 AM)
        var minutesFromMarketOpen = minutesFromMidnight - MarketOpenMinuteOfDay;

        // If before market open, snap to market open (edge case)
        if (minutesFromMarketOpen < 0)
        {
            return new DateTime(
                timestamp.Year,
                timestamp.Month,
                timestamp.Day,
                MarketOpenHour,
                MarketOpenMinute,
                0,
                DateTimeKind.Utc);
        }

        // Calculate which bucket this falls into (0-indexed from market open)
        var bucketIndex = minutesFromMarketOpen / timeframeMinutes;

        // Calculate the start time of this bucket
        var bucketStartMinutes = MarketOpenMinuteOfDay + (bucketIndex * timeframeMinutes);
        var bucketHour = bucketStartMinutes / 60;
        var bucketMinute = bucketStartMinutes % 60;

        return new DateTime(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            bucketHour,
            bucketMinute,
            0,
            DateTimeKind.Utc);
    }

    public async Task<List<DateRange>> GetMissingDataRangesAsync(
        int instrumentId,
        DateTime fromDate,
        DateTime toDate,
        int timeframeMinutes = 1,
        CancellationToken cancellationToken = default)
    {
        var missingRanges = new List<DateRange>();
        
        // For derived timeframes, check 1m base data
        var checkTimeframe = DerivedTimeframes.Contains(timeframeMinutes) ? 1 : timeframeMinutes;
        
        // Get all dates that have data for the specified timeframe
        var datesWithData = await _dbSet
            .Where(c => c.InstrumentId == instrumentId 
                     && c.TimeframeMinutes == checkTimeframe
                     && c.Timestamp >= fromDate
                     && c.Timestamp <= toDate)
            .AsNoTracking()
            .Select(c => c.Timestamp.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(cancellationToken);

        if (!datesWithData.Any())
        {
            // No data at all, entire range is missing
            missingRanges.Add(new DateRange 
            { 
                FromDate = fromDate, 
                ToDate = toDate 
            });
            return missingRanges;
        }

        // Check for gaps at the beginning
        if (datesWithData.First().Date > fromDate.Date)
        {
            missingRanges.Add(new DateRange
            {
                FromDate = fromDate,
                ToDate = datesWithData.First().AddDays(-1)
            });
        }

        // Check for gaps in the middle
        for (int i = 0; i < datesWithData.Count - 1; i++)
        {
            var currentDate = datesWithData[i];
            var nextDate = datesWithData[i + 1];
            var daysDiff = (nextDate - currentDate).Days;

            if (daysDiff > 1)
            {
                missingRanges.Add(new DateRange
                {
                    FromDate = currentDate.AddDays(1),
                    ToDate = nextDate.AddDays(-1)
                });
            }
        }

        // Check for gaps at the end
        if (datesWithData.Last().Date < toDate.Date)
        {
            missingRanges.Add(new DateRange
            {
                FromDate = datesWithData.Last().AddDays(1),
                ToDate = toDate
            });
        }

        return missingRanges;
    }

    public async Task<MarketCandle?> GetLatestCandleAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default)
    {
        // Validate timeframe
        if (!AllSupportedTimeframes.Contains(timeframeMinutes))
        {
            throw new ArgumentException(
                $"Unsupported timeframe: {timeframeMinutes}. " +
                $"Supported: 1m, 5m, 15m, 30m, 60m, 1440m (1d).",
                nameof(timeframeMinutes));
        }

        // If base timeframe, query directly
        if (BaseTimeframes.Contains(timeframeMinutes))
        {
            return await _dbSet
                .Where(c => c.InstrumentId == instrumentId 
                         && c.TimeframeMinutes == timeframeMinutes)
                .OrderByDescending(c => c.Timestamp)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        // For derived timeframes, get last 2 days of 1m data and aggregate
        var today = DateTime.UtcNow.Date;
        var fromDate = today.AddDays(-2);

        var recentCandles = await GetByInstrumentIdAsync(
            instrumentId,
            timeframeMinutes,
            fromDate,
            today.AddDays(1),
            cancellationToken);

        return recentCandles.OrderByDescending(c => c.Timestamp).FirstOrDefault();
    }

    public async Task<int> BulkUpsertAsync(
        IEnumerable<MarketCandle> candles,
        CancellationToken cancellationToken = default)
    {
        var candleList = candles.ToList();
        if (!candleList.Any())
        {
            return 0;
        }

        // Validate: Only base timeframes can be stored (1, 15, 1440)
        var invalidCandles = candleList.Where(c => !BaseTimeframes.Contains(c.TimeframeMinutes)).ToList();
        if (invalidCandles.Any())
        {
            throw new InvalidOperationException(
                $"Only base timeframes (1m, 15m, 1d) can be stored. " +
                $"Derived timeframes (5m, 30m, 60m) are calculated on-the-fly. " +
                $"Found {invalidCandles.Count} candle(s) with invalid timeframe.");
        }

        var instrumentIds = candleList.Select(c => c.InstrumentId).Distinct().ToList();
        var timestamps = candleList.Select(c => c.Timestamp).Distinct().ToList();
        var timeframes = candleList.Select(c => c.TimeframeMinutes).Distinct().ToList();

        // CRITICAL FIX: Use AsNoTracking for read-only query to avoid tracking overhead
        var existingCandles = await _dbSet
            .AsNoTracking()
            .Where(c => instrumentIds.Contains(c.InstrumentId)
                     && timeframes.Contains(c.TimeframeMinutes)
                     && timestamps.Contains(c.Timestamp))
            .ToDictionaryAsync(
                c => $"{c.InstrumentId}_{c.TimeframeMinutes}_{c.Timestamp:yyyyMMddHHmmss}",
                cancellationToken);

        var toAdd = new List<MarketCandle>();
        var toUpdate = new List<MarketCandle>();
        var now = DateTimeOffset.UtcNow;

        foreach (var candle in candleList)
        {
            var key = $"{candle.InstrumentId}_{candle.TimeframeMinutes}_{candle.Timestamp:yyyyMMddHHmmss}";

            if (existingCandles.TryGetValue(key, out var existing))
            {
                // Update existing candle (must attach to context first since we used AsNoTracking)
                var tracked = _dbSet.Local.FirstOrDefault(e => 
                    e.InstrumentId == existing.InstrumentId &&
                    e.TimeframeMinutes == existing.TimeframeMinutes &&
                    e.Timestamp == existing.Timestamp);

                if (tracked == null)
                {
                    _context.Attach(existing);
                    tracked = existing;
                }

                tracked.Open = candle.Open;
                tracked.High = candle.High;
                tracked.Low = candle.Low;
                tracked.Close = candle.Close;
                tracked.Volume = candle.Volume;
                toUpdate.Add(tracked);
            }
            else
            {
                // Add new candle
                candle.CreatedAt = now;
                toAdd.Add(candle);
            }
        }

        if (toAdd.Any())
        {
            await _dbSet.AddRangeAsync(toAdd, cancellationToken);
        }

        if (toUpdate.Any())
        {
            _dbSet.UpdateRange(toUpdate);
        }

        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasAnyDataAsync(int instrumentId, int timeframeMinutes, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .AnyAsync(
                c => c.InstrumentId == instrumentId
                && c.TimeframeMinutes == timeframeMinutes,
                cancellationToken);
    }

    /// <summary>
    /// FIX: Return UtcDateTime to preserve timezone consistency
    /// </summary>
    public async Task<DateTime?> GetLatestCandleDateAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default)
    {
        var latest = await _dbSet
            .AsNoTracking()
            .Where(c => c.InstrumentId    == instrumentId
                    && c.TimeframeMinutes == timeframeMinutes)
            .MaxAsync(c => (DateTime?)c.Timestamp.UtcDateTime, cancellationToken);

        return latest;
    }
}