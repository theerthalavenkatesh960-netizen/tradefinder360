 
using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
 
namespace TradingSystem.Data.Services;
 
public class IndicatorService : IIndicatorService
{
    private readonly TradingDbContext _db;
 
    public IndicatorService(TradingDbContext db)
    {
        _db = db;
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
}
 