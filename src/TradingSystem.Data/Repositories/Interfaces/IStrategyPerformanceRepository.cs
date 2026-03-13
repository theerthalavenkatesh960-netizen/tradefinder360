using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface IStrategyPerformanceRepository : ICommonRepository<StrategyPerformance>
{
    Task<StrategyPerformance?> GetLatestPerformanceAsync(
        StrategyType strategyType,
        CancellationToken cancellationToken = default);

    Task<List<StrategyPerformance>> GetPerformanceHistoryAsync(
        StrategyType strategyType,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    Task<List<StrategyPerformance>> GetAllStrategiesPerformanceAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
}