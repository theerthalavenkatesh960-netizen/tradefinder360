using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class MarketSentimentRepository : CommonRepository<MarketSentiment>, IMarketSentimentRepository
{
    public MarketSentimentRepository(IDbContextFactory<TradingDbContext> contextFactory) : base(contextFactory)
    {
    }

    public async Task<MarketSentiment?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<MarketSentiment>()
            .OrderByDescending(s => s.Timestamp)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<MarketSentiment>> GetHistoryAsync(
        DateTime fromDate, 
        DateTime toDate, 
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<MarketSentiment>()
            .Where(s => s.Timestamp >= fromDate && s.Timestamp <= toDate)
            .OrderByDescending(s => s.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}