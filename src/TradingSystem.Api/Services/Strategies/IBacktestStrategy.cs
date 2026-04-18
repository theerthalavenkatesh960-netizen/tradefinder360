using TradingSystem.Api.DTOs;

namespace TradingSystem.Api.Services.Strategies;

/// <summary>
/// Contract for every backtesting strategy.
/// Add a new strategy by: creating a class that implements this interface,
/// registering it in BacktestStrategyRegistry — no other changes required.
/// </summary>
public interface IBacktestStrategy
{
    /// <summary>Upper-case strategy key, e.g. "ORB", "EMA_CROSSOVER".</summary>
    string StrategyName { get; }

    BacktestStrategyResult Execute(BacktestRunContext ctx);
}
