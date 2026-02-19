using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public class RecommendationService : IRecommendationService
{
    private readonly TradingDbContext _db;

    public RecommendationService(TradingDbContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(Recommendation recommendation)
    {
        await _db.Recommendations.AddAsync(recommendation);
        await _db.SaveChangesAsync();
    }

    public async Task<List<Recommendation>> GetActiveAsync()
        => await _db.Recommendations
            .Where(r => r.IsActive && (r.ExpiresAt == null || r.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(r => r.Confidence)
            .ToListAsync();

    public async Task<Recommendation?> GetLatestForInstrumentAsync(string instrumentKey)
        => await _db.Recommendations
            .Where(r => r.InstrumentKey == instrumentKey && r.IsActive)
            .OrderByDescending(r => r.Timestamp)
            .FirstOrDefaultAsync();

    public async Task ExpireOldAsync(int olderThanMinutes = 60)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-olderThanMinutes);
        var old = await _db.Recommendations
            .Where(r => r.IsActive && r.CreatedAt < cutoff)
            .ToListAsync();
        foreach (var rec in old)
            rec.IsActive = false;
        await _db.SaveChangesAsync();
    }
}
