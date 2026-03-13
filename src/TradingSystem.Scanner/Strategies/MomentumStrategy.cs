using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Scanner.Strategies;

/// <summary>
/// Momentum strategy: Trades in the direction of strong trends
/// Uses RSI, MACD, and ADX for momentum confirmation
/// </summary>
public class MomentumStrategy : BaseTradingStrategy
{
    public override StrategyType StrategyType => StrategyType.MOMENTUM;
    public override string Name => "Momentum Trading";
    public override string Description => 
        "Identifies and trades strong trending moves with momentum indicators";

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
        
        // Check for strong trend (ADX > 25)
        if (indicators.ADX < 25)
        {
            signal.Explanation = "ADX too low - no strong trend";
            return signal;
        }

        signal.Signals.Add($"Strong trend detected (ADX: {indicators.ADX:F1})");

        // Bullish Momentum
        if (IsBullishMomentum(indicators, candles))
        {
            signal.Direction = "BUY";
            signal.EntryPrice = lastCandle.Close;
            signal.StopLoss = lastCandle.Close - (indicators.ATR * 2);
            signal.Target = lastCandle.Close + (indicators.ATR * 4);
            signal.Signals.Add($"Bullish momentum: RSI {indicators.RSI:F1}, MACD positive");
            
            // Adjust for market sentiment
            if (marketContext?.Sentiment == SentimentType.BULLISH)
            {
                signal.Signals.Add("Market sentiment supports bullish momentum");
                signal.Confidence += 10;
            }
        }
        // Bearish Momentum
        else if (IsBearishMomentum(indicators, candles))
        {
            signal.Direction = "SELL";
            signal.EntryPrice = lastCandle.Close;
            signal.StopLoss = lastCandle.Close + (indicators.ATR * 2);
            signal.Target = lastCandle.Close - (indicators.ATR * 4);
            signal.Signals.Add($"Bearish momentum: RSI {indicators.RSI:F1}, MACD negative");
            
            // Adjust for market sentiment
            if (marketContext?.Sentiment == SentimentType.BEARISH)
            {
                signal.Signals.Add("Market sentiment supports bearish momentum");
                signal.Confidence += 10;
            }
        }
        else
        {
            signal.Explanation = "No clear momentum direction";
            return signal;
        }

        signal.Score = CalculateScore(candles, indicators);
        signal.Confidence = CalculateConfidence(indicators, signal.Score);
        signal.IsValid = signal.Score >= 60 && signal.Confidence >= 60;
        
        signal.Metrics = new Dictionary<string, decimal>
        {
            ["ADX"] = indicators.ADX,
            ["RSI"] = indicators.RSI,
            ["MACD"] = indicators.MacdLine,
            ["ATR%"] = CalculateATRPercentage(candles, indicators.ATR),
            ["RiskReward"] = CalculateRiskRewardRatio(signal.EntryPrice, signal.StopLoss, signal.Target)
        };

        signal.Explanation = signal.IsValid
            ? $"Strong {signal.Direction} momentum with {signal.Confidence:F0}% confidence"
            : "Momentum signal below threshold";

        return signal;
    }

    public override int CalculateScore(List<Candle> candles, IndicatorValues indicators)
    {
        var score = 0;

        // ADX strength (0-30 points)
        if (indicators.ADX > 40)
            score += 30;
        else if (indicators.ADX > 30)
            score += 20;
        else if (indicators.ADX > 25)
            score += 10;

        // RSI momentum (0-25 points)
        if (indicators.RSI > 60 && indicators.RSI < 80)
            score += 25; // Bullish momentum zone
        else if (indicators.RSI < 40 && indicators.RSI > 20)
            score += 25; // Bearish momentum zone
        else if (indicators.RSI > 50)
            score += 10;

        // MACD alignment (0-25 points)
        if (indicators.MacdLine > 0 && indicators.MacdHistogram > 0)
            score += 25; // Bullish alignment
        else if (indicators.MacdLine < 0 && indicators.MacdHistogram < 0)
            score += 25; // Bearish alignment
        else if (Math.Abs(indicators.MacdHistogram) > 0)
            score += 10;

        // DI alignment (0-20 points)
        if (indicators.PlusDI > indicators.MinusDI + 5)
            score += 20; // Strong bullish
        else if (indicators.MinusDI > indicators.PlusDI + 5)
            score += 20; // Strong bearish

        return Math.Min(score, 100);
    }

    private bool IsBullishMomentum(IndicatorValues indicators, List<Candle> candles)
    {
        return indicators.RSI > 55 &&
               indicators.RSI < 80 &&
               indicators.MacdLine > 0 &&
               indicators.MacdHistogram > 0 &&
               indicators.PlusDI > indicators.MinusDI;
    }

    private bool IsBearishMomentum(IndicatorValues indicators, List<Candle> candles)
    {
        return indicators.RSI < 45 &&
               indicators.RSI > 20 &&
               indicators.MacdLine < 0 &&
               indicators.MacdHistogram < 0 &&
               indicators.MinusDI > indicators.PlusDI;
    }

    private decimal CalculateConfidence(IndicatorValues indicators, int score)
    {
        var confidence = (decimal)score;

        // Boost confidence for very strong trends
        if (indicators.ADX > 35)
            confidence += 10;

        // Reduce confidence for extreme RSI
        if (indicators.RSI > 80 || indicators.RSI < 20)
            confidence -= 15;

        return Math.Min(Math.Max(confidence, 0), 100);
    }
}