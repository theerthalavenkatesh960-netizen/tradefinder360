using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
 
namespace TradingSystem.Data.Services;
 
public class IndicatorService : IIndicatorService
{
    private const int MinCandlesForWarmup = 100;

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private readonly TradingDbContext _db;
    private readonly ICandleService _candleService;
    private readonly ILogger<IndicatorService> _logger;
 
    public IndicatorService(
        TradingDbContext db,
        ICandleService candleService,
        ILogger<IndicatorService> logger)
    {
        _db = db;
        _candleService = candleService;
        _logger = logger;
    }
 
    /// <summary>
    /// Bulk insert all snapshots in a single round trip.
    /// AddRangeAsync stages all entities, SaveChangesAsync sends one INSERT batch.
    /// </summary>
    public async Task BulkSaveAsync(
        List<IndicatorSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        if (snapshots.Count == 0)
            return;

        // Fetch existing snapshots that match any of the incoming instrument+timeframe+timestamp combos
        var instrumentIds = snapshots.Select(s => s.InstrumentId).Distinct().ToList();
        var timeframes    = snapshots.Select(s => s.TimeframeMinutes).Distinct().ToList();
        var timestamps    = snapshots.Select(s => s.Timestamp).Distinct().ToList();

        var existing = await _db.IndicatorSnapshots
            .Where(s => instrumentIds.Contains(s.InstrumentId)
                    && timeframes.Contains(s.TimeframeMinutes)
                    && timestamps.Contains(s.Timestamp))
            .ToDictionaryAsync(
                s => (s.InstrumentId, s.TimeframeMinutes, s.Timestamp.ToUniversalTime()),
                cancellationToken);

        var toAdd    = new List<IndicatorSnapshot>();
        var now      = DateTimeOffset.UtcNow;

        foreach (var snapshot in snapshots)
        {
            // FIX: normalize to UTC for dictionary lookup — DB returns UTC,
            // incoming snapshot.Timestamp may be +05:30 from Upstox parsing
            var key = (snapshot.InstrumentId, snapshot.TimeframeMinutes,
                    snapshot.Timestamp.ToUniversalTime());

            if (existing.TryGetValue(key, out var found))
            {
                // Update in-place — EF is already tracking 'found' from the query above
                found.EMAFast         = snapshot.EMAFast;
                found.EMASlow         = snapshot.EMASlow;
                found.RSI             = snapshot.RSI;
                found.MacdLine        = snapshot.MacdLine;
                found.MacdSignal      = snapshot.MacdSignal;
                found.MacdHistogram   = snapshot.MacdHistogram;
                found.ADX             = snapshot.ADX;
                found.PlusDI          = snapshot.PlusDI;
                found.MinusDI         = snapshot.MinusDI;
                found.ATR             = snapshot.ATR;
                found.BollingerUpper  = snapshot.BollingerUpper;
                found.BollingerMiddle = snapshot.BollingerMiddle;
                found.BollingerLower  = snapshot.BollingerLower;
                found.VWAP            = snapshot.VWAP;
                // CreatedAt intentionally not updated — preserve original insert time
            }
            else
            {
                snapshot.CreatedAt = now;
                toAdd.Add(snapshot);
            }
        }

        if (toAdd.Count > 0)
            await _db.IndicatorSnapshots.AddRangeAsync(toAdd, cancellationToken);

        // SaveChangesAsync handles both inserts (toAdd) and updates (tracked entities)
        // in a single round trip
        await _db.SaveChangesAsync(cancellationToken);
    }
 
