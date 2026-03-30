using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Models;

namespace TradingSystem.Api.Helpers;

/// <summary>
/// Computes all enriched analysis context (volume, structure, signal timing,
/// market regime, no-trade diagnostics) from existing candle + indicator data.
/// No DB writes or external API calls — pure computation.
/// </summary>
public static class AnalysisContextBuilder
{
    // ?? NoTradeContext ????????????????????????????????????????

    public static NoTradeContextDto BuildNoTradeContext(
        ScanResult? scan,
        IndicatorValues indicators,
        decimal lastClose,
        List<Candle> candles,
        int timeframeMinutes)
    {
        var (code, message) = DetermineNoTradeReason(scan, indicators, candles);

        var (triggerPrice, triggerCondition) = ComputeNextTrigger(
            scan, indicators, lastClose);

        return new NoTradeContextDto
        {
            WhyNoTradeCode = code,
            WhyNoTradeMessage = message,
            NextTriggerPrice = triggerPrice,
            NextTriggerCondition = triggerCondition,
            EstimatedRecheckMinutes = timeframeMinutes,
            InvalidatesAt = null
        };
    }

    private static (string Code, string Message) DetermineNoTradeReason(
        ScanResult? scan, IndicatorValues indicators, List<Candle> candles)
    {
        if (scan == null)
            return ("OTHER", "No scan data available for current market conditions");

        // Check trading window (IST 09:15–15:30)
        var ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var istNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ist);
        var marketOpen = new TimeOnly(9, 15);
        var marketClose = new TimeOnly(15, 30);
        var currentTime = TimeOnly.FromDateTime(istNow);
        if (currentTime < marketOpen || currentTime > marketClose
            || istNow.DayOfWeek == DayOfWeek.Saturday || istNow.DayOfWeek == DayOfWeek.Sunday)
            return ("OUTSIDE_TRADING_WINDOW",
                "Market is closed — trading hours are Mon–Fri 09:15–15:30 IST");

        if (scan.MarketState != ScanMarketState.PULLBACK_READY)
            return ("NOT_PULLBACK_READY",
                $"Market state is {scan.MarketState}, not PULLBACK_READY");

        if (indicators.ADX < 25)
            return ("WEAK_TREND",
                $"ADX {indicators.ADX:F1} below 25 — trend not confirmed");

        if (candles.Count >= 5)
        {
            var recent = candles.TakeLast(3).Average(c => (double)c.Volume);
            var baseline = candles.SkipLast(3).TakeLast(10).Average(c => (double)c.Volume);
            if (baseline > 0 && recent / baseline < 1.0)
                return ("LOW_VOLUME",
                    $"Recent volume {recent / baseline:F2}x below average — weak conviction");
        }

        if (indicators.ADX > 60)
            return ("RISK_TOO_HIGH",
                $"ADX {indicators.ADX:F1} above 60 — trend exhaustion risk");

        if (scan.Bias == ScanBias.NONE)
            return ("CONFLICTING_SIGNALS",
                "EMAs, VWAP and RSI disagree — no clear directional bias");

        if (scan.SetupScore < 50)
            return ("WEAK_TREND",
                $"Setup score {scan.SetupScore}/100 too low for a trade");

