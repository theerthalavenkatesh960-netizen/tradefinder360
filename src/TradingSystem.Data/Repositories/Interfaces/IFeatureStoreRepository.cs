using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface IFeatureStoreRepository
{
    /// <summary>
    /// Store feature vector
    /// </summary>
    Task StoreFeatureVectorAsync(QuantFeatureVector vector, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store multiple feature vectors in batch
    /// </summary>
    Task StoreBatchAsync(List<QuantFeatureVector> vectors, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get latest feature vector for an instrument
    /// </summary>
    Task<QuantFeatureVector?> GetLatestAsync(int instrumentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get feature vectors for time range
    /// </summary>
    Task<List<QuantFeatureVector>> GetHistoricalAsync(
        int instrumentId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get feature vectors for multiple instruments at specific timestamp
    /// </summary>
    Task<List<QuantFeatureVector>> GetSnapshotAsync(
        List<int> instrumentIds,
        DateTimeOffset timestamp,
        TimeSpan tolerance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build training dataset with features and labels
    /// </summary>
    Task<List<TrainingDataPoint>> BuildTrainingDatasetAsync(
        int instrumentId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        int forwardReturnDays = 5,
        CancellationToken cancellationToken = default);
}