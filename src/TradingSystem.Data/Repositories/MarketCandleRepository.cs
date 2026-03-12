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
        // If requesting 1-minute candles, return directly from database
        if (timeframeMinutes == 1)
        {
            return await GetOneMinuteCandlesAsync(instrumentId, fromDate, toDate, cancellationToken);
        }

        // For other timeframes, aggregate 1-minute candles
        return await AggregateToTimeframeAsync(instrumentId, timeframeMinutes, fromDate, toDate, cancellationToken);
    }

    public async Task<List<DateRange>> GetMissingDataRangesAsync(
        int instrumentId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        var missingRanges = new List<DateRange>();
        
        // Get all dates that have data
        var datesWithData = await _dbSet
            .Where(c => c.InstrumentId == instrumentId 
                     && c.TimeframeMinutes == 1
                     && c.Timestamp >= fromDate
                     && c.Timestamp <= toDate)
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

    private async Task<IReadOnlyList<MarketCandle>> GetOneMinuteCandlesAsync(
        int instrumentId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken)
    {
        return await _dbSet
            .Where(c => c.InstrumentId == instrumentId 
                     && c.TimeframeMinutes == 1
                     && c.Timestamp >= fromDate
                     && c.Timestamp <= toDate)
            .OrderBy(c => c.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<MarketCandle>> AggregateToTimeframeAsync(
    int instrumentId,
    int timeframeMinutes,
    DateTimeOffset fromDate,
    DateTimeOffset toDate,
    CancellationToken cancellationToken)
    {
        fromDate = fromDate.ToUniversalTime();
        toDate   = toDate.ToUniversalTime();

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
    /// Example for 15-min timeframe:
    /// - 2024-01-15 09:15 → 2024-01-15 09:15
    /// - 2024-01-15 09:28 → 2024-01-15 09:15
    /// - 2024-01-15 09:30 → 2024-01-15 09:30
    /// - 2024-01-15 15:29 → 2024-01-15 15:15
    /// </summary>
    private static DateTime GetTimeframeBucket(DateTimeOffset timestamp, int timeframeMinutes)
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
                DateTimeKind.Unspecified);
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
            DateTimeKind.Unspecified);
    }

    public async Task<MarketCandle?> GetLatestCandleAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default)
    {
        if (timeframeMinutes == 1)
        {
            return await _dbSet
                .Where(c => c.InstrumentId == instrumentId && c.TimeframeMinutes == 1)
                .OrderByDescending(c => c.Timestamp)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        // For other timeframes, get last 2 days of data and aggregate
        var today = DateTime.Today;
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

        // Validate: Only 1-minute candles should be stored
        var invalidCandles = candleList.Where(c => c.TimeframeMinutes != 1).ToList();
        if (invalidCandles.Any())
        {
            throw new InvalidOperationException(
                $"Only 1-minute candles can be stored in the database. " +
                $"Found {invalidCandles.Count} candle(s) with timeframe != 1 minute. " +
                $"Other timeframes are calculated on-the-fly.");
        }

        var instrumentIds = candleList.Select(c => c.InstrumentId).Distinct().ToList();
        var timestamps = candleList.Select(c => c.Timestamp).Distinct().ToList();

        // Fetch existing candles for upsert logic
        var existingCandles = await _dbSet
            .Where(c => instrumentIds.Contains(c.InstrumentId)
                     && c.TimeframeMinutes == 1
                     && timestamps.Contains(c.Timestamp))
            .ToDictionaryAsync(
                c => $"{c.InstrumentId}_{c.Timestamp:yyyyMMddHHmmss}",
                cancellationToken);

        var toAdd = new List<MarketCandle>();
        var toUpdate = new List<MarketCandle>();
        var now = DateTime.UtcNow;

        foreach (var candle in candleList)
        {
            var key = $"{candle.InstrumentId}_{candle.Timestamp:yyyyMMddHHmmss}";

            if (existingCandles.TryGetValue(key, out var existing))
            {
                // Update existing candle (in case of corrections)
                existing.Open = candle.Open;
                existing.High = candle.High;
                existing.Low = candle.Low;
                existing.Close = candle.Close;
                existing.Volume = candle.Volume;
                toUpdate.Add(existing);
            }
            else
            {
                // Add new candle
                candle.CreatedAt = now;
                candle.TimeframeMinutes = 1; // Ensure it's 1-minute
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
}