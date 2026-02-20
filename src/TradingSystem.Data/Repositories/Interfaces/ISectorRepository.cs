using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface ISectorRepository : ICommonRepository<Sector>
{
    Task<Sector?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Sector?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sector>> GetActiveSectorsAsync(CancellationToken cancellationToken = default);
    Task<int> BulkUpsertAsync(IEnumerable<Sector> sectors, CancellationToken cancellationToken = default);
}
