using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Detects current market regime using multiple indicators
/// </summary>
public class MarketRegimeService
{
    private readonly IMarketSentimentService _sentimentService;
    private readonly ICandleService _candleService;
    private readonly ILogger<MarketRegimeService> _logger;

    // Regime detection thresholds
    private const float BULL_TREND_THRESHOLD = 15f;
    private const float BEAR_TREND_THRESHOLD = -15f;
    private const float HIGH_VOLATILITY_THRESHOLD = 25f;
    private const float LOW_LIQUIDITY_THRESHOLD = 0.7f;
    private const float SIDEWAYS_THRESHOLD = 10f;

    public MarketRegimeService(
        IMarketSentimentService sentimentService,
        ICandleService candleService,
        ILogger<MarketRegimeService> logger)
    {
        _sentimentService = sentimentService;
        _candleService = candleService;
        _logger = logger;
    }

    /// <summary>
    /// Detect current market regime
    /// </summary>
    public async Task<MarketRegimeDetection> DetectRegimeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Detecting market regime...");

        var marketContext = await _sentimentService.GetCurrentMarketContextAsync(cancellationToken);
        
        // Calculate regime scores
        var regimeScores = new Dictionary<MarketRegimeType, float>();
        
        regimeScores[MarketRegimeType.BULL_MARKET] = CalculateBullScore(marketContext);
        regimeScores[MarketRegimeType.BEAR_MARKET] = CalculateBearScore(marketContext);
        regimeScores[MarketRegimeType.SIDEWAYS_MARKET] = CalculateSidewaysScore(marketContext);
        regimeScores[MarketRegimeType.HIGH_VOLATILITY_MARKET] = CalculateHighVolatilityScore(marketContext);
        regimeScores[MarketRegimeType.LOW_LIQUIDITY_MARKET] = CalculateLowLiquidityScore(marketContext);
        
        // Determine primary regime
        var primaryRegime = regimeScores.OrderByDescending(kv => kv.Value).First();
        
        var detection = new MarketRegimeDetection
        {
            Regime = primaryRegime.Key,
            Confidence = primaryRegime.Value / 100f,
            DetectedAt = DateTimeOffset.UtcNow,
            TrendStrength = CalculateTrendStrength(marketContext),
            VolatilityLevel = (float)marketContext.VolatilityIndex,
            LiquidityLevel = CalculateLiquidityLevel(marketContext),
            MarketBreadth = (float)marketContext.MarketBreadth,
            SectorCorrelation = CalculateSectorCorrelation(marketContext),
            RegimeScores = regimeScores,
            KeyIndicators = IdentifyKeyIndicators(marketContext, primaryRegime.Key),
            Guidance = GenerateStrategyGuidance(primaryRegime.Key, primaryRegime.Value)
        };

        _logger.LogInformation("Detected regime: {Regime} with {Confidence:P0} confidence",
            detection.Regime, detection.Confidence);

