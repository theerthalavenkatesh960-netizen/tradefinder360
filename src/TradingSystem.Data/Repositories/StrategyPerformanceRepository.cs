using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class StrategyPerformanceRepository : CommonRepository<StrategyPerformance>, IStrategyPerformanceRepository
{
    public StrategyPerformanceRepository(TradingDbContext context) : base(context)
    {
    }

    public async Task<StrategyPerformance?> GetLatestPerformanceAsync(
        StrategyType strategyType,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p => p.StrategyType == strategyType)
            .OrderByDescending(p => p.PeriodEnd)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<StrategyPerformance>> GetPerformanceHistoryAsync(
        StrategyType strategyType,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p => p.StrategyType == strategyType
                     && p.PeriodStart >= fromDate
                     && p.PeriodEnd <= toDate)
            .OrderByDescending(p => p.PeriodEnd)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<StrategyPerformance>> GetAllStrategiesPerformanceAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(p => p.PeriodStart >= fromDate && p.PeriodEnd <= toDate)
            .OrderByDescending(p => p.WinRate)
            .ThenByDescending(p => p.TotalPnL)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}