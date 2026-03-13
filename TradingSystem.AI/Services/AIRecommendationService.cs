using Microsoft.Extensions.Logging;
using TradingSystem.AI.Models;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Strategies;

namespace TradingSystem.AI.Services;

/// <summary>
/// Service for generating AI-enhanced trade recommendations
/// </summary>
public class AIRecommendationService
{
    private readonly IInstrumentService _instrumentService;
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IMarketSentimentService _marketSentimentService;
    private readonly TradePredictionService _aiService;
    private readonly ILogger<AIRecommendationService> _logger;
    private readonly Dictionary<StrategyType, ITradingStrategy> _strategies;

    public AIRecommendationService(
        IInstrumentService instrumentService,
        ICandleService candleService,
        IIndicatorService indicatorService,
        IMarketSentimentService marketSentimentService,
        TradePredictionService aiService,
        ILogger<AIRecommendationService> logger)
    {
        _instrumentService = instrumentService;
        _candleService = candleService;
        _indicatorService = indicatorService;
        _marketSentimentService = marketSentimentService;
        _aiService = aiService;
        _logger = logger;

        // Register all strategies
        _strategies = new Dictionary<StrategyType, ITradingStrategy>
        {
            [StrategyType.MOMENTUM] = new MomentumStrategy(),
            [StrategyType.BREAKOUT] = new BreakoutStrategy(),
            [StrategyType.MEAN_REVERSION] = new MeanReversionStrategy(),
            [StrategyType.SWING_TRADING] = new SwingTradingStrategy()
        };
    }

    /// <summary>
    /// Generate AI-ranked trade recommendations
    /// </summary>
    public async Task<List<AITradeRecommendation>> GenerateAIRecommendationsAsync(
        int topCount = 10,
        int minConfidence = 60,
        float minAIProbability = 0.5f,
        int timeframeMinutes = 15,
        List<StrategyType>? allowedStrategies = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Generating AI recommendations: TopCount={Count}, MinConfidence={Confidence}, MinAIProbability={Probability}",
            topCount, minConfidence, minAIProbability);

        if (!_aiService.IsModelTrained())
        {
            _logger.LogWarning("AI model not trained. Falling back to strategy-only recommendations.");
        }

        // Get market context
        var marketContext = await _marketSentimentService.GetCurrentMarketContextAsync(cancellationToken);

        // Determine strategies to use
        var strategiesToUse = allowedStrategies?.Any() == true
            ? allowedStrategies
            : _strategies.Keys.ToList();

        // Scan for opportunities
        var opportunities = await ScanForOpportunitiesAsync(
            strategiesToUse,
            timeframeMinutes,
            minConfidence,
            marketContext,
            cancellationToken);

        _logger.LogInformation("Found {Count} trading opportunities", opportunities.Count);

        // Enhance with AI predictions
        var aiRecommendations = new List<AITradeRecommendation>();

        foreach (var opp in opportunities)
        {
            try
            {
                var aiRec = await EnhanceWithAIPredictionAsync(opp, marketContext);

                // Filter by AI probability
                if (aiRec.SuccessProbability >= minAIProbability)
                {
                    aiRecommendations.Add(aiRec);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error enhancing recommendation for {Symbol}", opp.Instrument.Symbol);
            }
        }

        // Rank by composite score (AI + Strategy)
        var rankedRecommendations = aiRecommendations
            .OrderByDescending(r => r.CompositeScore)
            .ThenByDescending(r => r.SuccessProbability)
            .Take(topCount)
            .ToList();

        _logger.LogInformation(
            "Generated {Count} AI-enhanced recommendations (filtered from {Total})",
            rankedRecommendations.Count, aiRecommendations.Count);

        return rankedRecommendations;
    }

