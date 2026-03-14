using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class MarketCandleRepository : CommonRepository<MarketCandle>, IMarketCandleRepository
{
    // Single source of truth for IST timezone.
    // All timestamps in DB are stored as IST DateTimeOffset (+05:30).
    // EF/Npgsql normalizes DateTimeOffset to UTC (+00:00) on read,
    // so we must always ConvertTime(..., Ist) before extracting .Date.
    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    // IST market open = 09:15
    private const int MarketOpenHour           = 9;
    private const int MarketOpenMinute         = 15;
    private const int MarketOpenMinuteOfDay    = (9 * 60) + 15; // 555

    // Base timeframes — physically stored in partitions
    private static readonly HashSet<int> BaseTimeframes    = [1, 15, 1440];

    // Derived timeframes — aggregated on-the-fly from 1m base data
    private static readonly HashSet<int> DerivedTimeframes = [5, 30, 60];

    private static readonly HashSet<int> AllSupportedTimeframes =
        [..BaseTimeframes, ..DerivedTimeframes];

    public MarketCandleRepository(TradingDbContext context) : base(context)
    {
    }

    // -------------------------------------------------------------------------
    // READ
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<MarketCandle>> GetByInstrumentIdAsync(
        int instrumentId,
        int timeframeMinutes,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        if (!AllSupportedTimeframes.Contains(timeframeMinutes))
            throw new ArgumentException(
                $"Unsupported timeframe: {timeframeMinutes}. " +
                $"Supported: {string.Join(", ", AllSupportedTimeframes.Order())}m.",
                nameof(timeframeMinutes));

        return BaseTimeframes.Contains(timeframeMinutes)
            ? await GetCandlesFromPartitionAsync(instrumentId, timeframeMinutes, fromDate, toDate, cancellationToken)
            : await AggregateFromOneMinuteAsync(instrumentId, timeframeMinutes, fromDate, toDate, cancellationToken);
    }

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

    private async Task<IReadOnlyList<MarketCandle>> AggregateFromOneMinuteAsync(
        int instrumentId,
        int timeframeMinutes,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken)
    {
        var oneMinuteCandles = await _dbSet
            .Where(c => c.InstrumentId == instrumentId
                     && c.TimeframeMinutes  == 1
                     && c.Timestamp >= fromDate
                     && c.Timestamp <= toDate)
            .OrderBy(c => c.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (oneMinuteCandles.Count == 0)
            return [];

        // FIX: EF/Npgsql returns DateTimeOffset normalized to UTC.
        // Convert to IST before computing the bucket so that:
        //   - .Date gives the correct IST trading date (not UTC date)
        //   - market open alignment (09:15 IST) works correctly
        //
        // Without this fix, candles stored as e.g. 2026-03-13 09:15 +0530
        // come back as 2026-03-13 03:45 +0000. GetTimeframeBucket would then
        // calculate buckets relative to 03:45 UTC instead of 09:15 IST —
        // producing completely wrong bucket assignments.
        var aggregated = oneMinuteCandles
            .GroupBy(c =>
            {
                var istTime = TimeZoneInfo.ConvertTime(c.Timestamp, Ist);
                return new
                {
                    // IST date — ensures candles from different IST trading days
                    // are never merged into the same bucket
                    Date = istTime.Date,
                    BucketTime = GetTimeframeBucket(istTime.DateTime, timeframeMinutes)
                };
            })
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.Timestamp).ToList();
                return new MarketCandle
                {
                    InstrumentId     = instrumentId,
                    TimeframeMinutes = timeframeMinutes,
                    // Store bucket start as IST DateTimeOffset (+05:30)
                    // to be consistent with how base candles are stored
                    Timestamp        = new DateTimeOffset(
                                           g.Key.BucketTime,
                                           TimeSpan.FromHours(5.5)),
                    Open             = ordered.First().Open,
                    High             = g.Max(x => x.High),
                    Low              = g.Min(x => x.Low),
                    Close            = ordered.Last().Close,
                    Volume           = g.Sum(x => x.Volume),
                    CreatedAt        = DateTimeOffset.UtcNow
                };
            })
            .OrderBy(c => c.Timestamp)
            .ToList();

        return aggregated;
    }

    /// <summary>
    /// Calculates the timeframe bucket start time aligned to NSE market open (09:15 IST).
    /// Input MUST be an IST DateTime — do not pass UTC here.
    /// </summary>
    private static DateTime GetTimeframeBucket(DateTime istTimestamp, int timeframeMinutes)
    {
        var minutesFromMidnight  = (istTimestamp.Hour * 60) + istTimestamp.Minute;
        var minutesFromMarketOpen = minutesFromMidnight - MarketOpenMinuteOfDay;

        // Before market open — snap to 09:15
        if (minutesFromMarketOpen < 0)
        {
            return new DateTime(
                istTimestamp.Year, istTimestamp.Month, istTimestamp.Day,
                MarketOpenHour, MarketOpenMinute, 0);
        }

        var bucketIndex        = minutesFromMarketOpen / timeframeMinutes;
        var bucketStartMinutes = MarketOpenMinuteOfDay + (bucketIndex * timeframeMinutes);

        return new DateTime(
            istTimestamp.Year, istTimestamp.Month, istTimestamp.Day,
            bucketStartMinutes / 60,
            bucketStartMinutes % 60,
            0);
    }

    // -------------------------------------------------------------------------
    // MISSING RANGE DETECTION
    // -------------------------------------------------------------------------

    public async Task<List<DateRange>> GetMissingDataRangesAsync(
        int instrumentId,
        DateTime fromDate,
        DateTime toDate,
        int timeframeMinutes = 1,
        CancellationToken cancellationToken = default)
    {
        // Derived timeframes are aggregated from 1m — check 1m base for gaps
        var checkTimeframe = DerivedTimeframes.Contains(timeframeMinutes) ? 1 : timeframeMinutes;

        // FIX: c.Timestamp is DateTimeOffset. EF/Npgsql returns it normalized to UTC.
        // .Date on a UTC-normalized value gives the UTC date, not the IST date.
        //
        // Example: 2026-03-13 00:00:00 +0530 stored in DB
        //          EF returns 2026-03-12 18:30:00 +0000
        //          .Date = 2026-03-12  ← WRONG (should be 2026-03-13)
        //
        // Fix: project to the UTC ticks in the SELECT, then convert in-memory.
        // We can't call TimeZoneInfo.ConvertTime inside an EF query (not translatable),
        // so we fetch the raw DateTimeOffset values and convert after materialization.
        var rawTimestamps = await _dbSet
            .Where(c => c.InstrumentId == instrumentId
                     && c.TimeframeMinutes == checkTimeframe
                     && c.Timestamp >= fromDate
                     && c.Timestamp <= toDate)
            .AsNoTracking()
            .Select(c => c.Timestamp)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Convert each timestamp to IST date in memory
        var datesWithData = rawTimestamps
            .Select(ts => TimeZoneInfo.ConvertTime(ts, Ist).Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var missingRanges = new List<DateRange>();

        if (datesWithData.Count == 0)
        {
            missingRanges.Add(new DateRange { FromDate = fromDate, ToDate = toDate });
            return missingRanges;
        }

        // Gap at the beginning
        if (datesWithData.First() > fromDate.Date)
        {
            missingRanges.Add(new DateRange
            {
                FromDate = fromDate,
                ToDate   = datesWithData.First().AddDays(-1)
            });
        }

        // Gaps in the middle
        for (int i = 0; i < datesWithData.Count - 1; i++)
        {
            var current  = datesWithData[i];
            var next     = datesWithData[i + 1];
            var daysDiff = (next - current).Days;

            if (daysDiff > 1)
            {
                missingRanges.Add(new DateRange
                {
                    FromDate = current.AddDays(1),
                    ToDate   = next.AddDays(-1)
                });
            }
        }

        // Gap at the end
        if (datesWithData.Last() < toDate.Date)
        {
            missingRanges.Add(new DateRange
            {
                FromDate = datesWithData.Last().AddDays(1),
                ToDate   = toDate
            });
        }

        return missingRanges;
    }

    // -------------------------------------------------------------------------
    // LATEST CANDLE
    // -------------------------------------------------------------------------

    public async Task<MarketCandle?> GetLatestCandleAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default)
    {
        if (!AllSupportedTimeframes.Contains(timeframeMinutes))
            throw new ArgumentException(
                $"Unsupported timeframe: {timeframeMinutes}.",
                nameof(timeframeMinutes));

        if (BaseTimeframes.Contains(timeframeMinutes))
        {
            return await _dbSet
                .Where(c => c.InstrumentId    == instrumentId
                         && c.TimeframeMinutes == timeframeMinutes)
                .OrderByDescending(c => c.Timestamp)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Derived timeframe — aggregate last 2 IST trading days from 1m data
        // FIX: Use IstToday instead of DateTime.UtcNow.Date
        var istToday  = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist).Date;
        var fromDate  = istToday.AddDays(-2);
        var toDate    = istToday.AddDays(1);

        var recentCandles = await GetByInstrumentIdAsync(
            instrumentId, timeframeMinutes, fromDate, toDate, cancellationToken);

        return recentCandles.OrderByDescending(c => c.Timestamp).FirstOrDefault();
    }

    /// <summary>
    /// Returns the latest candle date as an IST date.
    /// EF/Npgsql normalizes DateTimeOffset to UTC on read, so we explicitly
    /// convert back to IST before extracting .Date.
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
            .MaxAsync(c => (DateTimeOffset?)c.Timestamp, cancellationToken);

        if (latest is null)
            return null;

        // FIX: EF returns 2026-03-12 18:30:00 +0000 for a value stored as
        // 2026-03-13 00:00:00 +0530. ConvertTime to IST gives 2026-03-13 00:00:00,
        // then .Date = 2026-03-13 — correct IST date.
        return TimeZoneInfo.ConvertTime(latest.Value, Ist).Date;
    }

    public async Task<bool> HasAnyDataAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .AnyAsync(
                c => c.InstrumentId    == instrumentId
                  && c.TimeframeMinutes == timeframeMinutes,
                cancellationToken);
    }

    // -------------------------------------------------------------------------
    // WRITE
    // -------------------------------------------------------------------------

    public async Task<int> BulkUpsertAsync(
        IEnumerable<MarketCandle> candles,
        CancellationToken cancellationToken = default)
    {
        var candleList = candles.ToList();
        if (candleList.Count == 0)
            return 0;

        // Only base timeframes are physically stored
        var invalid = candleList.Where(c => !BaseTimeframes.Contains(c.TimeframeMinutes)).ToList();
        if (invalid.Count > 0)
            throw new InvalidOperationException(
                $"Only base timeframes ({string.Join(", ", BaseTimeframes.Order())}m) can be stored. " +
                $"Derived timeframes are calculated on-the-fly. " +
                $"Found {invalid.Count} candle(s) with invalid timeframe.");

        var instrumentIds = candleList.Select(c => c.InstrumentId).Distinct().ToList();
        var timestamps    = candleList.Select(c => c.Timestamp).Distinct().ToList();
        var timeframes    = candleList.Select(c => c.TimeframeMinutes).Distinct().ToList();

        var existingCandles = await _dbSet
            .AsNoTracking()
            .Where(c => instrumentIds.Contains(c.InstrumentId)
                     && timeframes.Contains(c.TimeframeMinutes)
                     && timestamps.Contains(c.Timestamp))
            .ToDictionaryAsync(
                c => $"{c.InstrumentId}_{c.TimeframeMinutes}_{c.Timestamp:yyyyMMddHHmmss}",
                cancellationToken);

        var toAdd    = new List<MarketCandle>();
        var toUpdate = new List<MarketCandle>();
        var now      = DateTimeOffset.UtcNow;

        foreach (var candle in candleList)
        {
            var key = $"{candle.InstrumentId}_{candle.TimeframeMinutes}_{candle.Timestamp:yyyyMMddHHmmss}";

            if (existingCandles.TryGetValue(key, out var existing))
            {
                var tracked = _dbSet.Local.FirstOrDefault(e =>
                    e.InstrumentId     == existing.InstrumentId  &&
                    e.TimeframeMinutes == existing.TimeframeMinutes &&
                    e.Timestamp        == existing.Timestamp);

                if (tracked is null)
                {
                    _context.Attach(existing);
                    tracked = existing;
                }

                tracked.Open   = candle.Open;
                tracked.High   = candle.High;
                tracked.Low    = candle.Low;
                tracked.Close  = candle.Close;
                tracked.Volume = candle.Volume;
                toUpdate.Add(tracked);
            }
            else
            {
                candle.CreatedAt = now;
                toAdd.Add(candle);
            }
        }

        if (toAdd.Count > 0)
            await _dbSet.AddRangeAsync(toAdd, cancellationToken);

        if (toUpdate.Count > 0)
            _dbSet.UpdateRange(toUpdate);

        return await _context.SaveChangesAsync(cancellationToken);
    }
    
}