using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class MarketSentimentRepository : CommonRepository<MarketSentiment>, IMarketSentimentRepository
{
    public MarketSentimentRepository(TradingDbContext context) : base(context)
    {
    }

    public async Task<MarketSentiment?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .OrderByDescending(s => s.Timestamp)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<MarketSentiment>> GetHistoryAsync(
        DateTime fromDate, 
        DateTime toDate, 
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.Timestamp >= fromDate && s.Timestamp <= toDate)
            .OrderByDescending(s => s.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}