        return detection;
    }

    private float CalculateBullScore(MarketContext context)
    {
        var score = 0f;
        
        // Sentiment bullish
        if (context.Sentiment == SentimentType.BULLISH) score += 40f;
        
        // Strong uptrend
        score += Math.Clamp((float)context.SentimentScore * 30, -20, 30);
        
        // Good market breadth (>60% stocks advancing)
        if (context.MarketBreadth > 0.6m) score += 20f;
        
        // Low-moderate volatility
        if (context.VolatilityIndex < 20) score += 10f;
        
        return Math.Clamp(score, 0, 100);
    }

    private float CalculateBearScore(MarketContext context)
    {
        var score = 0f;
        
        // Sentiment bearish
        if (context.Sentiment == SentimentType.BEARISH) score += 40f;
        
        // Strong downtrend
        score += Math.Clamp((float)context.SentimentScore * -30, -20, 30);
        
        // Poor market breadth (<40% stocks advancing)
        if (context.MarketBreadth < 0.4m) score += 20f;
        
        // High volatility
        if (context.VolatilityIndex > 25) score += 10f;
        
        return Math.Clamp(score, 0, 100);
    }

    private float CalculateSidewaysScore(MarketContext context)
    {
        var score = 0f;
        
        // Neutral sentiment
        if (context.Sentiment == SentimentType.NEUTRAL) score += 40f;
        
        // Low trend strength (sentiment score near 0)
        var trendStrength = Math.Abs((float)context.SentimentScore);
        score += Math.Clamp((1 - trendStrength) * 30, 0, 30);
        
        // Balanced market breadth
        var breadthBalance = 1 - Math.Abs((float)context.MarketBreadth - 0.5f) * 2;
        score += breadthBalance * 20f;
        
        // Normal volatility
        if (context.VolatilityIndex >= 15 && context.VolatilityIndex <= 25) score += 10f;
        
        return Math.Clamp(score, 0, 100);
    }

    private float CalculateHighVolatilityScore(MarketContext context)
    {
        var score = 0f;
        
        // VIX level
        if (context.VolatilityIndex > (decimal)HIGH_VOLATILITY_THRESHOLD)
            score += Math.Clamp((float)(context.VolatilityIndex - 20) * 3, 0, 60);
        
        // Market uncertainty
        if (context.Sentiment == SentimentType.NEUTRAL && context.VolatilityIndex > 20)
            score += 20f;
        
        // Wide price swings implied by breadth
        var breadthVolatility = Math.Abs((float)context.MarketBreadth - 0.5f) * 2;
        score += breadthVolatility * 20f;
        
        return Math.Clamp(score, 0, 100);
    }

    private float CalculateLowLiquidityScore(MarketContext context)
    {
        var score = 0f;
        
        // This would require actual volume data
        // Placeholder implementation
        if (context.MarketBreadth < 0.3m || context.MarketBreadth > 0.7m)
            score += 30f;  // Extreme breadth suggests low participation
        
        if (context.VolatilityIndex < 12)
            score += 20f;  // Very low volatility can mean low liquidity
        
        return Math.Clamp(score, 0, 100);
    }

    private float CalculateTrendStrength(MarketContext context)
    {
        return Math.Abs((float)context.SentimentScore) * 100;
    }

    private float CalculateLiquidityLevel(MarketContext context)
    {
        // Inverse of volatility as proxy
        return Math.Clamp(100 - (float)context.VolatilityIndex * 3, 0, 100);
    }

    private float CalculateSectorCorrelation(MarketContext context)
    {
        // Would calculate from actual sector data
        // Higher breadth = lower correlation (more dispersed)
        return 1 - Math.Abs((float)context.MarketBreadth - 0.5f) * 2;
    }

    private List<string> IdentifyKeyIndicators(MarketContext context, MarketRegimeType regime)
    {
        var indicators = new List<string>();
        
        indicators.Add($"Market Sentiment: {context.Sentiment}");
        indicators.Add($"Volatility Index: {context.VolatilityIndex:F1}");
        indicators.Add($"Market Breadth: {context.MarketBreadth:P0}");
        indicators.Add($"Sentiment Score: {context.SentimentScore:F2}");
        
        return indicators;
    }

    private RegimeStrategyGuidance GenerateStrategyGuidance(MarketRegimeType regime, float score)
    {
        return regime switch
        {
            MarketRegimeType.BULL_MARKET => new RegimeStrategyGuidance
            {
                PreferredStrategies = "Momentum, Breakout, Trend Following",
                RecommendedExposure = 0.8f,
                RecommendedLeverage = 1.5f,
                RiskMultiplier = 0.9f,
                AvoidStrategies = new List<string> { "Mean Reversion", "Short Selling" },
                TradingTips = new List<string>
                {
                    "Increase long exposure",
                    "Focus on momentum stocks",
                    "Ride the trend with trailing stops",
                    "Overweight growth sectors"
                }
            },
            
            MarketRegimeType.BEAR_MARKET => new RegimeStrategyGuidance
            {
                PreferredStrategies = "Defensive, Put Options, Short Selling",
                RecommendedExposure = 0.3f,
                RecommendedLeverage = 0.8f,
                RiskMultiplier = 1.5f,
                AvoidStrategies = new List<string> { "Momentum Long", "Breakout Long" },
                TradingTips = new List<string>
                {
                    "Reduce position sizes",
                    "Increase cash allocation",
                    "Consider defensive sectors",
                    "Use tight stop losses",
                    "Short-term trades only"
                }
            },
            
            MarketRegimeType.SIDEWAYS_MARKET => new RegimeStrategyGuidance
            {
                PreferredStrategies = "Mean Reversion, Range Trading, Iron Condors",
                RecommendedExposure = 0.5f,
                RecommendedLeverage = 1.0f,
                RiskMultiplier = 1.0f,
                AvoidStrategies = new List<string> { "Long-term Breakouts" },
                TradingTips = new List<string>
                {
                    "Trade the range",
                    "Fade extremes",
                    "Use support/resistance",
                    "Quick profit targets",
                    "Neutral options strategies"
                }
            },
            
            MarketRegimeType.HIGH_VOLATILITY_MARKET => new RegimeStrategyGuidance
            {
                PreferredStrategies = "Options Selling, Volatility Arbitrage",
                RecommendedExposure = 0.4f,
                RecommendedLeverage = 0.7f,
                RiskMultiplier = 2.0f,
                AvoidStrategies = new List<string> { "High Leverage", "Tight Stops" },
                TradingTips = new List<string>
                {
                    "Reduce leverage significantly",
                    "Wider stop losses",
                    "Smaller position sizes",
                    "Short volatility strategies",
                    "Wait for calm periods"
                }
            },
            
            _ => new RegimeStrategyGuidance
            {
                PreferredStrategies = "Diversified, Low Risk",
                RecommendedExposure = 0.5f,
                RecommendedLeverage = 1.0f,
                RiskMultiplier = 1.0f,
                TradingTips = new List<string> { "Stay balanced", "Wait for clarity" }
            }
        };
    }
}