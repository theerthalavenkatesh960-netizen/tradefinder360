using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Scanner.Strategies;

/// <summary>
/// Interface for all trading strategies
/// </summary>
public interface ITradingStrategy
{
    StrategyType StrategyType { get; }
    string Name { get; }
    string Description { get; }
    
    /// <summary>
    /// Evaluate if the strategy conditions are met
    /// </summary>
    StrategySignal Evaluate(
        TradingInstrument instrument,
        List<Candle> candles,
        IndicatorValues indicators,
        MarketContext? marketContext = null);
    
    /// <summary>
    /// Calculate strategy-specific score
    /// </summary>
    int CalculateScore(
        List<Candle> candles,
        IndicatorValues indicators);
    
    /// <summary>
    /// Validate if instrument is suitable for this strategy
    /// </summary>
    bool IsInstrumentSuitable(
        TradingInstrument instrument,
        List<Candle> candles);
}