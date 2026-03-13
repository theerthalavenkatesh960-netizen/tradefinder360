using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Scanner.Strategies;

/// <summary>
/// Swing Trading strategy: Captures multi-day price swings
/// Uses trend following with EMA crossovers and MACD
/// </summary>
public class SwingTradingStrategy : BaseTradingStrategy
{
    public override StrategyType StrategyType => StrategyType.SWING_TRADING;
    public override string Name => "Swing Trading";
    public override string Description => 
        "Captures multi-day price swings using trend and momentum indicators";

    public override StrategySignal Evaluate(
        TradingInstrument instrument,
        List<Candle> candles,
        IndicatorValues indicators,
        MarketContext? marketContext = null)
    {
        var signal = new StrategySignal
        {
            Strategy = StrategyType,
            IsValid = false,
            Signals = new List<string>()
        };

        var lastCandle = candles.Last();
        var recentCandles = GetRecentCandles(candles, 10);

        // Need moderate trend (ADX between 20-35)
        if (indicators.ADX < 18)
        {
            signal.Explanation = "ADX too low - no trend for swing trade";
            return signal;
        }

        if (indicators.ADX > 40)
        {
            signal.Signals.Add("Warning: Very strong trend - may be late entry");
        }
        else
        {
            signal.Signals.Add($"Good swing trend (ADX: {indicators.ADX:F1})");
        }

        // Bullish Swing
        if (IsBullishSwing(indicators, recentCandles))
        {
            signal.Direction = "BUY";
            signal.EntryPrice = lastCandle.Close;
            signal.StopLoss = CalculateSwingLow(recentCandles);
            signal.Target = lastCandle.Close + (lastCandle.Close - signal.StopLoss) * 2.5m;
            signal.Signals.Add("Bullish swing: EMA fast > slow, MACD bullish crossover");
            
            // Check market alignment
            if (marketContext?.Sentiment == SentimentType.BULLISH)
            {
                signal.Signals.Add("Market sentiment aligned with swing direction");
            }
        }
        // Bearish Swing
        else if (IsBearishSwing(indicators, recentCandles))
        {
            signal.Direction = "SELL";
            signal.EntryPrice = lastCandle.Close;
            signal.StopLoss = CalculateSwingHigh(recentCandles);
            signal.Target = lastCandle.Close - (signal.StopLoss - lastCandle.Close) * 2.5m;
            signal.Signals.Add("Bearish swing: EMA fast < slow, MACD bearish crossover");
            
            if (marketContext?.Sentiment == SentimentType.BEARISH)
            {
                signal.Signals.Add("Market sentiment aligned with swing direction");
            }
        }
        else
        {
            signal.Explanation = "No clear swing setup detected";
            return signal;
        }

        signal.Score = CalculateScore(candles, indicators);
        signal.Confidence = CalculateConfidence(indicators, recentCandles, marketContext);
        signal.IsValid = signal.Score >= 60 && signal.Confidence >= 60;

        signal.Metrics = new Dictionary<string, decimal>
        {
            ["ADX"] = indicators.ADX,
            ["MACD"] = indicators.MacdLine,
            ["MACDSignal"] = indicators.MacdSignal,
            ["EMAFast"] = indicators.EMAFast,
            ["EMASlow"] = indicators.EMASlow,
            ["RiskReward"] = CalculateRiskRewardRatio(signal.EntryPrice, signal.StopLoss, signal.Target),
            ["SwingRange%"] = CalculateSwingRange(recentCandles)
        };

        signal.Explanation = signal.IsValid
            ? $"{signal.Direction} swing trade setup with {signal.Confidence:F0}% confidence"
            : "Swing signal below threshold";

        return signal;
    }