    private async Task<List<TradeOpportunity>> ScanForOpportunitiesAsync(
        List<StrategyType> strategies,
        int timeframeMinutes,
        int minConfidence,
        MarketContext marketContext,
        CancellationToken cancellationToken)
    {
        var opportunities = new List<TradeOpportunity>();
        var instruments = await _instrumentService.GetActiveAsync();

        // Filter to only stocks
        var tradableInstruments = instruments
            .Where(i => i.InstrumentType == InstrumentType.STOCK)
            .ToList();

        foreach (var instrument in tradableInstruments)
        {
            try
            {
                var candles = await _candleService.GetRecentCandlesAsync(
                    instrument.Id,
                    timeframeMinutes,
                    daysBack: 30);

                if (!candles.Any())
                    continue;

                var latestIndicator = await _indicatorService.GetLatestAsync(
                    instrument.Id,
                    timeframeMinutes);

                if (latestIndicator == null)
                    continue;

                var indicators = MapToIndicatorValues(latestIndicator);

                foreach (var strategyType in strategies)
                {
                    if (!_strategies.TryGetValue(strategyType, out var strategy))
                        continue;

                    if (!strategy.IsInstrumentSuitable(instrument, candles))
                        continue;

                    var signal = strategy.Evaluate(instrument, candles, indicators, marketContext);

                    if (signal.IsValid && signal.Confidence >= minConfidence)
                    {
                        opportunities.Add(new TradeOpportunity
                        {
                            Instrument = instrument,
                            Strategy = strategyType,
                            Signal = signal,
                            Candles = candles,
                            Indicators = indicators,
                            Sector = instrument.Sector?.Name ?? "Unknown"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error scanning instrument {Symbol}", instrument.Symbol);
            }
        }

        return opportunities;
    }

    private async Task<AITradeRecommendation> EnhanceWithAIPredictionAsync(
        TradeOpportunity opportunity,
        MarketContext marketContext)
    {
        var instrument = opportunity.Instrument;
        var signal = opportunity.Signal;

        // Extract features for AI model
        var features = _aiService.ExtractFeatures(
            instrument,
            opportunity.Candles,
            opportunity.Indicators,
            signal,
            marketContext);

        // Get AI prediction
        var prediction = _aiService.PredictTradeSuccess(features);
        var successProbability = prediction?.Probability ?? 0.5f;
        var aiScore = prediction?.Score ?? 0f;

        // Calculate composite score (60% AI, 40% Strategy)
        var compositeScore = (decimal)(successProbability * 60) + (signal.Score * 0.4m);

        // Determine prediction confidence
        var predictionConfidence = successProbability switch
        {
            >= 0.75f => "HIGH",
            >= 0.60f => "MEDIUM",
            _ => "LOW"
        };

        // Get top contributing features
        var featureImportance = _aiService.GetFeatureImportance();
        var topFeatures = featureImportance
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Assess risk level
        var riskLevel = AssessRiskLevel(signal, marketContext, successProbability);
        var (riskFactors, opportunityFactors) = IdentifyFactors(features, signal, marketContext);

        // Determine market condition
        var marketCondition = DetermineMarketCondition(marketContext);

        var recommendation = new AITradeRecommendation
        {
            InstrumentId = instrument.Id,
            Symbol = instrument.Symbol,
            InstrumentName = instrument.Name,
            Exchange = instrument.Exchange,
            Sector = opportunity.Sector,
            Direction = signal.Direction,
            EntryPrice = signal.EntryPrice,
            StopLoss = signal.StopLoss,
            Target = signal.Target,
            RiskRewardRatio = CalculateRiskReward(signal),
            SuccessProbability = successProbability,
            AIScore = aiScore,
            PredictionConfidence = predictionConfidence,
            StrategyScore = signal.Score,
            StrategyConfidence = signal.Confidence,
            CompositeScore = compositeScore,
            TopFeatures = topFeatures,
            Strategy = opportunity.Strategy.ToString(),
            Signals = signal.Signals,
            Explanation = signal.Explanation,
            MarketSentiment = marketContext.SentimentScore,
            MarketCondition = marketCondition,
            RiskLevel = riskLevel,
            RiskFactors = riskFactors,
            OpportunityFactors = opportunityFactors
        };

        return recommendation;
    }

    private string AssessRiskLevel(StrategySignal signal, MarketContext marketContext, float aiProbability)
    {
        var riskScore = 0;

        // AI probability
        if (aiProbability < 0.55f) riskScore += 3;
        else if (aiProbability < 0.65f) riskScore += 1;

        // Market volatility
        if (marketContext.VolatilityIndex > 25) riskScore += 2;
        else if (marketContext.VolatilityIndex > 20) riskScore += 1;

        // Risk-reward ratio
        var rrRatio = CalculateRiskReward(signal);
        if (rrRatio < 1.5m) riskScore += 2;
        else if (rrRatio < 2.0m) riskScore += 1;

        // Market sentiment alignment
        if ((signal.Direction == "BUY" && marketContext.Sentiment == SentimentType.BEARISH) ||
            (signal.Direction == "SELL" && marketContext.Sentiment == SentimentType.BULLISH))
        {
            riskScore += 2;
        }

        return riskScore switch
        {
            >= 6 => "HIGH",
            >= 3 => "MEDIUM",
            _ => "LOW"
        };
    }

    private (List<string> risks, List<string> opportunities) IdentifyFactors(
        TradeFeatures features,
        StrategySignal signal,
        MarketContext marketContext)
    {
        var risks = new List<string>();
        var opportunities = new List<string>();

        // RSI analysis
        if (features.RSI > 70)
            risks.Add($"Overbought condition (RSI: {features.RSI:F1})");
        else if (features.RSI < 30)
            opportunities.Add($"Oversold bounce potential (RSI: {features.RSI:F1})");

        // MACD
        if (features.MACDHistogram > 0 && signal.Direction == "BUY")
            opportunities.Add("MACD bullish crossover");
        else if (features.MACDHistogram < 0 && signal.Direction == "SELL")
            opportunities.Add("MACD bearish crossover");

        // Volume
        if (features.VolumeRatio > 1.5f)
            opportunities.Add($"Above-average volume ({features.VolumeRatio:F1}x)");
        else if (features.VolumeRatio < 0.7f)
            risks.Add("Below-average volume");

        // Market sentiment
        if (marketContext.Sentiment == SentimentType.BULLISH && signal.Direction == "BUY")
            opportunities.Add("Aligned with bullish market sentiment");
        else if (marketContext.Sentiment == SentimentType.BEARISH && signal.Direction == "SELL")
            opportunities.Add("Aligned with bearish market sentiment");
        else if (marketContext.Sentiment != SentimentType.NEUTRAL)
            risks.Add("Counter-trend trade");

        // Volatility
        if (marketContext.VolatilityIndex > 25)
            risks.Add($"High market volatility (VIX: {marketContext.VolatilityIndex:F1})");

        // ADX
        if (features.ADX > 25)
            opportunities.Add($"Strong trend strength (ADX: {features.ADX:F1})");
        else if (features.ADX < 20)
            risks.Add("Weak trend, choppy conditions");

        // Risk-reward
        if (features.RiskRewardRatio >= 2.5f)
            opportunities.Add($"Excellent risk-reward ratio ({features.RiskRewardRatio:F1}:1)");
        else if (features.RiskRewardRatio < 1.5f)
            risks.Add($"Low risk-reward ratio ({features.RiskRewardRatio:F1}:1)");

        return (risks, opportunities);
    }

    private string DetermineMarketCondition(MarketContext marketContext)
    {
        if (marketContext.Sentiment == SentimentType.BULLISH && marketContext.VolatilityIndex < 15)
            return "STRONG_BULLISH";
        if (marketContext.Sentiment == SentimentType.BULLISH)
            return "BULLISH";
        if (marketContext.Sentiment == SentimentType.BEARISH && marketContext.VolatilityIndex > 25)
            return "STRONG_BEARISH";
        if (marketContext.Sentiment == SentimentType.BEARISH)
            return "BEARISH";
        if (marketContext.VolatilityIndex > 25)
            return "VOLATILE_NEUTRAL";
        return "NEUTRAL";
    }

    private decimal CalculateRiskReward(StrategySignal signal)
    {
        var risk = Math.Abs(signal.EntryPrice - signal.StopLoss);
        var reward = Math.Abs(signal.Target - signal.EntryPrice);
        return risk > 0 ? reward / risk : 0;
    }

    private IndicatorValues MapToIndicatorValues(IndicatorSnapshot snapshot)
    {
        return new IndicatorValues
        {
            EMAFast = snapshot.EMAFast,
            EMASlow = snapshot.EMASlow,
            RSI = snapshot.RSI,
            MacdLine = snapshot.MacdLine,
            MacdSignal = snapshot.MacdSignal,
            MacdHistogram = snapshot.MacdHistogram,
            ADX = snapshot.ADX,
            PlusDI = snapshot.PlusDI,
            MinusDI = snapshot.MinusDI,
            ATR = snapshot.ATR,
            BollingerUpper = snapshot.BollingerUpper,
            BollingerMiddle = snapshot.BollingerMiddle,
            BollingerLower = snapshot.BollingerLower,
            BollingerWidth = snapshot.BollingerUpper - snapshot.BollingerLower,
            VWAP = snapshot.VWAP
        };
    }

    private class TradeOpportunity
    {
        public TradingInstrument Instrument { get; set; } = null!;
        public StrategyType Strategy { get; set; }
        public StrategySignal Signal { get; set; } = null!;
        public List<Candle> Candles { get; set; } = null!;
        public IndicatorValues Indicators { get; set; } = null!;
        public string Sector { get; set; } = string.Empty;
    }
}