using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class SectorRepository : CommonRepository<Sector>, ISectorRepository
{
    public SectorRepository(TradingDbContext context) : base(context)
    {
    }

    public async Task<Sector?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);
    }

    public async Task<Sector?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<Sector>> GetActiveSectorsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> BulkUpsertAsync(IEnumerable<Sector> sectors, CancellationToken cancellationToken = default)
    {
        var sectorList = sectors.ToList();
        var codes = sectorList.Select(s => s.Code).ToList();

        var existingSectors = await _dbSet
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
            await _dbSet.AddRangeAsync(toAdd, cancellationToken);
        }

        if (toUpdate.Any())
        {
            _dbSet.UpdateRange(toUpdate);
        }

        return await _context.SaveChangesAsync(cancellationToken);
    }
}