    /// <summary>
    /// Single save — kept for backward compat and one-off use.
    /// Do NOT call this inside a loop.
    /// </summary>
    public async Task SaveAsync(int instrumentId, int timeframeMinutes, IndicatorValues indicators)
    {
        await _db.IndicatorSnapshots.AddAsync(new IndicatorSnapshot
        {
            InstrumentId     = instrumentId,
            TimeframeMinutes = timeframeMinutes,
            Timestamp        = indicators.Timestamp,
            EMAFast          = indicators.EMAFast,
            EMASlow          = indicators.EMASlow,
            RSI              = indicators.RSI,
            MacdLine         = indicators.MacdLine,
            MacdSignal       = indicators.MacdSignal,
            MacdHistogram    = indicators.MacdHistogram,
            ADX              = indicators.ADX,
            PlusDI           = indicators.PlusDI,
            MinusDI          = indicators.MinusDI,
            ATR              = indicators.ATR,
            BollingerUpper   = indicators.BollingerUpper,
            BollingerMiddle  = indicators.BollingerMiddle,
            BollingerLower   = indicators.BollingerLower,
            VWAP             = indicators.VWAP,
            // FIX: was DateTime.UtcNow — should be DateTimeOffset.UtcNow
            // DateTime.UtcNow has no offset info; DateTimeOffset.UtcNow = +00:00
            CreatedAt        = DateTimeOffset.UtcNow
        });
 
        await _db.SaveChangesAsync();
    }
 
