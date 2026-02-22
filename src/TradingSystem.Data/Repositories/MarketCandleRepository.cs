using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class MarketCandleRepository : CommonRepository<MarketCandle>, IMarketCandleRepository
{
    public MarketCandleRepository(TradingDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<MarketCandle>> GetByInstrumentIdAsync(
        int instrumentId,
        int timeframeMinutes,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        // If requesting 1-minute candles, return directly from database
        if (timeframeMinutes == 1)
        {
            var query = _dbSet
                .Where(c => c.InstrumentId == instrumentId && c.TimeframeMinutes == 1);

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

        // For other timeframes, aggregate 1-minute candles
        return await AggregateToTimeframeAsync(instrumentId, timeframeMinutes, from, to, cancellationToken);
    }

    private async Task<IReadOnlyList<MarketCandle>> AggregateToTimeframeAsync(
        int instrumentId,
        int timeframeMinutes,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        // Fetch 1-minute candles
        var query = _dbSet
            .Where(c => c.InstrumentId == instrumentId && c.TimeframeMinutes == 1);

        if (from.HasValue)
        {
            query = query.Where(c => c.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(c => c.Timestamp <= to.Value);
        }

        var oneMinuteCandles = await query
            .OrderBy(c => c.Timestamp)
            .Select(c => new
            {
                c.InstrumentId,
                c.Timestamp,
                c.Open,
                c.High,
                c.Low,
                c.Close,
                c.Volume
            })
            .ToListAsync(cancellationToken);

        if (!oneMinuteCandles.Any())
        {
            return Array.Empty<MarketCandle>();
        }

        // Group and aggregate in-memory (most efficient for typical datasets)
        var aggregated = oneMinuteCandles
            .GroupBy(c => new
            {
                c.InstrumentId,
                // Round down to nearest timeframe interval
                TimeframeStart = new DateTime(
                    c.Timestamp.Year,
                    c.Timestamp.Month,
                    c.Timestamp.Day,
                    c.Timestamp.Hour,
                    (c.Timestamp.Minute / timeframeMinutes) * timeframeMinutes,
                    0
                )
            })
            .Select(g => new MarketCandle
            {
                InstrumentId = g.Key.InstrumentId,
                TimeframeMinutes = timeframeMinutes,
                Timestamp = g.Key.TimeframeStart,
                Open = g.OrderBy(x => x.Timestamp).First().Open,
                High = g.Max(x => x.High),
                Low = g.Min(x => x.Low),
                Close = g.OrderByDescending(x => x.Timestamp).First().Close,
                Volume = g.Sum(x => x.Volume),
                CreatedAt = DateTime.UtcNow
            })
            .OrderBy(c => c.Timestamp)
            .ToList();

        return aggregated;
    }

    public async Task<MarketCandle?> GetLatestCandleAsync(
        int instrumentId,
        int timeframeMinutes,
        CancellationToken cancellationToken = default)
    {
        // If requesting 1-minute candle, return directly
        if (timeframeMinutes == 1)
        {
            return await _dbSet
                .Where(c => c.InstrumentId == instrumentId && c.TimeframeMinutes == 1)
                .OrderByDescending(c => c.Timestamp)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // For other timeframes, get recent 1-min candles and aggregate
        var lookbackMinutes = timeframeMinutes * 2; // Get enough data for last complete candle
        var fromTime = DateTime.UtcNow.AddMinutes(-lookbackMinutes);

        var recentCandles = await GetByInstrumentIdAsync(
            instrumentId, 
            timeframeMinutes, 
            fromTime, 
            null, 
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

        // Ensure all candles are 1-minute timeframe
        if (candleList.Any(c => c.TimeframeMinutes != 1))
        {
            throw new InvalidOperationException("Only 1-minute candles can be stored. Other timeframes are calculated on-the-fly.");
        }

        var instrumentIds = candleList.Select(c => c.InstrumentId).Distinct().ToList();
        var timestamps = candleList.Select(c => c.Timestamp).Distinct().ToList();

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
                candle.TimeframeMinutes = 1; // Ensure it's stored as 1-minute
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