using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Data.Services;

public class CandleService : ICandleService
{
    private readonly TradingDbContext _db;

    public CandleService(TradingDbContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(int instrumentId, Candle candle)
    {
        await _db.MarketCandles.AddAsync(ToMarketCandle(instrumentId, candle));
        await _db.SaveChangesAsync();
    }

    public async Task SaveBatchAsync(int instrumentId, List<Candle> candles)
    {
        await _db.MarketCandles.AddRangeAsync(candles.Select(c => ToMarketCandle(instrumentId, c)));
        await _db.SaveChangesAsync();
    }

    public async Task<List<Candle>> GetRecentAsync(int instrumentId, int timeframeMinutes, int count)
    {
        var rows = await _db.MarketCandles
            .Where(c => c.InstrumentId == instrumentId && c.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(c => c.Timestamp)
            .Take(count)
            .OrderBy(c => c.Timestamp)
            .ToListAsync();
        return rows.Select(c => c.ToCandle()).ToList();
    }

    public async Task<List<Candle>> GetRangeAsync(int instrumentId, int timeframeMinutes, DateTime startTime, DateTime endTime)
    {
        var rows = await _db.MarketCandles
            .Where(c => c.InstrumentId == instrumentId
                     && c.TimeframeMinutes == timeframeMinutes
                     && c.Timestamp >= startTime
                     && c.Timestamp <= endTime)
            .OrderBy(c => c.Timestamp)
            .ToListAsync();
        return rows.Select(c => c.ToCandle()).ToList();
    }

    private static MarketCandle ToMarketCandle(int instrumentId, Candle candle) => new()
    {
        InstrumentId = instrumentId,
        TimeframeMinutes = candle.TimeframeMinutes,
        Timestamp = candle.Timestamp,
        Open = candle.Open,
        High = candle.High,
        Low = candle.Low,
        Close = candle.Close,
        Volume = candle.Volume,
        CreatedAt = DateTime.UtcNow
    };
}
