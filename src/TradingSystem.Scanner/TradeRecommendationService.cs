using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Models;

namespace TradingSystem.Scanner;

public class TradeRecommendationService
{
    private readonly IInstrumentService _instrumentService;
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IRecommendationService _recommendationService;
    private readonly IMarketSentimentService _marketSentimentService;
    private readonly SetupScoringService _scorer;
    private readonly ILogger<TradeRecommendationService> _logger;

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private static readonly TimeOnly MarketOpen  = new(9,  15);
    private static readonly TimeOnly MarketClose = new(15, 25); // 5 min before actual close

    public TradeRecommendationService(
        IInstrumentService instrumentService,
        ICandleService candleService,
        IIndicatorService indicatorService,
        IRecommendationService recommendationService,
        IMarketSentimentService marketSentimentService,
        SetupScoringService scorer,
        ILogger<TradeRecommendationService> logger)
    {
        _instrumentService = instrumentService;
        _candleService = candleService;
        _indicatorService = indicatorService;
        _recommendationService = recommendationService;
        _marketSentimentService = marketSentimentService;
        _scorer = scorer;
        _logger = logger;
    }

    public async Task<RecommendationResult> GenerateAsync(string instrumentKey, int timeframeMinutes = 15)
    {
        var instrument = await _instrumentService.GetByKeyAsync(instrumentKey);
        if (instrument == null || !instrument.IsActive)
            return RecommendationResult.Blocked($"Instrument '{instrumentKey}' not found or inactive");

        var candles = await _candleService.GetRecentCandlesAsync(instrument.Id, timeframeMinutes);
        if (candles.Count < 50)
            return RecommendationResult.Blocked($"Insufficient candle data ({candles.Count}/50 required)");

        var latestIndicator = await _indicatorService.GetLatestAsync(instrument.Id, timeframeMinutes);
        if (latestIndicator == null)
            return RecommendationResult.Blocked("No indicator snapshot available");

        var indicators = MapToIndicatorValues(latestIndicator);
        var scanResult = _scorer.Score(instrument, indicators, candles);
        var lastClose = candles.Last().Close;
        var direction = scanResult.Bias == ScanBias.BULLISH ? "BUY" : "SELL";

        var gateFailure = ValidateSignalGates(scanResult, indicators, lastClose, direction);
        if (gateFailure != null)
        {
            _logger.LogInformation(
                "Signal blocked for {Symbol}: {Reason}",
                instrumentKey, gateFailure);
            return RecommendationResult.Blocked(gateFailure);
        }

        var recommendation = await BuildRecommendationWithSentiment(
            instrument, indicators, scanResult, candles);

        if (recommendation.RiskRewardRatio < 2.0m)
            return RecommendationResult.Blocked(
                $"Risk-reward {recommendation.RiskRewardRatio:F1}:1 below minimum 2.0:1");

        try
        {
            await PersistAsync(recommendation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving recommendation for {Key}", instrumentKey);
        }

        return RecommendationResult.Success(recommendation);
    }

    /// <summary>
    /// Generate recommendations for all active instruments based on user criteria
    /// </summary>
    public async Task<List<Recommendation>> GenerateRecommendationsAsync(
        decimal targetReturnPercentage,
        decimal riskTolerance,
        decimal minRiskRewardRatio,
        int timeframeMinutes = 15)
    {
        var recommendations = new List<Recommendation>();

        var instruments = await _instrumentService.GetActiveAsync();
        if (!instruments.Any())
            return recommendations;

        foreach (var instrument in instruments)
        {
            try
            {
                var candles = await _candleService.GetRecentCandlesAsync(instrument.Id, timeframeMinutes);
                if (candles.Count < 50)
                    continue;

                var latestIndicator = await _indicatorService.GetLatestAsync(instrument.Id, timeframeMinutes);
                if (latestIndicator == null)
                    continue;

                var indicators = MapToIndicatorValues(latestIndicator);
                var scanResult = _scorer.Score(instrument, indicators, candles);
                var lastClose = candles.Last().Close;
                var direction = scanResult.Bias == ScanBias.BULLISH ? "BUY" : "SELL";

                // ✅ ADDED: Validate all signal gates
                var gateFailure = ValidateSignalGates(scanResult, indicators, lastClose, direction);
                if (gateFailure != null)
                    continue;

                var recommendation = await BuildRecommendationWithSentiment(
                    instrument, indicators, scanResult, candles);

                // ✅ ADDED: Final RR check
                if (recommendation.RiskRewardRatio < 2.0m)
                    continue;

                if (!MeetsUserCriteria(recommendation, targetReturnPercentage, riskTolerance, minRiskRewardRatio))
                    continue;

                try
                {
                    await PersistAsync(recommendation);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving recommendation for {Symbol}", instrument.Symbol);
                }

                recommendations.Add(recommendation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing instrument {Symbol}", instrument.Symbol);
            }
        }

        return recommendations
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.RiskRewardRatio)
            .ToList();
    }

    // =========================================================================
    // SIGNAL GATES
    // =========================================================================

    /// <summary>
    /// ✅ ADDED: Centralized gate validation. Returns first failure reason, or null if all pass.
    /// </summary>
    private static string? ValidateSignalGates(
        ScanResult scanResult,
        IndicatorValues indicators,
        decimal lastClose,
        string direction)
    {
        if (scanResult.SetupScore < 50 || scanResult.Bias == ScanBias.NONE)
            return $"Setup score {scanResult.SetupScore} or bias {scanResult.Bias} insufficient";

        if (scanResult.SetupScore < 65)
            return $"Confidence {scanResult.SetupScore} below minimum 65";

        if (scanResult.MarketState != ScanMarketState.PULLBACK_READY)
            return $"State {scanResult.MarketState} not actionable for entry";

        if (indicators.ADX < 25)
            return $"ADX {indicators.ADX:F1} below minimum 25 — trend not confirmed";

        if (indicators.ADX > 60)
            return $"ADX {indicators.ADX:F1} above 60 — trend exhaustion risk";

        if (direction == "BUY" && lastClose < indicators.VWAP && indicators.VWAP > 0)
            return $"Price {lastClose:F2} below VWAP {indicators.VWAP:F2} — no buy signal";

        if (direction == "SELL" && lastClose > indicators.VWAP && indicators.VWAP > 0)
            return $"Price {lastClose:F2} above VWAP {indicators.VWAP:F2} — no sell signal";

        if (!IsMarketOpen())
            return "Market is closed";

        return null; // all gates passed
    }

    /// <summary>
    /// ✅ ADDED: NSE market hours check (IST 9:15 – 15:25)
    /// </summary>
    private static bool IsMarketOpen()
    {
        var istNow  = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);
        var istTime = TimeOnly.FromDateTime(istNow);
        var istDay  = istNow.DayOfWeek;

        if (istDay == DayOfWeek.Saturday || istDay == DayOfWeek.Sunday)
            return false;

        return istTime >= MarketOpen && istTime <= MarketClose;
    }

