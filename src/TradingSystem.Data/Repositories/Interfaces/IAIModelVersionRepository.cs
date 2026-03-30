using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface IAIModelVersionRepository
{
    Task<AIModelVersion> CreateAsync(AIModelVersion modelVersion, CancellationToken cancellationToken = default);
    Task<AIModelVersion?> GetByVersionAsync(string version, string modelType, CancellationToken cancellationToken = default);
    Task<AIModelVersion?> GetActiveModelAsync(string modelType, CancellationToken cancellationToken = default);
    Task<List<AIModelVersion>> GetAllVersionsAsync(string modelType, CancellationToken cancellationToken = default);
    Task UpdateAsync(AIModelVersion modelVersion, CancellationToken cancellationToken = default);
    Task ActivateModelAsync(int id, CancellationToken cancellationToken = default);
    Task DeactivateAllAsync(string modelType, CancellationToken cancellationToken = default);
}