    public async Task<IndicatorSnapshot?> GetLatestAsync(int instrumentId, int timeframeMinutes)
        => await _db.IndicatorSnapshots
            .Where(s => s.InstrumentId    == instrumentId
                     && s.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(s => s.Timestamp)
            .AsNoTracking()
            .FirstOrDefaultAsync();
 
    public async Task<List<IndicatorSnapshot>> GetRecentAsync(
        int instrumentId, int timeframeMinutes, int count)
        => await _db.IndicatorSnapshots
            .Where(s => s.InstrumentId    == instrumentId
                     && s.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(s => s.Timestamp)
            .Take(count)
            .OrderBy(s => s.Timestamp)
            .AsNoTracking()
            .ToListAsync();

    public async Task<List<IndicatorSnapshot>> GetByDateRangeAsync(
        int instrumentId, int timeframeMinutes,
        DateTimeOffset fromDate, DateTimeOffset toDate,
        CancellationToken cancellationToken = default)
        => await _db.IndicatorSnapshots
            .Where(s => s.InstrumentId == instrumentId
                     && s.TimeframeMinutes == timeframeMinutes
                     && s.Timestamp >= fromDate
                     && s.Timestamp <= toDate)
            .OrderBy(s => s.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<List<IndicatorSnapshot>> EnsureIndicatorsCalculatedAsync(
        int instrumentId, int timeframeMinutes,
        DateTime fromDate, DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        // Convert IST boundaries → UTC for DB comparison
        var fromUtc = new DateTimeOffset(fromDate, TimeSpan.FromHours(5.5)).ToUniversalTime();
        var toUtc = new DateTimeOffset(toDate.AddDays(1).AddTicks(-1), TimeSpan.FromHours(5.5)).ToUniversalTime();

        // 1. Get existing snapshots in the requested range
        var existingSnapshots = await GetByDateRangeAsync(
            instrumentId, timeframeMinutes, fromUtc, toUtc, cancellationToken);

        // 2. Get candles already in the DB — NO Upstox API calls
        var candles = await _candleService.GetCandlesFromDbAsync(
            instrumentId, timeframeMinutes, fromDate, toDate);

        if (candles.Count == 0)
            return existingSnapshots;

        // 3. Build a HashSet of existing indicator timestamps (UTC) for O(1) lookup
        var existingTimestamps = existingSnapshots
            .Select(s => s.Timestamp.ToUniversalTime())
            .ToHashSet();

        // 4. Find candle timestamps that have no corresponding indicator snapshot
        var candlesWithoutIndicators = candles
            .Where(c => !existingTimestamps.Contains(c.Timestamp.ToUniversalTime()))
            .ToList();

        if (candlesWithoutIndicators.Count == 0)
            return existingSnapshots; // Everything is already calculated

        _logger.LogInformation(
            "Instrument {Id} {TF}m — {Missing} candles missing indicators out of {Total}. Backfilling...",
            instrumentId, timeframeMinutes,
            candlesWithoutIndicators.Count, candles.Count);

        // 5. Fetch full candle history including warmup buffer — DB only, no API calls
        var warmupFrom = fromDate.AddMinutes(-(MinCandlesForWarmup * timeframeMinutes));
        var allCandles = await _candleService.GetCandlesFromDbAsync(
            instrumentId, timeframeMinutes, warmupFrom, toDate);

        if (allCandles.Count < MinCandlesForWarmup)
        {
            _logger.LogDebug(
                "Instrument {Id} {TF}m — insufficient candles for warmup ({Count}/{Min})",
                instrumentId, timeframeMinutes, allCandles.Count, MinCandlesForWarmup);
            return existingSnapshots;
        }

        var orderedCandles = allCandles.OrderBy(c => c.Timestamp).ToList();

        // 6. Build a set of timestamps that need calculation
        var missingTimestamps = candlesWithoutIndicators
            .Select(c => c.Timestamp.ToUniversalTime())
            .ToHashSet();

        // 7. Run indicator engine over ALL candles (warmup + data range)
        //    but only save snapshots for the missing ones
        var engine = new IndicatorEngine(
            emaFastPeriod: 20, emaSlowPeriod: 50,
            rsiPeriod: 14,
            macdFast: 12, macdSlow: 26, macdSignal: 9,
            adxPeriod: 14, atrPeriod: 14,
            bollingerPeriod: 20, bollingerStdDev: 2.0m);

        var newSnapshots = new List<IndicatorSnapshot>();

        foreach (var candle in orderedCandles)
        {
            var indicators = engine.Calculate(candle);

            // Only save if this timestamp is one of the missing ones
            if (!missingTimestamps.Contains(candle.Timestamp.ToUniversalTime()))
                continue;

            // Skip warmup-phase values (indicators not yet stable)
            if (indicators.EMASlow == 0 || indicators.ADX == 0 ||
                indicators.ATR == 0 || indicators.BollingerMiddle == 0)
                continue;

            newSnapshots.Add(new IndicatorSnapshot
            {
                InstrumentId     = instrumentId,
                TimeframeMinutes = timeframeMinutes,
                Timestamp        = candle.Timestamp.ToUniversalTime(),
                EMAFast          = Math.Round(indicators.EMAFast,         4, MidpointRounding.AwayFromZero),
                EMASlow          = Math.Round(indicators.EMASlow,         4, MidpointRounding.AwayFromZero),
                RSI              = Math.Round(indicators.RSI,             4, MidpointRounding.AwayFromZero),
                MacdLine         = Math.Round(indicators.MacdLine,        4, MidpointRounding.AwayFromZero),
                MacdSignal       = Math.Round(indicators.MacdSignal,      4, MidpointRounding.AwayFromZero),
                MacdHistogram    = Math.Round(indicators.MacdHistogram,   4, MidpointRounding.AwayFromZero),
                ADX              = Math.Round(indicators.ADX,             4, MidpointRounding.AwayFromZero),
                PlusDI           = Math.Round(indicators.PlusDI,          4, MidpointRounding.AwayFromZero),
                MinusDI          = Math.Round(indicators.MinusDI,         4, MidpointRounding.AwayFromZero),
                ATR              = Math.Round(indicators.ATR,             4, MidpointRounding.AwayFromZero),
                BollingerUpper   = Math.Round(indicators.BollingerUpper,  4, MidpointRounding.AwayFromZero),
                BollingerMiddle  = Math.Round(indicators.BollingerMiddle, 4, MidpointRounding.AwayFromZero),
                BollingerLower   = Math.Round(indicators.BollingerLower,  4, MidpointRounding.AwayFromZero),
                VWAP             = Math.Round(indicators.VWAP,            4, MidpointRounding.AwayFromZero),
            });
        }

        // 8. Persist new snapshots
        if (newSnapshots.Count > 0)
        {
            await BulkSaveAsync(newSnapshots, cancellationToken);

            _logger.LogInformation(
                "Instrument {Id} {TF}m — backfilled {Count} indicator snapshots",
                instrumentId, timeframeMinutes, newSnapshots.Count);
        }

        // 9. Return the complete set (existing + newly calculated)
        var allSnapshots = existingSnapshots
            .Concat(newSnapshots)
            .OrderBy(s => s.Timestamp)
            .ToList();

        return allSnapshots;
    }
}