    public override int CalculateScore(List<Candle> candles, IndicatorValues indicators)
    {
        var score = 0;
        var recentCandles = GetRecentCandles(candles, 10);

        // Good ADX range for swings (0-25 points)
        if (indicators.ADX >= 20 && indicators.ADX <= 35)
            score += 25;
        else if (indicators.ADX > 18 && indicators.ADX < 40)
            score += 15;

        // EMA alignment (0-30 points)
        var emaSpread = Math.Abs(indicators.EMAFast - indicators.EMASlow);
        var emaPct = indicators.EMASlow > 0 ? (emaSpread / indicators.EMASlow) * 100 : 0;
        if (emaPct > 2)
            score += 30; // Strong separation
        else if (emaPct > 1)
            score += 20;
        else if (emaPct > 0.5m)
            score += 10;

        // MACD confirmation (0-25 points)
        if (indicators.MacdHistogram != 0 && 
            Math.Sign(indicators.MacdLine) == Math.Sign(indicators.MacdHistogram))
        {
            score += 25; // MACD aligned
        }
        else if (Math.Abs(indicators.MacdHistogram) > 0)
        {
            score += 10;
        }

        // Consistent candle direction (0-20 points)
        var bullishCandles = recentCandles.Count(c => c.Close > c.Open);
        var bearishCandles = recentCandles.Count(c => c.Close < c.Open);
        if (bullishCandles > 7 || bearishCandles > 7)
            score += 20; // Strong consistency
        else if (bullishCandles > 6 || bearishCandles > 6)
            score += 10;

        return Math.Min(score, 100);
    }

    private bool IsBullishSwing(IndicatorValues indicators, List<Candle> recentCandles)
    {
        return indicators.EMAFast > indicators.EMASlow &&
               indicators.MacdLine > indicators.MacdSignal &&
               indicators.MacdHistogram > 0 &&
               indicators.PlusDI > indicators.MinusDI &&
               recentCandles.Last().Close > recentCandles.Last().Open;
    }

    private bool IsBearishSwing(IndicatorValues indicators, List<Candle> recentCandles)
    {
        return indicators.EMAFast < indicators.EMASlow &&
               indicators.MacdLine < indicators.MacdSignal &&
               indicators.MacdHistogram < 0 &&
               indicators.MinusDI > indicators.PlusDI &&
               recentCandles.Last().Close < recentCandles.Last().Open;
    }

    private decimal CalculateSwingLow(List<Candle> candles)
    {
        return candles.Min(c => c.Low);
    }

    private decimal CalculateSwingHigh(List<Candle> candles)
    {
        return candles.Max(c => c.High);
    }

    private decimal CalculateSwingRange(List<Candle> candles)
    {
        var high = candles.Max(c => c.High);
        var low = candles.Min(c => c.Low);
        return low > 0 ? ((high - low) / low) * 100 : 0;
    }

    private decimal CalculateConfidence(
        IndicatorValues indicators,
        List<Candle> recentCandles,
        MarketContext? marketContext)
    {
        var confidence = 50m;

        // ADX in sweet spot
        if (indicators.ADX >= 20 && indicators.ADX <= 35)
            confidence += 20;
        else if (indicators.ADX > 18 && indicators.ADX < 40)
            confidence += 10;

        // Strong EMA separation
        var emaSpread = Math.Abs(indicators.EMAFast - indicators.EMASlow);
        var emaPct = indicators.EMASlow > 0 ? (emaSpread / indicators.EMASlow) * 100 : 0;
        if (emaPct > 2)
            confidence += 15;
        else if (emaPct > 1)
            confidence += 10;

        // MACD histogram growing
        if (Math.Abs(indicators.MacdHistogram) > Math.Abs(indicators.MacdLine) * 0.1m)
            confidence += 10;

        // Market sentiment alignment
        if (marketContext != null)
        {
            var isAligned = (indicators.EMAFast > indicators.EMASlow && marketContext.Sentiment == SentimentType.BULLISH) ||
                           (indicators.EMAFast < indicators.EMASlow && marketContext.Sentiment == SentimentType.BEARISH);
            if (isAligned)
                confidence += 10;
        }

        return Math.Min(confidence, 100);
    }
}