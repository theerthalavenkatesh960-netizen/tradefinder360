using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Scanner.Strategies;

/// <summary>
/// Base class for trading strategies with common functionality
/// </summary>
public abstract class BaseTradingStrategy : ITradingStrategy
{
    public abstract StrategyType StrategyType { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    public abstract StrategySignal Evaluate(
        TradingInstrument instrument,
        List<Candle> candles,
        IndicatorValues indicators,
        MarketContext? marketContext = null);

    public abstract int CalculateScore(
        List<Candle> candles,
        IndicatorValues indicators);

    public virtual bool IsInstrumentSuitable(
        TradingInstrument instrument,
        List<Candle> candles)
    {
        // Basic checks
        if (!instrument.IsActive || candles.Count < 50)
            return false;

        // Check for sufficient liquidity (volume)
        var avgVolume = candles.TakeLast(20).Average(c => c.Volume);
        if (avgVolume < 100000)
            return false;

        return true;
    }

    protected decimal CalculateATRPercentage(List<Candle> candles, decimal atr)
    {
        if (!candles.Any()) return 0;
        var lastClose = candles.Last().Close;
        return lastClose > 0 ? (atr / lastClose) * 100 : 0;
    }

    protected decimal CalculateRiskRewardRatio(
        decimal entryPrice,
        decimal stopLoss,
        decimal target)
    {
        var risk = Math.Abs(entryPrice - stopLoss);
        var reward = Math.Abs(target - entryPrice);
        return risk > 0 ? reward / risk : 0;
    }

    protected List<Candle> GetRecentCandles(List<Candle> candles, int count)
    {
        return candles.TakeLast(count).ToList();
    }
}