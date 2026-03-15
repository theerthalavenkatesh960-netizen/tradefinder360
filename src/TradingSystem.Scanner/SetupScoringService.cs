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
            InstrumentId = instrument.Id,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange,
            LastClose = recentCandles.LastOrDefault()?.Close ?? 0,
            ATR = indicators.ATR,
            ScannedAt = DateTime.UtcNow
        };

        var breakdown = new ScoreBreakdown();

        breakdown.AdxScore = ScoreADX(indicators, result.Reasons);
        breakdown.RsiScore = ScoreRSI(indicators, result.Reasons, out var rsiBias);
        breakdown.EmaVwapScore = ScoreEmaVwap(indicators, result.LastClose, result.Reasons, out var emaVwapBias);
        breakdown.VolumeScore = ScoreVolume(recentCandles, result.Reasons);
        breakdown.BollingerScore = ScoreBollinger(indicators, result.Reasons);
        breakdown.StructureScore = ScoreStructure(recentCandles, indicators, result.Reasons);

        result.ScoreBreakdown = breakdown;
        result.SetupScore = breakdown.Total;
        result.Bias = DetermineBias(rsiBias, emaVwapBias, indicators, result.LastClose);
        result.MarketState = DetermineMarketState(indicators, recentCandles, result.Bias, result.LastClose);

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

        // ✅ FIXED: Check directional zones FIRST, then neutral last.
        // This prevents 45-55 neutral from swallowing valid pullback signals.

        if (indicators.RSI > 70)
        {
            bias = "BULLISH";
            reasons.Add($"RSI overbought {indicators.RSI:F1} — overextended, avoid new entries");
            return (int)(weight * 0.3);
        }

        if (indicators.RSI < 30)
        {
            bias = "BEARISH";
            reasons.Add($"RSI oversold {indicators.RSI:F1} — overextended, avoid new entries");
            return (int)(weight * 0.3);
        }

        // Bullish pullback: 45-60 (but NOT overbought >70, already handled)
        if (indicators.RSI > 55 && indicators.RSI <= 60)
        {
            bias = "BULLISH";
            reasons.Add($"RSI bullish pullback zone: {indicators.RSI:F1}");
            return weight;
        }

        // Bearish pullback: 40-45 (but NOT oversold <30, already handled)
        if (indicators.RSI >= 40 && indicators.RSI < 45)
        {
            bias = "BEARISH";
            reasons.Add($"RSI bearish pullback zone: {indicators.RSI:F1}");
            return weight;
        }

        // Neutral zone: 45-55 — no directional edge
        if (indicators.RSI >= 45 && indicators.RSI <= 55)
        {
            bias = "NONE";
            reasons.Add($"RSI neutral zone {indicators.RSI:F1} — no directional edge");
            return (int)(weight * 0.3);
        }

        // RSI 30-40 or 60-70 — mild directional but not ideal pullback
        if (indicators.RSI >= 60)
        {
            bias = "BULLISH";
            reasons.Add($"RSI {indicators.RSI:F1} — bullish but approaching overbought");
            return (int)(weight * 0.5);
        }

        if (indicators.RSI <= 40)
        {
            bias = "BEARISH";
            reasons.Add($"RSI {indicators.RSI:F1} — bearish but approaching oversold");
            return (int)(weight * 0.5);
        }

        return (int)(weight * 0.5);
    }

    private int ScoreEmaVwap(IndicatorValues indicators, decimal lastClose, List<string> reasons, out string bias)
    {
        var weight = _config.Weights.EmaVwapWeight;
        bias = "NONE";
        var score = 0;

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

        // ✅ FIXED: Use lastClose instead of EMAFast for VWAP comparison
        if (indicators.VWAP > 0)
        {
            if (lastClose > indicators.VWAP && bias == "BULLISH")
            {
                score += (int)(weight * 0.5);
                reasons.Add($"Price {lastClose:F1} above VWAP {indicators.VWAP:F1} — bullish confirmation");
            }
            else if (lastClose < indicators.VWAP && bias == "BEARISH")
            {
                score += (int)(weight * 0.5);
                reasons.Add($"Price {lastClose:F1} below VWAP {indicators.VWAP:F1} — bearish confirmation");
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
        // ✅ FIXED: Also check BollingerMiddle to prevent division by zero
        if (indicators.BollingerUpper == 0 || indicators.BollingerLower == 0 || indicators.BollingerMiddle == 0)
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

    /// <summary>
    /// ✅ FIXED: Bias now requires EMA alignment AND VWAP confirmation to agree.
    /// If they disagree, bias is NONE (neutral) — preventing false signals.
    /// </summary>
    private ScanBias DetermineBias(
        string rsiBias, string emaVwapBias, IndicatorValues indicators, decimal lastClose)
    {
        bool emasBullish = indicators.EMAFast > indicators.EMASlow;
        bool vwapAvailable = indicators.VWAP > 0;

        // ✅ FIXED: When VWAP unavailable, rely on EMA alignment alone
        string priceStructureBias;
        if (!vwapAvailable)
        {
            // VWAP not available — fall back to EMA-only bias
            priceStructureBias = emasBullish ? "BULLISH" : "BEARISH";
        }
        else
        {
            bool priceAboveVwap = lastClose > indicators.VWAP;
            if (emasBullish && priceAboveVwap)
                priceStructureBias = "BULLISH";
            else if (!emasBullish && !priceAboveVwap)
                priceStructureBias = "BEARISH";
            else
                priceStructureBias = "NONE"; // EMAs and VWAP disagree
        }

        if (priceStructureBias == "NONE")
            return ScanBias.NONE;

        var bullishVotes = 0;
        var bearishVotes = 0;

        if (rsiBias == "BULLISH") bullishVotes++;
        if (rsiBias == "BEARISH") bearishVotes++;
        if (emaVwapBias == "BULLISH") bullishVotes++;
        if (emaVwapBias == "BEARISH") bearishVotes++;

        if (indicators.MacdLine > 0 && indicators.MacdHistogram > 0) bullishVotes++;
        else if (indicators.MacdLine > 0 && indicators.MacdHistogram < 0) { /* weakening — no vote */ }
        else if (indicators.MacdLine < 0 && indicators.MacdHistogram < 0) bearishVotes++;
        else if (indicators.MacdLine < 0 && indicators.MacdHistogram > 0) { /* recovering — no vote */ }

        if (indicators.PlusDI > indicators.MinusDI) bullishVotes++;
        if (indicators.MinusDI > indicators.PlusDI) bearishVotes++;

        if (bullishVotes > bearishVotes && priceStructureBias == "BULLISH")
            return ScanBias.BULLISH;
        if (bearishVotes > bullishVotes && priceStructureBias == "BEARISH")
            return ScanBias.BEARISH;

        return ScanBias.NONE;
    }

    /// <summary>
    /// ✅ FIXED: PULLBACK_READY now requires ADX >= 25 (confirmed trend)
    /// and price vs VWAP alignment. Added TREND_FORMING state for ADX 20-25.
    /// </summary>
    private ScanMarketState DetermineMarketState(
        IndicatorValues indicators, List<Candle> candles, ScanBias bias, decimal lastClose)
    {
        if (indicators.ADX < 20)
            return ScanMarketState.SIDEWAYS;

        var bandwidth = indicators.BollingerUpper > 0
            ? (indicators.BollingerUpper - indicators.BollingerLower) / indicators.BollingerMiddle
            : 0;

        if (bandwidth > 0.06m)
            return ScanMarketState.OVEREXTENDED;

        var isPullbackRsi = indicators.RSI >= 42 && indicators.RSI <= 58;

        if (isPullbackRsi && indicators.ADX >= 25)
        {
            // ✅ FIXED: VWAP check skipped when VWAP unavailable
            bool vwapConfirmed = indicators.VWAP <= 0
                || (bias == ScanBias.BULLISH && lastClose > indicators.VWAP)
                || (bias == ScanBias.BEARISH && lastClose < indicators.VWAP);

            if (vwapConfirmed)
                return ScanMarketState.PULLBACK_READY;
        }

        if (bias == ScanBias.BULLISH && indicators.ADX > 20)
            return ScanMarketState.TRENDING_BULLISH;

        if (bias == ScanBias.BEARISH && indicators.ADX > 20)
            return ScanMarketState.TRENDING_BEARISH;

        return ScanMarketState.SIDEWAYS;
    }
}
