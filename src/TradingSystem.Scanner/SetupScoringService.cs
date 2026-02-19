using TradingSystem.Core.Models;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Models;

namespace TradingSystem.Scanner;

public class SetupScoringService
{
    private readonly ScannerConfig _config;

    public SetupScoringService(ScannerConfig config)
    {
        _config = config;
    }

    public ScanResult Score(TradingInstrument instrument, IndicatorValues indicators, List<Candle> recentCandles)
    {
        var result = new ScanResult
        {
            InstrumentKey = instrument.InstrumentKey,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange,
            LastClose = recentCandles.LastOrDefault()?.Close ?? 0,
            ATR = indicators.ATR,
            ScannedAt = DateTime.UtcNow
        };

        var breakdown = new ScoreBreakdown();

        breakdown.AdxScore = ScoreADX(indicators, result.Reasons);
        breakdown.RsiScore = ScoreRSI(indicators, result.Reasons, out var rsiBias);
        breakdown.EmaVwapScore = ScoreEmaVwap(indicators, result.Reasons, out var emaVwapBias);
        breakdown.VolumeScore = ScoreVolume(recentCandles, result.Reasons);
        breakdown.BollingerScore = ScoreBollinger(indicators, result.Reasons);
        breakdown.StructureScore = ScoreStructure(recentCandles, indicators, result.Reasons);

        result.ScoreBreakdown = breakdown;
        result.SetupScore = breakdown.Total;
        result.Bias = DetermineBias(rsiBias, emaVwapBias, indicators);
        result.MarketState = DetermineMarketState(indicators, recentCandles, result.Bias);

        return result;
    }

    private int ScoreADX(IndicatorValues indicators, List<string> reasons)
    {
        var weight = _config.Weights.AdxWeight;
        if (indicators.ADX > 30)
        {
            reasons.Add($"Strong trend: ADX {indicators.ADX:F1} > 30");
            return weight;
        }
        if (indicators.ADX > 20)
        {
            reasons.Add($"Moderate trend: ADX {indicators.ADX:F1} > 20");
            return (int)(weight * 0.5);
        }
        reasons.Add($"Weak/sideways: ADX {indicators.ADX:F1} < 20");
        return 0;
    }

    private int ScoreRSI(IndicatorValues indicators, List<string> reasons, out string bias)
    {
        var weight = _config.Weights.RsiWeight;
        bias = "NONE";

        if (indicators.RSI >= 45 && indicators.RSI <= 55)
        {
            bias = "NONE";
            reasons.Add($"RSI neutral zone {indicators.RSI:F1} — no directional edge");
            return (int)(weight * 0.3);
        }

        if (indicators.RSI >= 45 && indicators.RSI <= 60)
        {
            bias = "BULLISH";
            reasons.Add($"RSI bullish pullback zone: {indicators.RSI:F1}");
            return weight;
        }

        if (indicators.RSI >= 40 && indicators.RSI <= 55)
        {
            bias = "BEARISH";
            reasons.Add($"RSI bearish pullback zone: {indicators.RSI:F1}");
            return weight;
        }

        if (indicators.RSI > 70)
        {
            bias = "BULLISH";
            reasons.Add($"RSI overbought {indicators.RSI:F1} — overextended");
            return (int)(weight * 0.3);
        }

        if (indicators.RSI < 30)
        {
            bias = "BEARISH";
            reasons.Add($"RSI oversold {indicators.RSI:F1} — overextended");
            return (int)(weight * 0.3);
        }

        return (int)(weight * 0.5);
    }

    private int ScoreEmaVwap(IndicatorValues indicators, List<string> reasons, out string bias)
    {
        var weight = _config.Weights.EmaVwapWeight;
        bias = "NONE";
        var score = 0;

        var lastClose = indicators.EMASlow > 0 ? indicators.EMASlow : 0;

        if (indicators.EMAFast > indicators.EMASlow)
        {
            bias = "BULLISH";
            score += (int)(weight * 0.5);
            reasons.Add($"EMA fast {indicators.EMAFast:F1} > EMA slow {indicators.EMASlow:F1} (bullish)");
        }
        else if (indicators.EMAFast < indicators.EMASlow)
        {
            bias = "BEARISH";
            score += (int)(weight * 0.5);
            reasons.Add($"EMA fast {indicators.EMAFast:F1} < EMA slow {indicators.EMASlow:F1} (bearish)");
        }

        if (indicators.VWAP > 0)
        {
            if (indicators.EMAFast > indicators.VWAP && bias == "BULLISH")
            {
                score += (int)(weight * 0.5);
                reasons.Add($"Price above VWAP {indicators.VWAP:F1} — bullish confirmation");
            }
            else if (indicators.EMAFast < indicators.VWAP && bias == "BEARISH")
            {
                score += (int)(weight * 0.5);
                reasons.Add($"Price below VWAP {indicators.VWAP:F1} — bearish confirmation");
            }
        }

        return Math.Min(score, weight);
    }

