using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Monitors AI model performance and triggers retraining when needed
/// </summary>
public class ModelPerformanceMonitor
{
    private readonly ITradeOutcomeRepository _outcomeRepository;
    private readonly IAIModelVersionRepository _modelVersionRepository;
    private readonly ModelTrainingPipeline _trainingPipeline;
    private readonly ILogger<ModelPerformanceMonitor> _logger;

    // Performance thresholds
    private const float MIN_WIN_RATE = 55f;
    private const float MIN_PROFIT_FACTOR = 1.2f;
    private const float MIN_SHARPE_RATIO = 0.5f;
    private const float MAX_DRAWDOWN = 15f;
    private const float MIN_PREDICTION_ACCURACY = 60f;

    public ModelPerformanceMonitor(
        ITradeOutcomeRepository outcomeRepository,
        IAIModelVersionRepository modelVersionRepository,
        ModelTrainingPipeline trainingPipeline,
        ILogger<ModelPerformanceMonitor> logger)
    {
        _outcomeRepository = outcomeRepository;
        _modelVersionRepository = modelVersionRepository;
        _trainingPipeline = trainingPipeline;
        _logger = logger;
    }

    /// <summary>
    /// Monitor model performance and trigger retraining if needed
    /// </summary>
    public async Task<PerformanceMonitorResult> MonitorAndActAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting model performance monitoring...");

        var activeModel = await _modelVersionRepository.GetActiveModelAsync(
            "AlphaModel", cancellationToken);

        if (activeModel == null)
        {
            _logger.LogWarning("No active model found. Triggering initial training.");
            var result = await _trainingPipeline.ExecuteTrainingPipelineAsync(true, cancellationToken);
            
            return new PerformanceMonitorResult
            {
                ModelVersion = result.ModelVersion,
                Status = "INITIAL_TRAINING",
                RetrainingTriggered = true,
                Message = "No active model - initial training completed"
            };
        }

        // Get performance metrics
        var metrics = await GetCurrentPerformanceMetricsAsync(
            activeModel.Version, cancellationToken);

        // Check if performance is acceptable
        var issues = CheckPerformanceThresholds(metrics);

        if (issues.Any())
        {
            _logger.LogWarning(
                "Performance issues detected for model {Version}: {Issues}",
                activeModel.Version, string.Join(", ", issues));

            // Trigger retraining
            var retrainResult = await _trainingPipeline.ExecuteTrainingPipelineAsync(
                true, cancellationToken);

            return new PerformanceMonitorResult
            {
                ModelVersion = activeModel.Version,
                Status = "PERFORMANCE_DEGRADED",
                RetrainingTriggered = true,
                PerformanceIssues = issues,
                NewModelVersion = retrainResult.ModelVersion,
                Metrics = metrics,
                Message = $"Retraining triggered due to: {string.Join(", ", issues)}"
            };
        }

        _logger.LogInformation("Model {Version} performance is acceptable", activeModel.Version);

        return new PerformanceMonitorResult
        {
            ModelVersion = activeModel.Version,
            Status = "HEALTHY",
            RetrainingTriggered = false,
            Metrics = metrics,
            Message = "Model performance is within acceptable thresholds"
        };
    }

    /// <summary>
    /// Get current performance metrics for active model
    /// </summary>
    public async Task<ModelPerformanceMetrics> GetCurrentPerformanceMetricsAsync(
        string? modelVersion = null,
        CancellationToken cancellationToken = default)
    {
        // Last 7 days performance
        var stats7d = await _outcomeRepository.GetStatisticsAsync(
            modelVersion,
            DateTimeOffset.UtcNow.AddDays(-7),
            cancellationToken: cancellationToken);

        // Last 30 days performance
        var stats30d = await _outcomeRepository.GetStatisticsAsync(
            modelVersion,
            DateTimeOffset.UtcNow.AddDays(-30),
            cancellationToken: cancellationToken);

        return new ModelPerformanceMetrics
        {
            ModelVersion = modelVersion ?? "current",
            
            // 7-day metrics
            WinRate7d = stats7d.WinRate,
            ProfitFactor7d = stats7d.ProfitFactor,
            SharpeRatio7d = stats7d.SharpeRatio,
            TotalPnL7d = stats7d.TotalPnL,
            TotalTrades7d = stats7d.TotalTrades,
            PredictionAccuracy7d = stats7d.AveragePredictionAccuracy,
            
            // 30-day metrics
            WinRate30d = stats30d.WinRate,
            ProfitFactor30d = stats30d.ProfitFactor,
            SharpeRatio30d = stats30d.SharpeRatio,
            MaxDrawdown30d = stats30d.MaxDrawdown,
            TotalPnL30d = stats30d.TotalPnL,
            TotalTrades30d = stats30d.TotalTrades,
            PredictionAccuracy30d = stats30d.AveragePredictionAccuracy,
            
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Check if metrics meet minimum thresholds
    /// </summary>
    private List<string> CheckPerformanceThresholds(ModelPerformanceMetrics metrics)
    {
        var issues = new List<string>();

        // Only check if we have enough trades
        if (metrics.TotalTrades7d < 5)
        {
            return issues; // Not enough data to judge
        }

        if (metrics.WinRate7d < MIN_WIN_RATE)
            issues.Add($"Win rate below threshold: {metrics.WinRate7d:F1}% < {MIN_WIN_RATE}%");

        if (metrics.ProfitFactor7d < MIN_PROFIT_FACTOR && metrics.TotalTrades7d >= 10)
            issues.Add($"Profit factor below threshold: {metrics.ProfitFactor7d:F2} < {MIN_PROFIT_FACTOR}");

        if (metrics.SharpeRatio7d < MIN_SHARPE_RATIO && metrics.TotalTrades7d >= 10)
            issues.Add($"Sharpe ratio below threshold: {metrics.SharpeRatio7d:F2} < {MIN_SHARPE_RATIO}");

        if (metrics.MaxDrawdown30d > (decimal)MAX_DRAWDOWN)
            issues.Add($"Drawdown exceeds limit: {metrics.MaxDrawdown30d:F1}% > {MAX_DRAWDOWN}%");

        if (metrics.PredictionAccuracy7d < MIN_PREDICTION_ACCURACY && metrics.TotalTrades7d >= 10)
            issues.Add($"Prediction accuracy low: {metrics.PredictionAccuracy7d:F1}% < {MIN_PREDICTION_ACCURACY}%");

        return issues;
    }
}

public class ModelPerformanceMetrics
{
    public string ModelVersion { get; set; } = string.Empty;
    
    // 7-day metrics
    public float WinRate7d { get; set; }
    public float ProfitFactor7d { get; set; }
    public float SharpeRatio7d { get; set; }
    public decimal TotalPnL7d { get; set; }
    public int TotalTrades7d { get; set; }
    public float PredictionAccuracy7d { get; set; }
    
    // 30-day metrics
    public float WinRate30d { get; set; }
    public float ProfitFactor30d { get; set; }
    public float SharpeRatio30d { get; set; }
    public decimal MaxDrawdown30d { get; set; }
    public decimal TotalPnL30d { get; set; }
    public int TotalTrades30d { get; set; }
    public float PredictionAccuracy30d { get; set; }
    
    public DateTimeOffset LastUpdated { get; set; }
}

public class PerformanceMonitorResult
{
    public string? ModelVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool RetrainingTriggered { get; set; }
    public List<string> PerformanceIssues { get; set; } = new();
    public string? NewModelVersion { get; set; }
    public ModelPerformanceMetrics? Metrics { get; set; }
    public string Message { get; set; } = string.Empty;
}