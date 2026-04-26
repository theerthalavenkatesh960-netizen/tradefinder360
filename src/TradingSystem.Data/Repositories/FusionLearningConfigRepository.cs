using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

/// <summary>
/// Repository implementation for fusion learning config persistence
/// Maintains audit trail of all algorithm tuning iterations
/// </summary>
public class FusionLearningConfigRepository : IFusionLearningConfigRepository
{
    private readonly TradingDbContext _dbContext;

    public FusionLearningConfigRepository(TradingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FusionLearningConfig> AddAsync(
        FusionLearningConfig config,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.FusionLearningConfigs.AddAsync(config, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return config;
    }

    public async Task<FusionLearningConfig> UpdateAsync(
        FusionLearningConfig config,
        CancellationToken cancellationToken = default)
    {
        _dbContext.FusionLearningConfigs.Update(config);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return config;
    }

    public async Task<FusionLearningConfig?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.FusionLearningConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<FusionLearningConfig?> GetActiveConfigAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.FusionLearningConfigs
            .AsNoTracking()
            .Where(x => x.Status == "ACTIVE")
            .OrderByDescending(x => x.AppliedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FusionLearningConfig?> GetLatestConfigAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.FusionLearningConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<FusionLearningConfig>> GetAllConfigsOrderedAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.FusionLearningConfigs
            .AsNoTracking()
            .OrderBy(x => x.Iteration)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<FusionLearningConfig>> GetLastNConfigsAsync(
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.FusionLearningConfigs
            .AsNoTracking()
            .OrderByDescending(x => x.Iteration)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<FusionLearningConfig?> GetByIterationAsync(
        int iteration,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.FusionLearningConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Iteration == iteration, cancellationToken);
    }
}