    private int ScoreVolume(List<Candle> candles, List<string> reasons)
    {
        var weight = _config.Weights.VolumeWeight;
        if (candles.Count < 5) return 0;

        var recent = candles.TakeLast(3).Average(c => c.Volume);
        var baseline = candles.SkipLast(3).TakeLast(10).Average(c => c.Volume);

        if (baseline == 0) return 0;

        var ratio = recent / baseline;
        if (ratio >= 1.5)
        {
            reasons.Add($"Volume expansion {ratio:F1}x above average");
            return weight;
        }
        if (ratio >= 1.2)
        {
            reasons.Add($"Moderate volume expansion {ratio:F1}x");
            return (int)(weight * 0.5);
        }

        reasons.Add("Volume below average — weak conviction");
        return 0;
    }

    private int ScoreBollinger(IndicatorValues indicators, List<string> reasons)
    {
        var weight = _config.Weights.BollingerWeight;
        if (indicators.BollingerUpper == 0 || indicators.BollingerLower == 0)
            return 0;

        var bandwidth = (indicators.BollingerUpper - indicators.BollingerLower) / indicators.BollingerMiddle;

        if (bandwidth > 0.04m)
        {
            reasons.Add($"Bollinger bands expanding ({bandwidth:P1}) — volatility increasing");
            return weight;
        }

        if (bandwidth < 0.02m)
        {
            reasons.Add($"Bollinger squeeze ({bandwidth:P1}) — breakout imminent");
            return (int)(weight * 0.8);
        }

        return (int)(weight * 0.4);
    }

    private int ScoreStructure(List<Candle> candles, IndicatorValues indicators, List<string> reasons)
    {
        var weight = _config.Weights.StructureWeight;
        if (candles.Count < 10) return 0;

        var score = 0;

        if (indicators.MacdLine > 0 && indicators.MacdHistogram > 0)
        {
            score += (int)(weight * 0.5);
            reasons.Add("MACD bullish — momentum supporting structure");
        }
        else if (indicators.MacdLine < 0 && indicators.MacdHistogram < 0)
        {
            score += (int)(weight * 0.5);
            reasons.Add("MACD bearish — momentum confirming downtrend");
        }

        var last3 = candles.TakeLast(3).ToList();
        var isClean = last3.All(c => Math.Abs(c.Close - c.Open) / c.Open > 0.001m);
        if (isClean)
        {
            score += (int)(weight * 0.5);
            reasons.Add("Clean candle structure — clear directional intent");
        }

        return Math.Min(score, weight);
    }

    private ScanBias DetermineBias(string rsiBias, string emaVwapBias, IndicatorValues indicators)
    {
        var bullishVotes = 0;
        var bearishVotes = 0;

        if (rsiBias == "BULLISH") bullishVotes++;
        if (rsiBias == "BEARISH") bearishVotes++;
        if (emaVwapBias == "BULLISH") bullishVotes++;
        if (emaVwapBias == "BEARISH") bearishVotes++;
        if (indicators.MacdLine > 0) bullishVotes++;
        if (indicators.MacdLine < 0) bearishVotes++;
        if (indicators.PlusDI > indicators.MinusDI) bullishVotes++;
        if (indicators.MinusDI > indicators.PlusDI) bearishVotes++;

        if (bullishVotes > bearishVotes) return ScanBias.BULLISH;
        if (bearishVotes > bullishVotes) return ScanBias.BEARISH;
        return ScanBias.NONE;
    }

    private ScanMarketState DetermineMarketState(IndicatorValues indicators, List<Candle> candles, ScanBias bias)
    {
        if (indicators.ADX < 20)
            return ScanMarketState.SIDEWAYS;

        var bandwidth = indicators.BollingerUpper > 0
            ? (indicators.BollingerUpper - indicators.BollingerLower) / indicators.BollingerMiddle
            : 0;

        if (bandwidth > 0.06m)
            return ScanMarketState.OVEREXTENDED;

        var isPullback = indicators.RSI >= 42 && indicators.RSI <= 58 && indicators.ADX > 20;

        if (isPullback)
            return ScanMarketState.PULLBACK_READY;

        if (bias == ScanBias.BULLISH && indicators.ADX > 20)
            return ScanMarketState.TRENDING_BULLISH;

        if (bias == ScanBias.BEARISH && indicators.ADX > 20)
            return ScanMarketState.TRENDING_BEARISH;

        return ScanMarketState.SIDEWAYS;
    }
}
