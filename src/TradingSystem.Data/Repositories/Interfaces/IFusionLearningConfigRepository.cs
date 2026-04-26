using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

/// <summary>
/// Repository for managing fusion algorithm learning configurations
/// Provides audit trail of all algorithm tuning iterations
/// </summary>
public interface IFusionLearningConfigRepository
{
    Task<FusionLearningConfig> AddAsync(
        FusionLearningConfig config,
        CancellationToken cancellationToken = default);

    Task<FusionLearningConfig> UpdateAsync(
        FusionLearningConfig config,
        CancellationToken cancellationToken = default);

    Task<FusionLearningConfig?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the currently active configuration
    /// </summary>
    Task<FusionLearningConfig?> GetActiveConfigAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the most recent learning iteration
    /// </summary>
    Task<FusionLearningConfig?> GetLatestConfigAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all learning iterations in order, used for audit trail
    /// </summary>
    Task<List<FusionLearningConfig>> GetAllConfigsOrderedAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get last N learning iterations
    /// </summary>
    Task<List<FusionLearningConfig>> GetLastNConfigsAsync(
        int count = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get by iteration number (unique identifier)
    /// </summary>
    Task<FusionLearningConfig?> GetByIterationAsync(
        int iteration,
        CancellationToken cancellationToken = default);
}