        return ("OTHER", "Market conditions not met for an entry signal");
    }

    private static (decimal? Price, string? Condition) ComputeNextTrigger(
        ScanResult? scan, IndicatorValues indicators, decimal lastClose)
    {
        if (scan == null) return (null, null);

        if (scan.Bias == ScanBias.BULLISH && indicators.EMAFast > 0)
            return (Math.Round(indicators.EMAFast, 2),
                "Price closes above EMA fast with rising MACD histogram");

        if (scan.Bias == ScanBias.BEARISH && indicators.EMAFast > 0)
            return (Math.Round(indicators.EMAFast, 2),
                "Price closes below EMA fast with falling MACD histogram");

        if (indicators.VWAP > 0)
            return (Math.Round(indicators.VWAP, 2),
                "Price crosses VWAP with ADX above 25");

        return (null, null);
    }

    // ?? VolumeContext ????????????????????????????????????????

    public static VolumeContextDto BuildVolumeContext(List<Candle> candles)
    {
        if (candles.Count == 0)
            return new VolumeContextDto();

        var currentVolume = candles.Last().Volume;
        var avg20 = candles.Count >= 20
            ? (long)candles.TakeLast(20).Average(c => c.Volume)
            : (long)candles.Average(c => c.Volume);

        var relVol = avg20 > 0 ? Math.Round((decimal)currentVolume / avg20, 2) : 0m;

        return new VolumeContextDto
        {
            CurrentVolume = currentVolume,
            Volume20Avg = avg20,
            RelativeVolume = relVol,
            DeliveryVolumeRatio = null, // Not available from candle data
            IsAboveAverage = currentVolume > avg20
        };
    }

    // ?? StructureLevels ??????????????????????????????????????

    public static StructureLevelsDto BuildStructureLevels(List<Candle> candles)
    {
        if (candles.Count < 2)
            return new StructureLevelsDto();

        // Current session = today's candles
        var todayDate = candles.Last().Timestamp.Date;
        var todayCandles = candles.Where(c => c.Timestamp.Date == todayDate).ToList();

        // Previous day = last full trading day before today
        var prevDayCandles = candles
            .Where(c => c.Timestamp.Date < todayDate)
            .GroupBy(c => c.Timestamp.Date)
            .OrderByDescending(g => g.Key)
            .FirstOrDefault()
            ?.ToList();

        decimal? sessionHigh = todayCandles.Count > 0 ? todayCandles.Max(c => c.High) : null;
        decimal? sessionLow = todayCandles.Count > 0 ? todayCandles.Min(c => c.Low) : null;

        decimal? prevHigh = prevDayCandles?.Max(c => c.High);
        decimal? prevLow = prevDayCandles?.Min(c => c.Low);
        decimal? prevClose = prevDayCandles?.Last().Close;

        // Classic pivot point calculation (from previous day OHLC)
        decimal? pivot = null, r1 = null, s1 = null;
        if (prevHigh.HasValue && prevLow.HasValue && prevClose.HasValue)
        {
            pivot = Math.Round((prevHigh.Value + prevLow.Value + prevClose.Value) / 3, 2);
            r1 = Math.Round(2 * pivot.Value - prevLow.Value, 2);
            s1 = Math.Round(2 * pivot.Value - prevHigh.Value, 2);
        }

        // Nearest support/resistance from recent swing highs/lows
        var lastClose = candles.Last().Close;
        var (support, resistance) = FindNearestLevels(candles, lastClose);

        return new StructureLevelsDto
        {
            SessionHigh = sessionHigh.HasValue ? Math.Round(sessionHigh.Value, 2) : null,
            SessionLow = sessionLow.HasValue ? Math.Round(sessionLow.Value, 2) : null,
            PreviousDayHigh = prevHigh.HasValue ? Math.Round(prevHigh.Value, 2) : null,
            PreviousDayLow = prevLow.HasValue ? Math.Round(prevLow.Value, 2) : null,
            NearestSupport = support,
            NearestResistance = resistance,
            Pivot = pivot,
            R1 = r1,
            S1 = s1
        };
    }

    private static (decimal? Support, decimal? Resistance) FindNearestLevels(
        List<Candle> candles, decimal lastClose)
    {
        if (candles.Count < 10) return (null, null);

        // Use recent swing lows as support, swing highs as resistance
        var recentCandles = candles.TakeLast(Math.Min(candles.Count, 50)).ToList();

        var swingLows = new List<decimal>();
        var swingHighs = new List<decimal>();

        for (int i = 2; i < recentCandles.Count - 2; i++)
        {
            var c = recentCandles[i];
            if (c.Low < recentCandles[i - 1].Low && c.Low < recentCandles[i - 2].Low &&
                c.Low < recentCandles[i + 1].Low && c.Low < recentCandles[i + 2].Low)
                swingLows.Add(c.Low);

            if (c.High > recentCandles[i - 1].High && c.High > recentCandles[i - 2].High &&
                c.High > recentCandles[i + 1].High && c.High > recentCandles[i + 2].High)
                swingHighs.Add(c.High);
        }

        var support = swingLows.Where(l => l < lastClose)
            .OrderByDescending(l => l).FirstOrDefault();
        var resistance = swingHighs.Where(h => h > lastClose)
            .OrderBy(h => h).FirstOrDefault();

        return (
            support > 0 ? Math.Round(support, 2) : null,
            resistance > 0 ? Math.Round(resistance, 2) : null
        );
    }

    // ?? SignalTiming ?????????????????????????????????????????

    public static SignalTimingDto BuildSignalTiming(List<IndicatorSnapshot> snapshots)
    {
        if (snapshots.Count < 2)
            return new SignalTimingDto { SignalFreshnessScore = 0 };

        var ordered = snapshots.OrderBy(s => s.Timestamp).ToList();

        int? barsSinceMacdCross = null;
        int? barsSinceRsiExit = null;

        // Walk backward from latest to find MACD zero-cross
        for (int i = ordered.Count - 2; i >= 0; i--)
        {
            if (barsSinceMacdCross == null)
            {
                var prevSign = Math.Sign(ordered[i].MacdHistogram);
                var currSign = Math.Sign(ordered[i + 1].MacdHistogram);
                if (prevSign != currSign && prevSign != 0)
                    barsSinceMacdCross = ordered.Count - 1 - i;
            }

            if (barsSinceRsiExit == null)
            {
                bool prevInZone = ordered[i].RSI < 30 || ordered[i].RSI > 70;
                bool currOutZone = ordered[i + 1].RSI >= 30 && ordered[i + 1].RSI <= 70;
                if (prevInZone && currOutZone)
                    barsSinceRsiExit = ordered.Count - 1 - i;
            }

            if (barsSinceMacdCross.HasValue && barsSinceRsiExit.HasValue)
                break;
        }

        // Signal age = bars since the latest of MACD cross or RSI zone exit
        int? signalAge = null;
        if (barsSinceMacdCross.HasValue || barsSinceRsiExit.HasValue)
            signalAge = Math.Min(
                barsSinceMacdCross ?? int.MaxValue,
                barsSinceRsiExit ?? int.MaxValue);

        // Freshness: 100 at 0 bars old, decays linearly to 0 at 20 bars
        var freshness = signalAge.HasValue
            ? Math.Max(0, 100 - (signalAge.Value * 5))
            : 0;

        return new SignalTimingDto
        {
            SignalAgeBars = signalAge,
            BarsSinceMacdCross = barsSinceMacdCross,
            BarsSinceRsiZoneExit = barsSinceRsiExit,
            SignalFreshnessScore = freshness
        };
    }

    // ?? MarketRegime ?????????????????????????????????????????

    public static MarketRegimeDto BuildMarketRegime(
        IndicatorValues indicators, List<Candle> candles)
    {
        // Volatility regime based on Bollinger bandwidth
        string volRegime = "NORMAL";
        if (indicators.BollingerMiddle > 0)
        {
            var bw = (indicators.BollingerUpper - indicators.BollingerLower) / indicators.BollingerMiddle;
            if (bw > 0.05m) volRegime = "HIGH";
            else if (bw < 0.02m) volRegime = "LOW";
        }

        // Trend strength: ADX mapped to 0-100
        var trendStrength = Math.Min(100, (int)(indicators.ADX * 2));

        // Range compression: inverse of Bollinger bandwidth, 0-100
        int rangeCompression = 50;
        if (indicators.BollingerMiddle > 0)
        {
            var bw = (indicators.BollingerUpper - indicators.BollingerLower) / indicators.BollingerMiddle;
            // 0.01 ? 100 (very compressed), 0.06+ ? 0 (very wide)
            rangeCompression = Math.Clamp((int)((0.06m - bw) / 0.06m * 100), 0, 100);
        }

        // Momentum quality: MACD histogram direction + RSI alignment
        int momentumQuality = 50;
        bool macdBullish = indicators.MacdHistogram > 0;
        bool rsiBullish = indicators.RSI > 50;
        bool macdRising = indicators.MacdHistogram > indicators.MacdSignal * 0.1m; // rough proxy

        if (macdBullish == rsiBullish)
            momentumQuality = 70; // aligned
        else
            momentumQuality = 30; // conflicting

        if (indicators.ADX > 25 && macdBullish == rsiBullish)
            momentumQuality = 85; // strong trend + aligned momentum

        return new MarketRegimeDto
        {
            VolatilityRegime = volRegime,
            TrendStrengthScore = trendStrength,
            RangeCompressionScore = rangeCompression,
            MomentumQualityScore = momentumQuality
        };
    }

    // ?? ConfidenceBreakdown (for EntryGuidance) ??????????????

    public static ConfidenceBreakdownDto BuildConfidenceBreakdown(
        ScanResult scan, IndicatorValues indicators, List<Candle> candles)
    {
        // Trend score: ADX-based
        int trend = indicators.ADX >= 30 ? 20 : indicators.ADX >= 20 ? 10 : 0;

        // Momentum: MACD + RSI alignment
        int momentum = 0;
        if ((indicators.MacdHistogram > 0 && indicators.RSI > 50) ||
            (indicators.MacdHistogram < 0 && indicators.RSI < 50))
            momentum = 20;
        else if (indicators.MacdHistogram != 0)
            momentum = 10;

        // Volume
        int volume = scan.ScoreBreakdown.VolumeScore > 0
            ? (int)(20.0 * scan.ScoreBreakdown.VolumeScore / 15.0) // normalize from weight
            : 0;
        volume = Math.Min(volume, 20);

        // Structure
        int structure = scan.ScoreBreakdown.StructureScore > 0
            ? (int)(20.0 * scan.ScoreBreakdown.StructureScore / 20.0)
            : 0;
        structure = Math.Min(structure, 20);

        // Volatility: Bollinger-based
        int volatility = scan.ScoreBreakdown.BollingerScore > 0
            ? (int)(20.0 * scan.ScoreBreakdown.BollingerScore / 10.0)
            : 0;
        volatility = Math.Min(volatility, 20);

        return new ConfidenceBreakdownDto
        {
            Trend = trend,
            Momentum = momentum,
            Volume = volume,
            Structure = structure,
            Volatility = volatility,
            Total = trend + momentum + volume + structure + volatility
        };
    }

    /// <summary>
    /// Estimate expected holding time based on ATR and timeframe.
    /// </summary>
    public static int? EstimateHoldingMinutes(
        decimal entryPrice, decimal target, decimal atr, int timeframeMinutes)
    {
        if (atr <= 0 || entryPrice <= 0) return null;

        var targetDistance = Math.Abs(target - entryPrice);
        var barsToTarget = targetDistance / atr;
        return (int)Math.Round(barsToTarget * timeframeMinutes);
    }

    /// <summary>
    /// Estimate max adverse/favorable excursion from recent ATR.
    /// </summary>
    public static (decimal? Mae, decimal? Mfe) EstimateExcursion(
        decimal entryPrice, decimal atr)
    {
        if (atr <= 0 || entryPrice <= 0) return (null, null);

        // MAE ? 1.5 × ATR as % of entry, MFE ? 2.5 × ATR as % of entry
        var mae = Math.Round(1.5m * atr / entryPrice * 100, 2);
        var mfe = Math.Round(2.5m * atr / entryPrice * 100, 2);
        return (mae, mfe);
    }
}
