using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Data.Services;

public class ScanService : IScanService
{
    private readonly TradingDbContext _db;

    public ScanService(TradingDbContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(ScanSnapshot snapshot)
    {
        await _db.ScanSnapshots.AddAsync(snapshot);
        await _db.SaveChangesAsync();
    }

    public async Task<List<ScanSnapshot>> GetTopAsync(int minScore, int limit)
        => await _db.ScanSnapshots
            .Where(s => s.SetupScore >= minScore)
            .GroupBy(s => s.InstrumentId)
            .Select(g => g.OrderByDescending(s => s.Timestamp).First())
            .OrderByDescending(s => s.SetupScore)
            .Take(limit)
            .ToListAsync();

    public async Task<ScanSnapshot?> GetLatestSnapshotAsync(int instrumentId)
        => await _db.ScanSnapshots
            .Where(s => s.InstrumentId == instrumentId)
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync();
}
