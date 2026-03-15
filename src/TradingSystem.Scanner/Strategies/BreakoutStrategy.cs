using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Scanner.Strategies;

/// <summary>
/// Breakout strategy: Identifies price breaking out of consolidation
/// Uses Bollinger Bands, volume, and support/resistance levels
/// </summary>
public class BreakoutStrategy : BaseTradingStrategy
{
    public override StrategyType StrategyType => StrategyType.BREAKOUT;
    public override string Name => "Breakout Trading";
    public override string Description => 
        "Captures strong moves as price breaks out of consolidation zones";

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
        var recentCandles = GetRecentCandles(candles, 20);

        // Skip if indicators are not ready
        if (indicators.BollingerMiddle == 0 || indicators.BollingerUpper == 0)
        {
            return new StrategySignal 
            { 
                IsValid = false, 
                Explanation = "Indicators still warming up" 
            };
        }

        // Check for consolidation first (Bollinger Band squeeze)
        var bandwidth = indicators.BollingerUpper > 0 
            ? (indicators.BollingerUpper - indicators.BollingerLower) / indicators.BollingerMiddle 
            : 0;

        var isConsolidating = bandwidth < 0.03m; // Tight bands indicate consolidation

        if (!isConsolidating && bandwidth < 0.05m)
        {
            signal.Signals.Add($"Potential consolidation (bandwidth: {bandwidth:P1})");
        }

        // Bullish Breakout
        if (IsBullishBreakout(lastCandle, indicators, recentCandles))
        {
            signal.Direction = "BUY";
            signal.EntryPrice = lastCandle.Close;
            signal.StopLoss = indicators.BollingerMiddle;
            signal.Target = lastCandle.Close + (lastCandle.Close - indicators.BollingerMiddle) * 2;
            signal.Signals.Add($"Bullish breakout above Bollinger Upper ({indicators.BollingerUpper:F2})");
            
            // Check volume confirmation
            var avgVolume = (decimal)recentCandles.Average(c => c.Volume);
            if (lastCandle.Volume > avgVolume * 1.5m)
            {
                signal.Signals.Add($"Strong volume confirmation ({lastCandle.Volume / avgVolume:F1}x avg)");
            }
        }
        // Bearish Breakout
        else if (IsBearishBreakout(lastCandle, indicators, recentCandles))
        {
            signal.Direction = "SELL";
            signal.EntryPrice = lastCandle.Close;
            signal.StopLoss = indicators.BollingerMiddle;
            signal.Target = lastCandle.Close - (indicators.BollingerMiddle - lastCandle.Close) * 2;
            signal.Signals.Add($"Bearish breakout below Bollinger Lower ({indicators.BollingerLower:F2})");
            
            var avgVolume = (decimal)recentCandles.Average(c => c.Volume);
            if (lastCandle.Volume > avgVolume * 1.5m)
            {
                signal.Signals.Add($"Strong volume confirmation ({lastCandle.Volume / avgVolume:F1}x avg)");
            }
        }
        else
        {
            signal.Explanation = "No breakout detected";
            return signal;
        }

        signal.Score = CalculateScore(candles, indicators);
        signal.Confidence = CalculateConfidence(lastCandle, recentCandles, indicators, bandwidth);
        signal.IsValid = signal.Score >= 60 && signal.Confidence >= 65;

        signal.Metrics = new Dictionary<string, decimal>
        {
            ["Bandwidth"] = bandwidth * 100,
            ["VolumeRatio"] = (decimal)(lastCandle.Volume / recentCandles.Average(c => c.Volume)),
            ["ADX"] = indicators.ADX,
            ["RiskReward"] = CalculateRiskRewardRatio(signal.EntryPrice, signal.StopLoss, signal.Target),
            ["DistanceFromBand"] = Math.Abs(lastCandle.Close - indicators.BollingerMiddle) / indicators.BollingerMiddle * 100
        };

        signal.Explanation = signal.IsValid
            ? $"{signal.Direction} breakout with {signal.Confidence:F0}% confidence"
            : "Breakout signal below threshold";

        return signal;
    }

    public override int CalculateScore(List<Candle> candles, IndicatorValues indicators)
    {
        var score = 0;
        var lastCandle = candles.Last();
        var recentCandles = GetRecentCandles(candles, 20);

        // Bandwidth compression (0-30 points)
        var bandwidth = (indicators.BollingerUpper - indicators.BollingerLower) / indicators.BollingerMiddle;
        if (bandwidth < 0.02m)
            score += 30; // Very tight squeeze
        else if (bandwidth < 0.03m)
            score += 20;
        else if (bandwidth < 0.05m)
            score += 10;

        // Volume surge (0-30 points)
        var avgVolume = recentCandles.Average(c => c.Volume);
        var volumeRatio = lastCandle.Volume / avgVolume;
        if (volumeRatio > 2.0d)
            score += 30;
        else if (volumeRatio > 1.5d)
            score += 20;
        else if (volumeRatio > 1.2d)
            score += 10;

        // Price distance from band (0-20 points)
        var distanceFromMiddle = Math.Abs(lastCandle.Close - indicators.BollingerMiddle);
        var bandWidth = indicators.BollingerUpper - indicators.BollingerMiddle;
        if (distanceFromMiddle > bandWidth * 1.2m)
            score += 20; // Strong breakout
        else if (distanceFromMiddle > bandWidth)
            score += 15;

        // Trend strength (0-20 points)
        if (indicators.ADX > 25)
            score += 20;
        else if (indicators.ADX > 20)
            score += 10;

        return Math.Min(score, 100);
    }

    private bool IsBullishBreakout(Candle lastCandle, IndicatorValues indicators, List<Candle> recentCandles)
    {
        return lastCandle.Close > indicators.BollingerUpper &&
               lastCandle.Close > lastCandle.Open && // Bullish candle
               indicators.RSI > 50 &&
               indicators.RSI < 80;
    }

    private bool IsBearishBreakout(Candle lastCandle, IndicatorValues indicators, List<Candle> recentCandles)
    {
        return lastCandle.Close < indicators.BollingerLower &&
               lastCandle.Close < lastCandle.Open && // Bearish candle
               indicators.RSI < 50 &&
               indicators.RSI > 20;
    }

    private decimal CalculateConfidence(
        Candle lastCandle,
        List<Candle> recentCandles,
        IndicatorValues indicators,
        decimal bandwidth)
    {
        var confidence = 50m;

        // Tight consolidation before breakout
        if (bandwidth < 0.02m)
            confidence += 20;
        else if (bandwidth < 0.03m)
            confidence += 10;

        // Volume confirmation
        var volumeRatio = lastCandle.Volume / recentCandles.Average(c => c.Volume);
        if (volumeRatio > 2.0d)
            confidence += 20;
        else if (volumeRatio > 1.5d)
            confidence += 10;

        // Strong candle body
        var candleBody = Math.Abs(lastCandle.Close - lastCandle.Open);
        var candleRange = lastCandle.High - lastCandle.Low;
        if (candleRange > 0 && candleBody / candleRange > 0.7m)
            confidence += 10;

        return Math.Min(confidence, 100);
    }
}