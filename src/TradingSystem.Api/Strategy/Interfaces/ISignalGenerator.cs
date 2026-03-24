using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

public interface ISignalGenerator
{
    /// <summary>
    /// Called only after ALL hard-gate filters have passed.
    /// Computes entry, SL, targets, and confidence score.
    /// Returns null only if arithmetic produces invalid values (SL >= entry for bullish, etc.).
    /// </summary>
    Task<TradeSignal?> GenerateAsync(
        string symbol,
        OpeningRange or,
        BreakoutResult breakout,
        FairValueGap fvg,
        Candle confirmationCandle,
        StrategyConfig config,
        CancellationToken ct = default);
}
