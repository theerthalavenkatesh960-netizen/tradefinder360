using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Scanner.Strategies;

/// <summary>
/// Mean Reversion strategy: Trades when price deviates significantly from average
/// Uses RSI, Bollinger Bands, and VWAP for mean reversion signals
/// </summary>
public class MeanReversionStrategy : BaseTradingStrategy
{
    public override StrategyType StrategyType => StrategyType.MEAN_REVERSION;
    public override string Name => "Mean Reversion";
    public override string Description => 
        "Identifies oversold/overbought conditions for reversion trades";

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

        // Mean reversion works best in ranging markets (ADX < 25)
        if (indicators.ADX > 30)
        {
            signal.Explanation = "Market too trending for mean reversion (ADX > 30)";
            return signal;
        }

        signal.Signals.Add($"Ranging market detected (ADX: {indicators.ADX:F1})");

        // Bullish Mean Reversion (Oversold)
        if (IsOversold(lastCandle, indicators))
        {
            signal.Direction = "BUY";
            signal.EntryPrice = lastCandle.Close;
            signal.StopLoss = indicators.BollingerLower;
            signal.Target = indicators.VWAP > 0 ? indicators.VWAP : indicators.BollingerMiddle;
            signal.Signals.Add($"Oversold: RSI {indicators.RSI:F1}, price below Bollinger Lower");
            
            // Distance from VWAP
            if (indicators.VWAP > 0)
            {
                var vwapDistance = ((indicators.VWAP - lastCandle.Close) / lastCandle.Close) * 100;
                signal.Signals.Add($"Price {vwapDistance:F1}% below VWAP");
            }
        }
        // Bearish Mean Reversion (Overbought)
        else if (IsOverbought(lastCandle, indicators))
        {
            signal.Direction = "SELL";
            signal.EntryPrice = lastCandle.Close;
            signal.StopLoss = indicators.BollingerUpper;
            signal.Target = indicators.VWAP > 0 ? indicators.VWAP : indicators.BollingerMiddle;
            signal.Signals.Add($"Overbought: RSI {indicators.RSI:F1}, price above Bollinger Upper");
            
            if (indicators.VWAP > 0)
            {
                var vwapDistance = ((lastCandle.Close - indicators.VWAP) / indicators.VWAP) * 100;
                signal.Signals.Add($"Price {vwapDistance:F1}% above VWAP");
            }
        }
        else
        {
            signal.Explanation = "No extreme deviation from mean detected";
            return signal;
        }

        signal.Score = CalculateScore(candles, indicators);
        signal.Confidence = CalculateConfidence(indicators, lastCandle);
        signal.IsValid = signal.Score >= 55 && signal.Confidence >= 60;

        signal.Metrics = new Dictionary<string, decimal>
        {
            ["RSI"] = indicators.RSI,
            ["ADX"] = indicators.ADX,
            ["VWAPDistance%"] = indicators.VWAP > 0 
                ? ((lastCandle.Close - indicators.VWAP) / indicators.VWAP) * 100 
                : 0,
            ["BollingerPosition"] = CalculateBollingerPosition(lastCandle.Close, indicators),
            ["RiskReward"] = CalculateRiskRewardRatio(signal.EntryPrice, signal.StopLoss, signal.Target)
        };

        signal.Explanation = signal.IsValid
            ? $"Mean reversion {signal.Direction} signal with {signal.Confidence:F0}% confidence"
            : "Mean reversion signal below threshold";

        return signal;
    }

    public override int CalculateScore(List<Candle> candles, IndicatorValues indicators)
    {
        var score = 0;
        var lastCandle = candles.Last();

        // Low ADX = ranging market (0-25 points)
        if (indicators.ADX < 20)
            score += 25;
        else if (indicators.ADX < 25)
            score += 15;
        else if (indicators.ADX < 30)
            score += 5;

        // RSI extremes (0-35 points)
        if (indicators.RSI < 25 || indicators.RSI > 75)
            score += 35;
        else if (indicators.RSI < 30 || indicators.RSI > 70)
            score += 25;
        else if (indicators.RSI < 35 || indicators.RSI > 65)
            score += 10;

        // Bollinger Band position (0-25 points)
        var bbPosition = CalculateBollingerPosition(lastCandle.Close, indicators);
        if (bbPosition < -0.1m || bbPosition > 1.1m) // Outside bands
            score += 25;
        else if (bbPosition < 0.1m || bbPosition > 0.9m) // Near bands
            score += 15;

        // VWAP deviation (0-15 points)
        if (indicators.VWAP > 0)
        {
            var vwapDev = Math.Abs((lastCandle.Close - indicators.VWAP) / indicators.VWAP);
            if (vwapDev > 0.03m) // More than 3% deviation
                score += 15;
            else if (vwapDev > 0.02m)
                score += 10;
        }

        return Math.Min(score, 100);
    }

    public override bool IsInstrumentSuitable(
        TradingInstrument instrument,
        List<Candle> candles)
    {
        if (!base.IsInstrumentSuitable(instrument, candles))
            return false;

        // Mean reversion works better with liquid stocks
        var avgVolume = candles.TakeLast(20).Average(c => c.Volume);
        return avgVolume > 500000;
    }

    private bool IsOversold(Candle lastCandle, IndicatorValues indicators)
    {
        return indicators.RSI < 30 &&
               lastCandle.Close < indicators.BollingerLower &&
               (indicators.VWAP == 0 || lastCandle.Close < indicators.VWAP);
    }

    private bool IsOverbought(Candle lastCandle, IndicatorValues indicators)
    {
        return indicators.RSI > 70 &&
               lastCandle.Close > indicators.BollingerUpper &&
               (indicators.VWAP == 0 || lastCandle.Close > indicators.VWAP);
    }

    private decimal CalculateBollingerPosition(decimal price, IndicatorValues indicators)
    {
        if (indicators.BollingerUpper == indicators.BollingerLower)
            return 0.5m;

        return (price - indicators.BollingerLower) / (indicators.BollingerUpper - indicators.BollingerLower);
    }

    private decimal CalculateConfidence(IndicatorValues indicators, Candle lastCandle)
    {
        var confidence = 50m;

        // Strong RSI signal
        if (indicators.RSI < 25 || indicators.RSI > 75)
            confidence += 25;
        else if (indicators.RSI < 30 || indicators.RSI > 70)
            confidence += 15;

        // Low ADX confirms ranging
        if (indicators.ADX < 20)
            confidence += 15;

        // Price outside Bollinger Bands
        if (lastCandle.Close < indicators.BollingerLower || lastCandle.Close > indicators.BollingerUpper)
            confidence += 10;

        return Math.Min(confidence, 100);
    }
}