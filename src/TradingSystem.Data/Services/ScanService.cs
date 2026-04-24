using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Data.Services;

public class ScanService : IScanService
{
    private readonly IDbContextFactory<TradingDbContext> _contextFactory;

    public ScanService(IDbContextFactory<TradingDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task SaveAsync(ScanSnapshot snapshot)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        await context.ScanSnapshots.AddAsync(snapshot);
        await context.SaveChangesAsync();
    }

    public async Task<List<ScanSnapshot>> GetTopAsync(int minScore, int limit)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ScanSnapshots
            .Include(s => s.Instrument)
            .Where(s => s.SetupScore >= minScore)
            .GroupBy(s => s.InstrumentId)
            .Select(g => g.OrderByDescending(s => s.Timestamp).First())
            .OrderByDescending(s => s.SetupScore)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<ScanSnapshot?> GetLatestSnapshotAsync(int instrumentId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ScanSnapshots
            .Include(s => s.Instrument)
            .Where(s => s.InstrumentId == instrumentId)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync();
    }

    public async Task<List<ScanSnapshot>> GetLatestSnapshotsAsync(IEnumerable<int> instrumentIds)
    {
        var ids = instrumentIds.Distinct().ToList();
        if (ids.Count == 0)
            return new List<ScanSnapshot>();

        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ScanSnapshots
            .Include(s => s.Instrument)
            .Where(s => ids.Contains(s.InstrumentId))
            .GroupBy(s => s.InstrumentId)
            .Select(g => g.OrderByDescending(s => s.Timestamp).First())
            .ToListAsync();
    }
}
