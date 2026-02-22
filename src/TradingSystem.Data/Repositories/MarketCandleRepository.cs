using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class MarketCandleRepository : CommonRepository<MarketCandle>, IMarketCandleRepository
{
    public MarketCandleRepository(TradingDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<MarketCandle>> GetByInstrumentKeyAsync(
        string instrumentKey,
        int timeframeMinutes,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Where(c => c.InstrumentKey == instrumentKey && c.TimeframeMinutes == timeframeMinutes);

        if (from.HasValue)
        {
            query = query.Where(c => c.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(c => c.Timestamp <= to.Value);
        }

        return await query
            .OrderBy(c => c.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<MarketCandle?> GetLatestCandleAsync(
        string instrumentKey,
        int timeframeMinutes,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(c => c.InstrumentKey == instrumentKey && c.TimeframeMinutes == timeframeMinutes)
            .OrderByDescending(c => c.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
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

        var instrumentKeys = candleList.Select(c => c.InstrumentKey).Distinct().ToList();
        var timeframes = candleList.Select(c => c.TimeframeMinutes).Distinct().ToList();
        var timestamps = candleList.Select(c => c.Timestamp).Distinct().ToList();

        var existingCandles = await _dbSet
            .Where(c => instrumentKeys.Contains(c.InstrumentKey)
                     && timeframes.Contains(c.TimeframeMinutes)
                     && timestamps.Contains(c.Timestamp))
            .ToDictionaryAsync(
                c => $"{c.InstrumentKey}_{c.TimeframeMinutes}_{c.Timestamp:yyyyMMddHHmmss}",
                cancellationToken);

        var toAdd = new List<MarketCandle>();
        var toUpdate = new List<MarketCandle>();
        var now = DateTime.UtcNow;

        foreach (var candle in candleList)
        {
            var key = $"{candle.InstrumentKey}_{candle.TimeframeMinutes}_{candle.Timestamp:yyyyMMddHHmmss}";

            if (existingCandles.TryGetValue(key, out var existing))
            {
                existing.Open = candle.Open;
                existing.High = candle.High;
                existing.Low = candle.Low;
                existing.Close = candle.Close;
                existing.Volume = candle.Volume;
                toUpdate.Add(existing);
            }
            else
            {
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
}