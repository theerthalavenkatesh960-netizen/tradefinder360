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

    public async Task SaveAsync(int instrumentId, int timeframeMinutes, IndicatorValues indicators)
    {
        await _db.IndicatorSnapshots.AddAsync(new IndicatorSnapshot
        {
            InstrumentId = instrumentId,
            TimeframeMinutes = timeframeMinutes,
            Timestamp = indicators.Timestamp,
            EMAFast = indicators.EMAFast,
            EMASlow = indicators.EMASlow,
            RSI = indicators.RSI,
            MacdLine = indicators.MacdLine,
            MacdSignal = indicators.MacdSignal,
            MacdHistogram = indicators.MacdHistogram,
            ADX = indicators.ADX,
            PlusDI = indicators.PlusDI,
            MinusDI = indicators.MinusDI,
            ATR = indicators.ATR,
            BollingerUpper = indicators.BollingerUpper,
            BollingerMiddle = indicators.BollingerMiddle,
            BollingerLower = indicators.BollingerLower,
            VWAP = indicators.VWAP,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<IndicatorSnapshot?> GetLatestAsync(int instrumentId, int timeframeMinutes)
        => await _db.IndicatorSnapshots
            .Where(s => s.InstrumentId == instrumentId && s.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync();

    public async Task<List<IndicatorSnapshot>> GetRecentAsync(int instrumentId, int timeframeMinutes, int count)
        => await _db.IndicatorSnapshots
            .Where(s => s.InstrumentId == instrumentId && s.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(s => s.Timestamp)
            .Take(count)
            .OrderBy(s => s.Timestamp)
            .ToListAsync();
}
