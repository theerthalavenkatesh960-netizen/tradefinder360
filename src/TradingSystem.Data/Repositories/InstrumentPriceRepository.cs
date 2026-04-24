using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class InstrumentPriceRepository : CommonRepository<InstrumentPrice>, IInstrumentPriceRepository
{
    public InstrumentPriceRepository(IDbContextFactory<TradingDbContext> contextFactory) : base(contextFactory)
    {
    }

    public async Task<IReadOnlyList<InstrumentPrice>> GetByInstrumentIdAsync(
        int instrumentId,
        string timeframe,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.Set<InstrumentPrice>()
            .Where(p => p.InstrumentId == instrumentId && p.Timeframe == timeframe);

        if (from.HasValue)
        {
            query = query.Where(p => p.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(p => p.Timestamp <= to.Value);
        }

        return await query
            .OrderBy(p => p.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<InstrumentPrice?> GetLatestPriceAsync(int instrumentId, string timeframe, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<InstrumentPrice>()
            .Where(p => p.InstrumentId == instrumentId && p.Timeframe == timeframe)
            .OrderByDescending(p => p.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> BulkUpsertAsync(IEnumerable<InstrumentPrice> prices, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var priceList = prices.ToList();
        if (!priceList.Any())
        {
            return 0;
        }

        var instrumentIds = priceList.Select(p => p.InstrumentId).Distinct().ToList();
        var timeframes = priceList.Select(p => p.Timeframe).Distinct().ToList();
        // var timestamps = priceList.Select(p => p.Timestamp).Distinct().ToList();

        var existingPrices = await context.Set<InstrumentPrice>()
            .Where(p => instrumentIds.Contains(p.InstrumentId)
                     && timeframes.Contains(p.Timeframe))
                     //&& timestamps.Contains(p.Timestamp))
            .ToDictionaryAsync(
                p => $"{p.InstrumentId}_{p.Timeframe}",
                cancellationToken);

        var toAdd = new List<InstrumentPrice>();
        var toUpdate = new List<InstrumentPrice>();
        var now = DateTime.UtcNow;

        foreach (var price in priceList)
        {
            var key = $"{price.InstrumentId}_{price.Timeframe}";

            if (existingPrices.TryGetValue(key, out var existing))
            {
                existing.Open = price.Open;
                existing.High = price.High;
                existing.Low = price.Low;
                existing.Close = price.Close;
                existing.Volume = price.Volume;
                existing.UpdatedAt = now;
                toUpdate.Add(existing);
            }
            else
            {
                price.CreatedAt = now;
                price.UpdatedAt = now;
                toAdd.Add(price);
            }
        }

        if (toAdd.Any())
        {
            await context.Set<InstrumentPrice>().AddRangeAsync(toAdd, cancellationToken);
        }

        if (toUpdate.Any())
        {
            context.Set<InstrumentPrice>().UpdateRange(toUpdate);
        }

        return await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Dictionary<int, InstrumentPrice>> GetLatestPricesForInstrumentsAsync(IEnumerable<int> instrumentIds, string timeframe,
    CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var ids = instrumentIds.ToList();

        try
        {
            var latestPrices = await context.Set<InstrumentPrice>()
                .Where(p => ids.Contains(p.InstrumentId) && p.Timeframe == timeframe)
                .OrderByDescending(p => p.Timestamp)
                .GroupBy(p => p.InstrumentId)
                .Select(g => g.First())
                .ToListAsync(cancellationToken);

            return latestPrices.ToDictionary(p => p.InstrumentId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching latest prices: {ex.Message}");
            return new Dictionary<int, InstrumentPrice>();
        }
    }
}
