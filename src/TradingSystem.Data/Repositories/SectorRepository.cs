using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class SectorRepository : CommonRepository<Sector>, ISectorRepository
{
    public SectorRepository(IDbContextFactory<TradingDbContext> contextFactory) : base(contextFactory)
    {
    }

    public async Task<Sector?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<Sector>().FirstOrDefaultAsync(s => s.Code == code, cancellationToken);
    }

    public async Task<Sector?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<Sector>().FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<Sector>> GetActiveSectorsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<Sector>()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> BulkUpsertAsync(IEnumerable<Sector> sectors, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var sectorList = sectors.ToList();
        var codes = sectorList.Select(s => s.Code).ToList();

        var existingSectors = await context.Set<Sector>()
            .Where(s => codes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, cancellationToken);

        var toAdd = new List<Sector>();
        var toUpdate = new List<Sector>();

        foreach (var sector in sectorList)
        {
            if (existingSectors.TryGetValue(sector.Code, out var existing))
            {
                existing.Name = sector.Name;
                existing.Description = sector.Description;
                existing.IsActive = sector.IsActive;
                existing.UpdatedAt = DateTime.UtcNow;
                toUpdate.Add(existing);
            }
            else
            {
                sector.CreatedAt = DateTime.UtcNow;
                sector.UpdatedAt = DateTime.UtcNow;
                toAdd.Add(sector);
            }
        }

        if (toAdd.Any())
        {
            await context.Set<Sector>().AddRangeAsync(toAdd, cancellationToken);
        }

        if (toUpdate.Any())
        {
            context.Set<Sector>().UpdateRange(toUpdate);
        }

        return await context.SaveChangesAsync(cancellationToken);
    }
}
