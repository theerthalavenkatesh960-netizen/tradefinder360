using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories;

public class CandleRepository : ICandleRepository
{
    private readonly TradingDbContext _context;

    public CandleRepository(TradingDbContext context)
    {
        _context = context;
    }

    public async Task SaveAsync(string instrumentKey, Candle candle)
    {
        var marketCandle = new MarketCandle
        {
            InstrumentKey = instrumentKey,
            TimeframeMinutes = candle.TimeframeMinutes,
            Timestamp = candle.Timestamp,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume,
            CreatedAt = DateTime.UtcNow
        };

        await _context.MarketCandles.AddAsync(marketCandle);
        await _context.SaveChangesAsync();
    }

    public async Task SaveBatchAsync(string instrumentKey, List<Candle> candles)
    {
        var marketCandles = candles.Select(c => new MarketCandle
        {
            InstrumentKey = instrumentKey,
            TimeframeMinutes = c.TimeframeMinutes,
            Timestamp = c.Timestamp,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        await _context.MarketCandles.AddRangeAsync(marketCandles);
        await _context.SaveChangesAsync();
    }

    public async Task<List<Candle>> GetRecentAsync(string instrumentKey, int timeframeMinutes, int count)
    {
        var marketCandles = await _context.MarketCandles
            .Where(c => c.InstrumentKey == instrumentKey && c.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(c => c.Timestamp)
            .Take(count)
            .ToListAsync();

        return marketCandles
            .OrderBy(c => c.Timestamp)
            .Select(c => c.ToCandle())
            .ToList();
    }

    public async Task<List<Candle>> GetRangeAsync(string instrumentKey, int timeframeMinutes, DateTime startTime, DateTime endTime)
    {
        var marketCandles = await _context.MarketCandles
            .Where(c => c.InstrumentKey == instrumentKey
                     && c.TimeframeMinutes == timeframeMinutes
                     && c.Timestamp >= startTime
                     && c.Timestamp <= endTime)
            .OrderBy(c => c.Timestamp)
            .ToListAsync();

        return marketCandles.Select(c => c.ToCandle()).ToList();
    }
}
