using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface IStrategyOrchestrator
{
    /// <summary>
    /// Runs the full strategy pipeline for one trading session.
    /// Accepts candle streams — works identically for live and backtesting.
    /// Emits at most ONE TradeSignal per session per symbol.
    /// </summary>
    IAsyncEnumerable<TradeSignal> RunAsync(
        string symbol,
        IAsyncEnumerable<Candle> oneMinCandles,
        DateOnly sessionDate,
        StrategyConfig config,
        CancellationToken ct = default);
}
