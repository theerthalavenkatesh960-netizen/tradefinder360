using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Reinforcement learning for dynamic factor weight adjustment
/// </summary>
public class ReinforcementLearningService
{
    private readonly ITradeOutcomeRepository _outcomeRepository;
    private readonly TradingDbContext _context;
    private readonly ILogger<ReinforcementLearningService> _logger;

    // Current factor weights (will be dynamically adjusted)
    private Dictionary<string, float> _factorWeights = new()
    {
        ["Momentum"] = 0.25f,
        ["Trend"] = 0.25f,
        ["Volatility"] = 0.10f,
        ["Liquidity"] = 0.15f,
        ["RelativeStrength"] = 0.15f,
        ["Sentiment"] = 0.05f,
        ["Risk"] = 0.05f
    };

    public ReinforcementLearningService(
        ITradeOutcomeRepository outcomeRepository,
        TradingDbContext context,
        ILogger<ReinforcementLearningService> logger)
    {
        _outcomeRepository = outcomeRepository;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Analyze trade outcomes and adjust factor weights
    /// </summary>
    public async Task<FactorWeightAdjustment> OptimizeFactorWeightsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting reinforcement learning factor optimization...");

        // Get recent trade outcomes (last 30 days)
        var startDate = DateTimeOffset.UtcNow.AddDays(-30);
        var outcomes = await _outcomeRepository.GetClosedTradesAsync(
            startDate, cancellationToken: cancellationToken);

        if (outcomes.Count < 20)
        {
            _logger.LogWarning("Insufficient trade outcomes for optimization: {Count}", outcomes.Count);
            return new FactorWeightAdjustment
            {
                Success = false,
                Message = "Insufficient data for optimization"
            };
        }

        // Analyze performance by dominant factor
        var factorPerformance = AnalyzeFactorPerformance(outcomes);

        // Calculate new weights using reinforcement learning
        var newWeights = CalculateOptimalWeights(factorPerformance);

        // Track performance
        var tracking = await SavePerformanceTrackingAsync(
            factorPerformance, 
            newWeights, 
            startDate, 
            cancellationToken);

        // Update current weights
        var oldWeights = new Dictionary<string, float>(_factorWeights);
        _factorWeights = newWeights;

        var adjustment = new FactorWeightAdjustment
        {
            Success = true,
            OldWeights = oldWeights,
            NewWeights = newWeights,
            PerformanceMetrics = factorPerformance,
            AnalyzedTrades = outcomes.Count,
            Message = "Factor weights optimized successfully"
        };

        _logger.LogInformation(
            "Factor optimization complete: Analyzed {Count} trades, Adjusted {Adjusted} weights",
            outcomes.Count, newWeights.Count(kv => Math.Abs(kv.Value - oldWeights[kv.Key]) > 0.01f));

        return adjustment;
    }

    /// <summary>
    /// Get current factor weights
    /// </summary>
    public Dictionary<string, float> GetCurrentWeights() => new(_factorWeights);

