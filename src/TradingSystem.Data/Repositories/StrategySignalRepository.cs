using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class StrategySignalRepository : CommonRepository<StrategySignalRecord>, IStrategySignalRepository
{
    public StrategySignalRepository(TradingDbContext context) : base(context)
    {
    }

    public async Task<List<StrategySignalRecord>> GetByStrategyTypeAsync(
        StrategyType strategyType,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.StrategyType == strategyType
                     && s.Timestamp >= fromDate
                     && s.Timestamp <= toDate)
            .Include(s => s.Instrument)
            .OrderByDescending(s => s.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<StrategySignalRecord>> GetByInstrumentAsync(
        int instrumentId,
        StrategyType? strategyType = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Where(s => s.InstrumentId == instrumentId);

        if (strategyType.HasValue)
        {
            query = query.Where(s => s.StrategyType == strategyType.Value);
        }

        return await query
            .OrderByDescending(s => s.Timestamp)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<StrategySignalRecord>> GetValidSignalsAsync(
        StrategyType? strategyType = null,
        int minConfidence = 60,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Where(s => s.IsValid && s.Confidence >= minConfidence);

        if (strategyType.HasValue)
        {
            query = query.Where(s => s.StrategyType == strategyType.Value);
        }

        return await query
            .Include(s => s.Instrument)
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Confidence)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<StrategySignalRecord?> GetLatestSignalAsync(
        int instrumentId,
        StrategyType strategyType,
        CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.InstrumentId == instrumentId && s.StrategyType == strategyType)
            .OrderByDescending(s => s.Timestamp)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<StrategySignalRecord>> GetUnactedSignalsAsync(
        DateTimeOffset? expiresAfter = null,
        CancellationToken cancellationToken = default)
    {
        var cutoffTime = expiresAfter ?? DateTimeOffset.UtcNow;

        return await _dbSet
            .Where(s => !s.WasActedUpon 
                     && s.IsValid 
                     && (!s.ExpiresAt.HasValue || s.ExpiresAt > cutoffTime))
            .Include(s => s.Instrument)
            .OrderByDescending(s => s.Score)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsActedUponAsync(
        int signalId,
        int tradeId,
        CancellationToken cancellationToken = default)
    {
        var signal = await _dbSet.FindAsync(new object[] { signalId }, cancellationToken);
        if (signal != null)
        {
            signal.WasActedUpon = true;
            signal.RelatedTradeId = tradeId;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}