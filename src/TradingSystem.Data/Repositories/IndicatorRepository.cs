using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Data.Repositories;

public class IndicatorRepository : IIndicatorRepository
{
    private readonly TradingDbContext _context;

    public IndicatorRepository(TradingDbContext context)
    {
        _context = context;
    }

    public async Task SaveAsync(string instrumentKey, int timeframeMinutes, IndicatorValues indicators)
    {
        var snapshot = new IndicatorSnapshot
        {
            InstrumentKey = instrumentKey,
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
        };

        await _context.IndicatorSnapshots.AddAsync(snapshot);
        await _context.SaveChangesAsync();
    }

    public async Task<List<IndicatorSnapshot>> GetRecentAsync(string instrumentKey, int timeframeMinutes, int count)
    {
        return await _context.IndicatorSnapshots
            .Where(s => s.InstrumentKey == instrumentKey && s.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(s => s.Timestamp)
            .Take(count)
            .OrderBy(s => s.Timestamp)
            .ToListAsync();
    }
}