    /// <summary>
    /// Analyze which factors contributed to successful trades
    /// </summary>
    private Dictionary<string, FactorMetrics> AnalyzeFactorPerformance(
        List<TradeOutcome> outcomes)
    {
        var performance = new Dictionary<string, FactorMetrics>();
        var factorNames = new[] { "Momentum", "Trend", "Volatility", "Liquidity", 
            "RelativeStrength", "Sentiment", "Risk" };

        foreach (var factor in factorNames)
        {
            performance[factor] = new FactorMetrics();
        }

        foreach (var outcome in outcomes)
        {
            if (string.IsNullOrEmpty(outcome.MetaFactorsJson)) continue;

            try
            {
                var metaFactors = JsonSerializer.Deserialize<Dictionary<string, float>>(
                    outcome.MetaFactorsJson) ?? new Dictionary<string, float>();

                // Identify dominant factor
                var dominantFactor = GetDominantFactor(metaFactors);

                if (performance.ContainsKey(dominantFactor))
                {
                    var metrics = performance[dominantFactor];
                    metrics.TradeCount++;

                    if (outcome.IsSuccessful == true)
                    {
                        metrics.SuccessCount++;
                        metrics.TotalReturn += outcome.ActualReturn.HasValue ? (float)outcome.ActualReturn.Value : 0f;
                    }
                    else
                    {
                        metrics.TotalReturn += outcome.ActualReturn.HasValue ? (float)outcome.ActualReturn.Value : 0f;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error analyzing outcome {Id}", outcome.Id);
            }
        }

        // Calculate metrics
        foreach (var metrics in performance.Values)
        {
            metrics.WinRate = metrics.TradeCount > 0 
                ? (float)metrics.SuccessCount / metrics.TradeCount * 100 
                : 0f;
            metrics.AvgReturn = metrics.TradeCount > 0 
                ? metrics.TotalReturn / metrics.TradeCount 
                : 0f;
        }

        return performance;
    }

    /// <summary>
    /// Calculate optimal weights using performance-based reinforcement
    /// </summary>
    private Dictionary<string, float> CalculateOptimalWeights(
        Dictionary<string, FactorMetrics> performance)
    {
        var newWeights = new Dictionary<string, float>();
        
        // Calculate total performance score
        var totalScore = performance.Values.Sum(m => CalculateFactorScore(m));

        if (totalScore == 0)
        {
            _logger.LogWarning("Zero total score. Returning equal weights.");
            return new Dictionary<string, float>(_factorWeights);
        }

        // Distribute weights based on performance
        foreach (var (factor, metrics) in performance)
        {
            var score = CalculateFactorScore(metrics);
            var baseWeight = score / totalScore;
            
            // Apply learning rate (0.3 = 30% adjustment, 70% keep old)
            var learningRate = 0.3f;
            var oldWeight = _factorWeights.GetValueOrDefault(factor, 0.14f);
            var adjustedWeight = oldWeight * (1 - learningRate) + baseWeight * learningRate;
            
            newWeights[factor] = Math.Clamp(adjustedWeight, 0.05f, 0.40f); // Min 5%, Max 40%
        }

        // Normalize to sum to 1.0
        var sum = newWeights.Values.Sum();
        if (sum > 0)
        {
            foreach (var key in newWeights.Keys.ToList())
            {
                newWeights[key] /= sum;
            }
        }

        return newWeights;
    }

    private float CalculateFactorScore(FactorMetrics metrics)
    {
        if (metrics.TradeCount == 0) return 0.1f; // Small default score

        // Score = WinRate * AvgReturn * sqrt(TradeCount)
        var winRateScore = metrics.WinRate / 100f;
        var returnScore = Math.Max(metrics.AvgReturn / 10f, -1f); // Cap at -1 to 1
        var volumeScore = (float)Math.Sqrt(Math.Min(metrics.TradeCount, 100));

        var score = winRateScore * Math.Max(returnScore, 0.1f) * volumeScore;
        return Math.Max(score, 0.1f); // Minimum score
    }

    private string GetDominantFactor(Dictionary<string, float> metaFactors)
    {
        var factors = new Dictionary<string, float>
        {
            ["Momentum"] = Math.Abs(metaFactors.GetValueOrDefault("MomentumMetaFactor", 0f)),
            ["Trend"] = Math.Abs(metaFactors.GetValueOrDefault("TrendMetaFactor", 0f)),
            ["Volatility"] = Math.Abs(metaFactors.GetValueOrDefault("VolatilityMetaFactor", 0f)),
            ["Liquidity"] = Math.Abs(metaFactors.GetValueOrDefault("LiquidityMetaFactor", 0f)),
            ["RelativeStrength"] = Math.Abs(metaFactors.GetValueOrDefault("RelativeStrengthMetaFactor", 0f)),
            ["Sentiment"] = Math.Abs(metaFactors.GetValueOrDefault("SentimentMetaFactor", 0f)),
            ["Risk"] = Math.Abs(metaFactors.GetValueOrDefault("RiskMetaFactor", 0f))
        };

        return factors.OrderByDescending(kv => kv.Value).First().Key;
    }

    private async Task<FactorPerformanceTracking> SavePerformanceTrackingAsync(
        Dictionary<string, FactorMetrics> performance,
        Dictionary<string, float> newWeights,
        DateTimeOffset startDate,
        CancellationToken cancellationToken)
    {
        var tracking = new FactorPerformanceTracking
        {
            PeriodStart = startDate,
            PeriodEnd = DateTimeOffset.UtcNow,
            
            // Current weights
            MomentumWeight = newWeights["Momentum"],
            TrendWeight = newWeights["Trend"],
            VolatilityWeight = newWeights["Volatility"],
            LiquidityWeight = newWeights["Liquidity"],
            RelativeStrengthWeight = newWeights["RelativeStrength"],
            SentimentWeight = newWeights["Sentiment"],
            RiskWeight = newWeights["Risk"],
            
            // Performance metrics
            MomentumWinRate = performance["Momentum"].WinRate,
            MomentumAvgReturn = performance["Momentum"].AvgReturn,
            MomentumTradeCount = performance["Momentum"].TradeCount,
            
            TrendWinRate = performance["Trend"].WinRate,
            TrendAvgReturn = performance["Trend"].AvgReturn,
            TrendTradeCount = performance["Trend"].TradeCount,
            
            SentimentWinRate = performance["Sentiment"].WinRate,
            SentimentAvgReturn = performance["Sentiment"].AvgReturn,
            SentimentTradeCount = performance["Sentiment"].TradeCount,
            
            TotalTrades = performance.Values.Sum(m => m.TradeCount),
            OverallWinRate = CalculateOverallWinRate(performance),
            
            RecommendedAdjustmentsJson = JsonSerializer.Serialize(newWeights)
        };

        _context.FactorPerformanceTracking.Add(tracking);
        await _context.SaveChangesAsync(cancellationToken);

        return tracking;
    }

    private float CalculateOverallWinRate(Dictionary<string, FactorMetrics> performance)
    {
        var totalTrades = performance.Values.Sum(m => m.TradeCount);
        var totalSuccess = performance.Values.Sum(m => m.SuccessCount);
        return totalTrades > 0 ? (float)totalSuccess / totalTrades * 100 : 0f;
    }
}

public class FactorMetrics
{
    public int TradeCount { get; set; }
    public int SuccessCount { get; set; }
    public float WinRate { get; set; }
    public float TotalReturn { get; set; }
    public float AvgReturn { get; set; }
}

public class FactorWeightAdjustment
{
    public bool Success { get; set; }
    public Dictionary<string, float> OldWeights { get; set; } = new();
    public Dictionary<string, float> NewWeights { get; set; } = new();
    public Dictionary<string, FactorMetrics> PerformanceMetrics { get; set; } = new();
    public int AnalyzedTrades { get; set; }
    public string Message { get; set; } = string.Empty;
}