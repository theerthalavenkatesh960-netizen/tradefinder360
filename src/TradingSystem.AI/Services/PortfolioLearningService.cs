using Microsoft.Extensions.Logging;
using System.Text.Json;
using TradingSystem.Core.Models;
using TradingSystem.Data;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Portfolio learning service that adapts fusion algorithm based on performance
/// Extends ReinforcementLearningService pattern for portfolio-specific use case
/// Implements rule-based tuning: analyzes performance metrics and decides what parameters to adjust
/// </summary>
public class PortfolioLearningService
{
    private readonly IFusionLearningConfigRepository _configRepository;
    private readonly TradingDbContext _dbContext;
    private readonly ILogger<PortfolioLearningService> _logger;

    // Tuning parameter bounds to prevent extreme settings
    private const decimal MinWeight = 0.20m;
    private const decimal MaxWeight = 0.70m;
    private const decimal MinFusionScore = 0.45m;
    private const decimal MaxFusionScore = 0.75m;
    private const decimal MinNewsBoundary = -0.50m;
    private const decimal MaxNewsBoundary = -0.20m;
    private const decimal MaxPositiveBoundary = 0.50m;
    private const decimal MinPositiveBoundary = 0.20m;

    public PortfolioLearningService(
        IFusionLearningConfigRepository configRepository,
        TradingDbContext dbContext,
        ILogger<PortfolioLearningService> logger)
    {
        _configRepository = configRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Evaluate if learning/adaptation is needed based on performance degradation
    /// </summary>
    public bool EvaluateLearningNeed(
        PortfolioPerformanceMetrics currentMetrics,
        decimal minWinRateThreshold = 50,
        decimal minSharpeThreshold = 0.5m,
        decimal maxDrawdownThreshold = 20,
        decimal maxVetoRejectionThreshold = 40)
    {
        var isDegraded = currentMetrics.WinRate < minWinRateThreshold
            || currentMetrics.SharpeRatio < minSharpeThreshold
            || currentMetrics.MaxDrawdown > maxDrawdownThreshold
            || currentMetrics.VetoRejectionRate > maxVetoRejectionThreshold;

        if (isDegraded)
        {
            _logger.LogInformation(
                "Learning need detected: WinRate={WinRate:F1}%, Sharpe={Sharpe:F2}, DD={DD:F1}%, VetoRate={Veto:F1}%",
                currentMetrics.WinRate, currentMetrics.SharpeRatio, currentMetrics.MaxDrawdown, currentMetrics.VetoRejectionRate);
        }

        return isDegraded;
    }

    /// <summary>
    /// Compute adaptive configuration based on performance metrics and signal correlations
    /// Returns new proposed FusionLearningConfig
    /// </summary>
    public async Task<FusionLearningConfig> ComputeAdaptiveConfigAsync(
        PortfolioPerformanceMetrics currentMetrics,
        FusionLearningConfig? priorConfig = null,
        Dictionary<string, float>? signalCorrelations = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Computing adaptive fusion config based on performance metrics");

        // Use prior config as base, or create defaults
        var baseConfig = priorConfig ?? await GetOrCreateDefaultConfigAsync(cancellationToken);

        // Start with base values
        var newConfig = new FusionLearningConfig
        {
            Iteration = baseConfig.Iteration + 1,
            CreatedAt = DateTime.UtcNow,
            TechnicalWeight = baseConfig.TechnicalWeight,
            NewsWeight = baseConfig.NewsWeight,
            SectorWeight = baseConfig.SectorWeight,
            MinimumFusionScore = baseConfig.MinimumFusionScore,
            NewsNegativeBoundary = baseConfig.NewsNegativeBoundary,
            NewsPositiveBoundary = baseConfig.NewsPositiveBoundary,
            SessionsAnalyzed = 1,
            PriorConfigJson = JsonSerializer.Serialize(baseConfig),
            PriorPerformanceMetricsJson = JsonSerializer.Serialize(currentMetrics),
            Status = "CANDIDATE"
        };

        // List of changes for reasoning
        var changes = new List<string>();

        // Rule 1: Win Rate < 50% - boost best-correlated signal
        if (currentMetrics.WinRate < 50)
        {
            changes.Add("Win rate degraded to " + currentMetrics.WinRate.ToString("F1") + "%");

            if (signalCorrelations?.Any() == true)
            {
                var bestSignal = signalCorrelations.OrderByDescending(x => x.Value).First();
                if (bestSignal.Key == "Technical")
                {
                    newConfig.TechnicalWeight = AdjustWeight(newConfig.TechnicalWeight, +0.05m);
                    newConfig.NewsWeight = AdjustWeight(newConfig.NewsWeight, -0.03m);
                    changes.Add("Technical signal shows best correlation; increasing TechnicalWeight");
                }
                else if (bestSignal.Key == "News")
                {
                    newConfig.NewsWeight = AdjustWeight(newConfig.NewsWeight, +0.05m);
                    newConfig.TechnicalWeight = AdjustWeight(newConfig.TechnicalWeight, -0.03m);
                    changes.Add("News signal shows best correlation; increasing NewsWeight");
                }
            }
        }

        // Rule 2: Sharpe < 0.5 - be more selective (increase minimum fusion score)
        if (currentMetrics.SharpeRatio < 0.5m)
        {
            newConfig.MinimumFusionScore = Math.Min(newConfig.MinimumFusionScore + 0.05m, MaxFusionScore);
            changes.Add("Sharpe ratio degraded to " + currentMetrics.SharpeRatio.ToString("F2") + "; raising MinimumFusionScore");
        }

        // Rule 3: Max Drawdown > 20% - tighten veto boundaries (more selective on extreme sentiment)
        if (currentMetrics.MaxDrawdown > 20)
        {
            newConfig.NewsNegativeBoundary = Math.Max(newConfig.NewsNegativeBoundary - 0.05m, MinNewsBoundary);
            newConfig.NewsPositiveBoundary = Math.Min(newConfig.NewsPositiveBoundary + 0.05m, MaxPositiveBoundary);
            changes.Add("Max drawdown at " + currentMetrics.MaxDrawdown.ToString("F1") + "%; tightening veto boundaries");
        }

        // Rule 4: Veto Rejection > 40% - relax news boundaries (news is over-blocking)
        if (currentMetrics.VetoRejectionRate > 40)
        {
            newConfig.NewsNegativeBoundary = Math.Min(newConfig.NewsNegativeBoundary + 0.05m, MaxNewsBoundary);
            newConfig.NewsPositiveBoundary = Math.Max(newConfig.NewsPositiveBoundary - 0.05m, MinPositiveBoundary);
            changes.Add("Veto rejection rate at " + currentMetrics.VetoRejectionRate.ToString("F1") + "%; relaxing news boundaries");
        }

        // Rule 5: Hold efficiency declining - reduce sector weight (sector might be misaligned)
        if (currentMetrics.AverageHoldEfficiency < 0.5m && currentMetrics.AverageHoldDays > 0)
        {
            newConfig.SectorWeight = AdjustWeight(newConfig.SectorWeight, -0.03m);
            newConfig.TechnicalWeight = AdjustWeight(newConfig.TechnicalWeight, +0.03m);
            changes.Add("Hold efficiency low; reducing sector signal weight");
        }

        // Enforce constraint: weights must sum to 1.0
        NormalizeWeights(newConfig);

        // Determine risk assessment
        var changeCount = changes.Count;
        newConfig.RiskAssessment = changeCount switch
        {
            0 => "SAFE",
            1 => "SAFE",
            2 => "SAFE",
            3 => "MODERATE",
            _ => "MODERATE"
        };

        // Generate reasoning text
        newConfig.ReasoningText = string.Join(" | ", changes);

        _logger.LogInformation(
            "Proposed config iteration {Iteration}: " +
            "Tech={Tech:F3} News={News:F3} Sector={Sector:F3} | " +
            "MinScore={MinScore:F3} | News[-]{NewsNeg:F3} News[+]{NewsPos:F3} | Risk={Risk}",
            newConfig.Iteration,
            newConfig.TechnicalWeight, newConfig.NewsWeight, newConfig.SectorWeight,
            newConfig.MinimumFusionScore,
            newConfig.NewsNegativeBoundary, newConfig.NewsPositiveBoundary,
            newConfig.RiskAssessment);

        return newConfig;
    }

    /// <summary>
    /// Adjust a weight by delta while respecting min/max bounds
    /// Used in tuning rules
    /// </summary>
    private decimal AdjustWeight(decimal currentWeight, decimal delta)
    {
        var adjusted = currentWeight + delta;
        return Math.Max(MinWeight, Math.Min(MaxWeight, adjusted));
    }

    /// <summary>
    /// Normalize weights so they sum to exactly 1.0
    /// Called after adjustments to ensure constraint
    /// </summary>
    private void NormalizeWeights(FusionLearningConfig config)
    {
        var sum = config.TechnicalWeight + config.NewsWeight + config.SectorWeight;
        if (sum > 0 && Math.Abs(sum - 1m) > 0.0001m)
        {
            var ratio = 1m / sum;
            config.TechnicalWeight = config.TechnicalWeight * ratio;
            config.NewsWeight = config.NewsWeight * ratio;
            config.SectorWeight = config.SectorWeight * ratio;
        }
    }

    /// <summary>
    /// Get existing active config or create defaults if none exist
    /// </summary>
    private async Task<FusionLearningConfig> GetOrCreateDefaultConfigAsync(CancellationToken cancellationToken)
    {
        var activeConfig = await _configRepository.GetActiveConfigAsync(cancellationToken);
        if (activeConfig != null)
            return activeConfig;

        var latestConfig = await _configRepository.GetLatestConfigAsync(cancellationToken);
        if (latestConfig != null)
            return latestConfig;

        // Return defaults
        return new FusionLearningConfig
        {
            Iteration = 0,
            TechnicalWeight = 0.50m,
            NewsWeight = 0.35m,
            SectorWeight = 0.15m,
            MinimumFusionScore = 0.55m,
            NewsNegativeBoundary = -0.35m,
            NewsPositiveBoundary = 0.35m,
            Status = "ACTIVE"
        };
    }
}
