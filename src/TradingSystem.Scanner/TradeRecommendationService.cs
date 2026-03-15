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

    public TradeRecommendationService(
        IInstrumentService instrumentService,
        ICandleService candleService,
        IIndicatorService indicatorService,
        IRecommendationService recommendationService,
        IMarketSentimentService marketSentimentService,
        SetupScoringService scorer)
    {
        _instrumentService = instrumentService;
        _candleService = candleService;
        _indicatorService = indicatorService;
        _recommendationService = recommendationService;
        _marketSentimentService = marketSentimentService;
        _scorer = scorer;
    }

    public async Task<Recommendation?> GenerateAsync(string instrumentKey, int timeframeMinutes = 15)
    {
        var instrument = await _instrumentService.GetByKeyAsync(instrumentKey);
        if (instrument == null || !instrument.IsActive) return null;

        var candles = await _candleService.GetRecentCandlesAsync(instrument.Id, timeframeMinutes);
        if (candles.Count < 50) return null;

        var latestIndicator = await _indicatorService.GetLatestAsync(instrument.Id, timeframeMinutes);
        if (latestIndicator == null) return null;

        var indicators = MapToIndicatorValues(latestIndicator);
        var scanResult = _scorer.Score(instrument, indicators, candles);

        if (scanResult.SetupScore < 50 || scanResult.Bias == ScanBias.NONE)
            return null;

        var recommendation = await BuildRecommendationWithSentiment(instrument, indicators, scanResult, candles);
        try 
        {
            await PersistAsync(recommendation);
        }
        catch (Exception ex)
        {
            // Log the error (not implemented here)
            Console.WriteLine($"Error saving recommendation: {ex.Message}");
        }
        return recommendation;
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
        
        // Get all active instruments
        var instruments = await _instrumentService.GetActiveAsync();
        
        if (!instruments.Any())
            return recommendations;

        // Scan each instrument
        foreach (var instrument in instruments)
        {
            try
            {
                // Get candles for the instrument
                var candles = await _candleService.GetRecentCandlesAsync(instrument.Id, timeframeMinutes);
                if (candles.Count < 50) 
                    continue;

                // Get latest indicators
                var latestIndicator = await _indicatorService.GetLatestAsync(instrument.Id, timeframeMinutes);
                if (latestIndicator == null) 
                    continue;

                var indicators = MapToIndicatorValues(latestIndicator);
                var scanResult = _scorer.Score(instrument, indicators, candles);

                // Filter based on setup score
                if (scanResult.SetupScore < 50 || scanResult.Bias == ScanBias.NONE)
                    continue;

                // Build recommendation
                var recommendation = await BuildRecommendationWithSentiment(instrument, indicators, scanResult, candles);

                // Apply user filters
                if (!MeetsUserCriteria(recommendation, targetReturnPercentage, riskTolerance, minRiskRewardRatio))
                    continue;

                // Save to database
                try
                {
                    await PersistAsync(recommendation);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving recommendation for {instrument.Symbol}: {ex.Message}");
                }

                recommendations.Add(recommendation);
            }
            catch (Exception ex)
            {
                // Log and continue with next instrument
                Console.WriteLine($"Error processing instrument {instrument.Symbol}: {ex.Message}");
            }
        }

        // Sort by confidence and risk-reward ratio
        return recommendations
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.RiskRewardRatio)
            .ToList();
    }

    private bool MeetsUserCriteria(
        Recommendation recommendation,
        decimal targetReturnPercentage,
        decimal riskTolerance,
        decimal minRiskRewardRatio)
    {
        // Check minimum risk-reward ratio
        if (recommendation.RiskRewardRatio < minRiskRewardRatio)
            return false;

        // Calculate expected return percentage
        var expectedReturn = recommendation.Direction == "BUY"
            ? ((recommendation.Target - recommendation.EntryPrice) / recommendation.EntryPrice) * 100
            : ((recommendation.EntryPrice - recommendation.Target) / recommendation.EntryPrice) * 100;

        // Check if expected return meets or exceeds target
        if (expectedReturn < targetReturnPercentage)
            return false;

        // Calculate risk percentage
        var risk = recommendation.Direction == "BUY"
            ? ((recommendation.EntryPrice - recommendation.StopLoss) / recommendation.EntryPrice) * 100
            : ((recommendation.StopLoss - recommendation.EntryPrice) / recommendation.EntryPrice) * 100;

        // Check if risk is within tolerance
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
        var target = isBullish ? entry + (atr * 2.0m) : entry - (atr * 2.0m);
        var rrr = Math.Abs(target - entry) / Math.Abs(entry - stopLoss);

        var direction = isBullish ? "BUY" : "SELL";
        var optionType = instrument.IsDerivativesEnabled
            ? (isBullish ? "CALL" : "PUT")
            : null;

        var optionStrike = instrument.IsDerivativesEnabled
            ? RoundToStrike(lastClose, instrument.TickSize)
            : (decimal?)null;

        var reasons = BuildReasoningPoints(scan, indicators, isBullish);
        var explanation = BuildExplanation(instrument, scan, indicators, direction, entry, stopLoss, target, rrr);

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
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60)
        };
    }

    private async Task<Recommendation> BuildRecommendationWithSentiment(
        TradingInstrument instrument,
        IndicatorValues indicators,
        ScanResult scanResult,
        List<Candle> candles)
    {
        var recommendation = BuildRecommendation(instrument, indicators, scanResult, candles);
        
        // Adjust confidence based on market sentiment
        recommendation.Confidence = await AdjustConfidenceForMarketSentiment(recommendation.Confidence);
        
        // Add market sentiment to explanation
        try
        {
            var marketContext = await _marketSentimentService.GetCurrentMarketContextAsync();
            recommendation.ReasoningPoints.Add(
                $"Market Sentiment: {marketContext.Sentiment} (adjusted confidence)");
        }
        catch
        {
            // Ignore if market sentiment unavailable
        }
        
        return recommendation;
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
        catch
        {
            return baseConfidence; // Return original if sentiment unavailable
        }
    }

    private List<string> BuildReasoningPoints(ScanResult scan, IndicatorValues ind, bool bullish)
    {
        var points = new List<string>();

        if (ind.ADX > 25)
            points.Add($"ADX {ind.ADX:F1} confirms a strong trend environment");

        if (bullish)
        {
            if (ind.EMAFast > ind.EMASlow)
                points.Add($"EMA alignment is bullish (fast {ind.EMAFast:F1} > slow {ind.EMASlow:F1})");
            if (ind.RSI >= 45 && ind.RSI <= 60)
                points.Add($"RSI {ind.RSI:F1} is in pullback zone — healthy retracement for entry");
            if (ind.MacdLine > 0)
                points.Add("MACD is positive — upward momentum intact");
            if (ind.VWAP > 0 && ind.EMAFast > ind.VWAP)
                points.Add($"Price above VWAP {ind.VWAP:F1} — institutional bias is long");
        }
        else
        {
            if (ind.EMAFast < ind.EMASlow)
                points.Add($"EMA alignment is bearish (fast {ind.EMAFast:F1} < slow {ind.EMASlow:F1})");
            if (ind.RSI >= 40 && ind.RSI <= 55)
                points.Add($"RSI {ind.RSI:F1} is in bearish pullback zone — healthy retracement for short entry");
            if (ind.MacdLine < 0)
                points.Add("MACD is negative — downward momentum intact");
            if (ind.VWAP > 0 && ind.EMAFast < ind.VWAP)
                points.Add($"Price below VWAP {ind.VWAP:F1} — institutional bias is short");
        }

        points.AddRange(scan.Reasons.Take(3));
        return points;
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