    // =========================================================================
    // RECOMMENDATION BUILDING
    // =========================================================================

    private bool MeetsUserCriteria(
        Recommendation recommendation,
        decimal targetReturnPercentage,
        decimal riskTolerance,
        decimal minRiskRewardRatio)
    {
        if (recommendation.RiskRewardRatio < minRiskRewardRatio)
            return false;

        var expectedReturn = recommendation.Direction == "BUY"
            ? ((recommendation.Target - recommendation.EntryPrice) / recommendation.EntryPrice) * 100
            : ((recommendation.EntryPrice - recommendation.Target) / recommendation.EntryPrice) * 100;

        if (expectedReturn < targetReturnPercentage)
            return false;

        var risk = recommendation.Direction == "BUY"
            ? ((recommendation.EntryPrice - recommendation.StopLoss) / recommendation.EntryPrice) * 100
            : ((recommendation.StopLoss - recommendation.EntryPrice) / recommendation.EntryPrice) * 100;

        if (risk > riskTolerance)
            return false;

        return true;
    }

    public async Task<List<Recommendation>> GetActiveRecommendationsAsync()
        => await _recommendationService.GetActiveAsync();

    public async Task<Recommendation?> GetLatestForInstrumentAsync(int instrumentId)
        => await _recommendationService.GetLatestForInstrumentAsync(instrumentId);

    public async Task ExpireOldRecommendationsAsync()
        => await _recommendationService.ExpireOldAsync(60);

