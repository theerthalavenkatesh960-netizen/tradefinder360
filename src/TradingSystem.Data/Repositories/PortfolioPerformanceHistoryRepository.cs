using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

/// <summary>
/// Repository implementation for portfolio performance history persistence
/// Follows existing repository patterns in the codebase
/// </summary>
public class PortfolioPerformanceHistoryRepository : IPortfolioPerformanceHistoryRepository
{
    private readonly TradingDbContext _dbContext;

    public PortfolioPerformanceHistoryRepository(TradingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PortfolioPerformanceHistory> AddAsync(
        PortfolioPerformanceHistory history,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.PortfolioPerformanceHistories.AddAsync(history, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return history;
    }

    public async Task<PortfolioPerformanceHistory?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PortfolioPerformanceHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<List<PortfolioPerformanceHistory>> GetBySessionIdAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PortfolioPerformanceHistories
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.RecordedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PortfolioPerformanceHistory>> GetLastNForSessionAsync(
        long sessionId,
        int count = 5,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PortfolioPerformanceHistories
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.RecordedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PortfolioPerformanceHistory>> GetByUserIdAndDateRangeAsync(
        string userId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PortfolioPerformanceHistories
            .AsNoTracking()
            .Include(x => x.Session)
            .Where(x => x.Session!.UserId == userId
                && x.RecordedAt >= fromDate
                && x.RecordedAt <= toDate)
            .OrderByDescending(x => x.RecordedAt)
            .ToListAsync(cancellationToken);
    }
}
