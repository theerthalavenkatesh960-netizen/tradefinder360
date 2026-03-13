using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface IStrategySignalRepository : ICommonRepository<StrategySignalRecord>
{
    Task<List<StrategySignalRecord>> GetByStrategyTypeAsync(
        StrategyType strategyType,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    Task<List<StrategySignalRecord>> GetByInstrumentAsync(
        int instrumentId,
        StrategyType? strategyType = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<List<StrategySignalRecord>> GetValidSignalsAsync(
        StrategyType? strategyType = null,
        int minConfidence = 60,
        CancellationToken cancellationToken = default);

    Task<StrategySignalRecord?> GetLatestSignalAsync(
        int instrumentId,
        StrategyType strategyType,
        CancellationToken cancellationToken = default);

    Task<List<StrategySignalRecord>> GetUnactedSignalsAsync(
        DateTimeOffset? expiresAfter = null,
        CancellationToken cancellationToken = default);

    Task MarkAsActedUponAsync(
        int signalId,
        int tradeId,
        CancellationToken cancellationToken = default);
}