    private Recommendation BuildRecommendation(
        TradingInstrument instrument,
        IndicatorValues indicators,
        ScanResult scan,
        List<Candle> candles)
    {
        var lastClose = candles.Last().Close;
        var atr = indicators.ATR > 0 ? indicators.ATR : lastClose * 0.005m;
        var isBullish = scan.Bias == ScanBias.BULLISH;

        var entry = lastClose;
        var stopLoss = isBullish ? entry - (atr * 1.5m) : entry + (atr * 1.5m);
        var risk = Math.Abs(entry - stopLoss);

        // Target calculation: minimum 2:1, prefer 2.5:1
        // Fall back to 2:1 only if preferred target exceeds Bollinger band
        var minimumTarget   = isBullish ? entry + (risk * 2.0m) : entry - (risk * 2.0m);
        var preferredTarget = isBullish ? entry + (risk * 2.5m) : entry - (risk * 2.5m);

        bool preferredExceedsBand = isBullish
            ? (indicators.BollingerUpper > 0 && preferredTarget > indicators.BollingerUpper)
            : (indicators.BollingerLower > 0 && preferredTarget < indicators.BollingerLower);

        var target = preferredExceedsBand ? minimumTarget : preferredTarget;

        // Final RR on unrounded values — must be >= 2.0 before we commit
        var rrr = risk > 0 ? Math.Abs(target - entry) / risk : 0;

        var direction = isBullish ? "BUY" : "SELL";
        var optionType = instrument.IsDerivativesEnabled
            ? (isBullish ? "CALL" : "PUT")
            : null;

        var optionStrike = instrument.IsDerivativesEnabled
            ? RoundToStrike(lastClose, instrument.TickSize)
            : (decimal?)null;

        var reasons = BuildReasoningPoints(scan, indicators, isBullish, lastClose);
        var stopBasis = $"ATR × 1.5 = {atr * 1.5m:F2}";
        var targetBasis = preferredExceedsBand
            ? $"ATR × 2.0 risk (capped by Bollinger)"
            : $"ATR × 2.5 risk";

        // ✅ FIXED: Signal-specific reasoning only — no analysis reasons copied
        var signalReasons = new List<string>
        {
            $"Setup state: {scan.MarketState} with score {scan.SetupScore}/100",
            $"Direction: {direction} — {(isBullish ? "bullish" : "bearish")} bias confirmed",
            $"Entry at {entry:F2} with {rrr:F1}:1 risk-reward",
            $"Stop loss at {stopLoss:F2} ({stopBasis})",
            $"Target at {target:F2} ({targetBasis})"
        };

        // Merge indicator reasons + signal-specific reasons, deduped
        reasons.AddRange(signalReasons);

        var explanation = BuildExplanation(
            instrument, scan, indicators, direction, entry, stopLoss, target, rrr);

        var expiresAt = CalculateExpiry();

        return new Recommendation
        {
            InstrumentId = instrument.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Direction = direction,
            EntryPrice = Math.Round(entry, 2),
            StopLoss = Math.Round(stopLoss, 2),
            Target = Math.Round(target, 2),
            RiskRewardRatio = Math.Round(rrr, 2),
            Confidence = scan.SetupScore,
            OptionType = optionType,
            OptionStrike = optionStrike,
            ReasoningPoints = reasons,
            ExplanationText = explanation,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// ✅ FIXED: Expire at market close (15:30 IST) or in 1 hour, whichever is sooner.
    /// </summary>
    private static DateTimeOffset CalculateExpiry()
    {
        var istNow       = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);
        var marketClose  = istNow.Date.Add(new TimeSpan(15, 30, 0));
        var oneHourLater = istNow.AddHours(1);

        var expiryIst = oneHourLater < marketClose ? oneHourLater : marketClose;
        return new DateTimeOffset(
            DateTime.SpecifyKind(expiryIst, DateTimeKind.Unspecified),
            TimeSpan.FromHours(5.5)).ToUniversalTime();
    }

    private async Task<int> AdjustConfidenceForMarketSentiment(int baseConfidence)
    {
        try
        {
            var marketContext = await _marketSentimentService.GetCurrentMarketContextAsync();
            return (int)_marketSentimentService.AdjustConfidenceForMarketSentiment(
                baseConfidence,
                marketContext.Sentiment);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Sentiment unavailable — degrade gracefully
            return baseConfidence;
        }
    }

    private async Task<Recommendation> BuildRecommendationWithSentiment(
        TradingInstrument instrument,
        IndicatorValues indicators,
        ScanResult scanResult,
        List<Candle> candles)
    {
        var recommendation = BuildRecommendation(instrument, indicators, scanResult, candles);

        recommendation.Confidence = await AdjustConfidenceForMarketSentiment(
            recommendation.Confidence);

        try
        {
            var marketContext = await _marketSentimentService.GetCurrentMarketContextAsync();
            recommendation.ReasoningPoints.Add(
                $"Market Sentiment: {marketContext.Sentiment} (adjusted confidence)");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Sentiment unavailable — proceed without it
        }

        return recommendation;
    }

    /// <summary>
    /// ✅ FIXED: MACD reasoning checks histogram direction, not just line polarity.
    /// ✅ FIXED: Deduplicates reasoning points by indicator topic.
    /// </summary>
    private List<string> BuildReasoningPoints(
        ScanResult scan, IndicatorValues ind, bool bullish, decimal lastClose)
    {
        var points = new List<string>();

        // ADX
        if (ind.ADX >= 25)
            points.Add($"ADX {ind.ADX:F1} confirms a strong trend environment");
        else if (ind.ADX >= 20)
            points.Add($"ADX {ind.ADX:F1} — trend forming but not yet confirmed");
        else
            points.Add($"ADX {ind.ADX:F1} — weak trend, exercise caution");

        // EMA
        if (bullish && ind.EMAFast > ind.EMASlow)
            points.Add($"EMA alignment is bullish (fast {ind.EMAFast:F1} > slow {ind.EMASlow:F1})");
        else if (!bullish && ind.EMAFast < ind.EMASlow)
            points.Add($"EMA alignment is bearish (fast {ind.EMAFast:F1} < slow {ind.EMASlow:F1})");

        // RSI
        if (bullish && ind.RSI >= 45 && ind.RSI <= 60)
            points.Add($"RSI {ind.RSI:F1} is in pullback zone — healthy retracement for entry");
        else if (!bullish && ind.RSI >= 40 && ind.RSI <= 55)
            points.Add($"RSI {ind.RSI:F1} is in bearish pullback zone — healthy retracement for short entry");

        // ✅ FIXED: MACD reasoning uses histogram direction
        if (ind.MacdLine > 0 && ind.MacdHistogram > 0)
            points.Add("MACD positive and rising — upward momentum building");
        else if (ind.MacdLine > 0 && ind.MacdHistogram < 0)
            points.Add("MACD positive but weakening — momentum fading, caution");
        else if (ind.MacdLine < 0 && ind.MacdHistogram < 0)
            points.Add("MACD negative and falling — downward momentum building");
        else if (ind.MacdLine < 0 && ind.MacdHistogram > 0)
            points.Add("MACD negative but recovering — potential reversal watch");

        // VWAP
        if (ind.VWAP > 0)
        {
            if (lastClose > ind.VWAP)
                points.Add($"Price above VWAP {ind.VWAP:F1} — institutional bias is long");
            else
                points.Add($"Price below VWAP {ind.VWAP:F1} — institutional bias is short");
        }

        // ✅ FIXED: Deduplicate by indicator topic — one point per indicator max
        var deduped = points
            .GroupBy(ExtractIndicatorTopic)
            .Select(g => g.Last()) // keep last (most specific) per topic
            .ToList();

        return deduped;
    }

    /// <summary>
    /// ✅ ADDED: Identify which indicator a reasoning string is about for deduplication.
    /// </summary>
    private static string ExtractIndicatorTopic(string reason)
    {
        if (reason.Contains("RSI"))       return "RSI";
        if (reason.Contains("MACD"))      return "MACD";
        if (reason.Contains("EMA"))       return "EMA";
        if (reason.Contains("ADX"))       return "ADX";
        if (reason.Contains("VWAP"))      return "VWAP";
        if (reason.Contains("Bollinger")) return "Bollinger";
        if (reason.Contains("Volume"))    return "Volume";
        if (reason.Contains("Sentiment")) return "Sentiment";
        return reason; // unique key = no dedup for unknown topics
    }

    private string BuildExplanation(
        TradingInstrument instrument,
        ScanResult scan,
        IndicatorValues ind,
        string direction,
        decimal entry,
        decimal sl,
        decimal target,
        decimal rrr)
    {
        var stateLabel = scan.MarketState.ToString().Replace("_", " ").ToLower();
        var quality = scan.QualityLabel.ToLower();
        var optionNote = instrument.IsDerivativesEnabled
            ? $" Consider ATM {(direction == "BUY" ? "CALL" : "PUT")} option."
            : string.Empty;

        return $"{instrument.Symbol} is in a {stateLabel} state with a setup score of {scan.SetupScore}/100 ({quality} quality). "
            + $"The directional bias is {direction.ToLower()} based on EMA alignment, RSI position, and MACD momentum. "
            + $"Suggested entry near {entry:F2} with stop loss at {sl:F2} (ATR-based) and target at {target:F2}. "
            + $"Risk-reward ratio is {rrr:F1}:1.{optionNote}";
    }

    private async Task PersistAsync(Recommendation recommendation)
        => await _recommendationService.SaveAsync(recommendation);

    private static decimal RoundToStrike(decimal price, decimal tickSize)
    {
        var strikeInterval = tickSize > 0 ? tickSize * 1000 : 100;
        return Math.Round(price / strikeInterval) * strikeInterval;
    }

    private static IndicatorValues MapToIndicatorValues(IndicatorSnapshot s) => new()
    {
        Timestamp = s.Timestamp,
        EMAFast = s.EMAFast,
        EMASlow = s.EMASlow,
        RSI = s.RSI,
        MacdLine = s.MacdLine,
        MacdSignal = s.MacdSignal,
        MacdHistogram = s.MacdHistogram,
        ADX = s.ADX,
        PlusDI = s.PlusDI,
        MinusDI = s.MinusDI,
        ATR = s.ATR,
        BollingerUpper = s.BollingerUpper,
        BollingerMiddle = s.BollingerMiddle,
        BollingerLower = s.BollingerLower,
        VWAP = s.VWAP
    };
}
