using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TradingSystem.AI.Services;
using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Data;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Api.Services;

/// <summary>
/// Lightweight orchestrator for portfolio learning workflow
/// Calls TradingSystem.AI services and returns structured DTOs
/// Managed all heavy lifting in AI layer; this just coordinates
/// </summary>
public class PortfolioLearningOrchestrator
{
    private readonly PortfolioPerformanceAnalyzer _performanceAnalyzer;
    private readonly PortfolioLearningService _learningService;
    private readonly SignalCorrelationAnalyzer _correlationAnalyzer;
    private readonly IFusionLearningConfigRepository _configRepository;
    private readonly IPortfolioPerformanceHistoryRepository _historyRepository;
    private readonly TradingDbContext _dbContext;
    private readonly ILogger<PortfolioLearningOrchestrator> _logger;

    public PortfolioLearningOrchestrator(
        PortfolioPerformanceAnalyzer performanceAnalyzer,
        PortfolioLearningService learningService,
        SignalCorrelationAnalyzer correlationAnalyzer,
        IFusionLearningConfigRepository configRepository,
        IPortfolioPerformanceHistoryRepository historyRepository,
        TradingDbContext dbContext,
        ILogger<PortfolioLearningOrchestrator> logger)
    {
        _performanceAnalyzer = performanceAnalyzer;
        _learningService = learningService;
        _correlationAnalyzer = correlationAnalyzer;
        _configRepository = configRepository;
        _historyRepository = historyRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Main learning trigger: analyze recent performance and decide if tuning needed
    /// Returns LearningResult with proposed config (if learning triggered)
    /// </summary>
    public async Task<LearningResultDto> TriggerLearningAsync(
        string userId,
        string triggerSource = "USER_MANUAL",
        int sessionsToAnalyze = 5,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Learning triggered for user {UserId} via {Source}",
            userId, triggerSource);

        // Step 1: Get recent performance metrics
        var currentMetrics = await _performanceAnalyzer.AnalyzeLastSessionsAsync(
            userId, sessionsToAnalyze, cancellationToken);

        // Step 2: Get prior active config
        var priorConfig = await _configRepository.GetActiveConfigAsync(cancellationToken)
            ?? await _configRepository.GetLatestConfigAsync(cancellationToken);

        // Step 3: Check if learning is needed
        var isLearningNeeded = _learningService.EvaluateLearningNeed(currentMetrics);

        if (!isLearningNeeded && triggerSource == "AUTO_THRESHOLD")
        {
            _logger.LogInformation("Performance is healthy; learning not triggered");
            return CreateNoLearningNeededResult(currentMetrics, priorConfig);
        }

        // Step 4: Analyze signal correlations for learning insights
        var recentSession = await _dbContext.PortfolioManagerSessions
            .Where(s => s.UserId == userId && s.Status == PortfolioSessionStatus.COMPLETED)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        Dictionary<string, float>? signalCorrelations = null;
        string? aiInsights = null;

        if (recentSession != null)
        {
            signalCorrelations = await _correlationAnalyzer.AnalyzeSignalCorrelationsAsync(
                recentSession.Id, cancellationToken);
            aiInsights = FormatSignalCorrelationInsights(signalCorrelations);
        }

        // Step 5: Compute proposed adaptive config
        var proposedConfig = await _learningService.ComputeAdaptiveConfigAsync(
            currentMetrics, priorConfig, signalCorrelations, cancellationToken);

        // Step 6: Persist proposed config as candidate
        var savedConfig = await _configRepository.AddAsync(proposedConfig, cancellationToken);

        // Step 7: Also persist this performance metrics to history
        var historyRecord = new PortfolioPerformanceHistory
        {
            SessionId = recentSession?.Id ?? 0,
            WinRate = currentMetrics.WinRate,
            SharpeRatio = currentMetrics.SharpeRatio,
            MaxDrawdown = currentMetrics.MaxDrawdown,
            ProfitFactor = currentMetrics.ProfitFactor,
            AverageHoldDays = currentMetrics.AverageHoldDays,
            AverageHoldEfficiency = currentMetrics.AverageHoldEfficiency,
            AverageFusionScore = currentMetrics.AverageFusionScore,
            VetoRejectionRate = currentMetrics.VetoRejectionRate,
            TotalTrades = currentMetrics.TotalTrades,
            WinningTrades = currentMetrics.WinningTrades,
            LosingTrades = currentMetrics.LosingTrades,
            TotalPnL = currentMetrics.TotalPnL,
            RecordedAt = DateTime.UtcNow
        };

        await _historyRepository.AddAsync(historyRecord, cancellationToken);

        // Step 8: Build response DTO with all transparency
        return BuildLearningResultDto(
            savedConfig,
            currentMetrics,
            priorConfig,
            signalCorrelations,
            aiInsights,
            triggerSource);
    }

    /// <summary>
    /// Approve a proposed learning config and make it active
    /// </summary>
    public async Task<LearningResultDto> ApproveConfigAsync(
        long configId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Approving fusion learning config {ConfigId}", configId);

        var config = await _configRepository.GetByIdAsync(configId, cancellationToken)
            ?? throw new ArgumentException($"Config {configId} not found");

        // Deactivate previous active config
        var activeConfig = await _configRepository.GetActiveConfigAsync(cancellationToken);
        if (activeConfig != null)
        {
            activeConfig.Status = "INACTIVE";
            await _configRepository.UpdateAsync(activeConfig, cancellationToken);
        }

        // Activate this config
        config.Status = "ACTIVE";
        config.AppliedAt = DateTime.UtcNow;
        await _configRepository.UpdateAsync(config, cancellationToken);

        _logger.LogInformation("Config {Iteration} is now ACTIVE", config.Iteration);

        // Return result showing approval
        var result = new LearningResultDto
        {
            IterationNumber = config.Iteration,
            TriggeredAt = config.CreatedAt,
            TriggerSource = "USER_MANUAL",
            Status = "APPLIED",
            CompletedAt = DateTime.UtcNow,
            ProposedConfig = MapConfigToDto(config),
            ReasoningText = config.ReasoningText ?? "",
            RiskAssessment = config.RiskAssessment
        };

        return result;
    }

    /// <summary>
    /// Reject a proposed config
    /// </summary>
    public async Task<LearningResultDto> RejectConfigAsync(
        long configId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rejecting fusion learning config {ConfigId}", configId);

        var config = await _configRepository.GetByIdAsync(configId, cancellationToken)
            ?? throw new ArgumentException($"Config {configId} not found");

        config.Status = "REJECTED";
        await _configRepository.UpdateAsync(config, cancellationToken);

        var result = new LearningResultDto
        {
            IterationNumber = config.Iteration,
            TriggeredAt = config.CreatedAt,
            Status = "REJECTED",
            CompletedAt = DateTime.UtcNow,
            ProposedConfig = MapConfigToDto(config)
        };

        return result;
    }

    /// <summary>
    /// Rollback to prior config if current one underperforms
    /// </summary>
    public async Task<LearningResultDto> RollbackConfigAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rolling back active fusion learning config");

        var activeConfig = await _configRepository.GetActiveConfigAsync(cancellationToken)
            ?? throw new InvalidOperationException("No active config found");

        var lastConfigs = await _configRepository.GetLastNConfigsAsync(2, cancellationToken);
        var priorConfig = lastConfigs.FirstOrDefault(c => c.Id != activeConfig.Id)
            ?? throw new InvalidOperationException("No prior config available for rollback");

        // Deactivate current
        activeConfig.Status = "ROLLED_BACK";
        activeConfig.RolledBackAt = DateTime.UtcNow;
        await _configRepository.UpdateAsync(activeConfig, cancellationToken);

        // Reactivate prior
        priorConfig.Status = "ACTIVE";
        await _configRepository.UpdateAsync(priorConfig, cancellationToken);

        _logger.LogInformation(
            "Rolled back from iteration {FromIter} to {ToIter}",
            activeConfig.Iteration, priorConfig.Iteration);

        var result = new LearningResultDto
        {
            IterationNumber = priorConfig.Iteration,
            TriggeredAt = DateTime.UtcNow,
            TriggerSource = "USER_MANUAL",
            Status = "APPLIED",
            CompletedAt = DateTime.UtcNow,
            ProposedConfig = MapConfigToDto(priorConfig),
            ReasoningText = "Rolled back due to underperformance"
        };

        return result;
    }

    /// <summary>
    /// Get learning history (audit trail of all learning iterations)
    /// </summary>
    public async Task<List<LearningResultDto>> GetLearningHistoryAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching learning history (limit: {Limit})", limit);

        var configs = await _configRepository.GetLastNConfigsAsync(limit, cancellationToken);

        return configs
            .OrderByDescending(c => c.Iteration)
            .Select(c => new LearningResultDto
            {
                IterationNumber = c.Iteration,
                TriggeredAt = c.CreatedAt,
                Status = c.Status,
                ProposedConfig = MapConfigToDto(c),
                ReasoningText = c.ReasoningText ?? "",
                RiskAssessment = c.RiskAssessment,
                SessionsAnalyzed = c.SessionsAnalyzed,
                CompletedAt = c.AppliedAt ?? c.RolledBackAt
            })
            .ToList();
    }

    /// <summary>
    /// Get current active config
    /// </summary>
    public async Task<FusionConfigSnapshotDto?> GetCurrentConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var config = await _configRepository.GetActiveConfigAsync(cancellationToken)
            ?? await _configRepository.GetLatestConfigAsync(cancellationToken);

        return config != null ? MapConfigToDto(config) : null;
    }

    // ===== Helper methods =====

    private LearningResultDto CreateNoLearningNeededResult(
        PortfolioPerformanceMetrics metrics,
        FusionLearningConfig? priorConfig)
    {
        return new LearningResultDto
        {
            IterationNumber = priorConfig?.Iteration ?? 0,
            TriggeredAt = DateTime.UtcNow,
            TriggerSource = "AUTO_THRESHOLD",
            Status = "REJECTED",
            CurrentMetrics = MapMetricsToDto(metrics),
            ReasoningText = "Performance is healthy; learning not needed"
        };
    }

    private LearningResultDto BuildLearningResultDto(
        FusionLearningConfig proposedConfig,
        PortfolioPerformanceMetrics currentMetrics,
        FusionLearningConfig? priorConfig,
        Dictionary<string, float>? signalCorrelations,
        string? aiInsights,
        string triggerSource)
    {
        var changes = ComputeConfigChanges(priorConfig, proposedConfig);

        return new LearningResultDto
        {
            IterationNumber = proposedConfig.Iteration,
            TriggeredAt = proposedConfig.CreatedAt,
            TriggerSource = triggerSource,
            CurrentMetrics = MapMetricsToDto(currentMetrics),
            PriorConfig = priorConfig != null ? MapConfigToDto(priorConfig) : null,
            ProposedConfig = MapConfigToDto(proposedConfig),
            Changes = changes,
            ReasoningText = proposedConfig.ReasoningText ?? "",
            RiskAssessment = proposedConfig.RiskAssessment,
            AIModelInsights = aiInsights,
            Status = "PENDING_ACTIVATION",
            SessionsAnalyzed = proposedConfig.SessionsAnalyzed
        };
    }

    private List<TuningChangeDto> ComputeConfigChanges(
        FusionLearningConfig? priorConfig,
        FusionLearningConfig proposedConfig)
    {
        var changes = new List<TuningChangeDto>();
        var prior = priorConfig ?? new FusionLearningConfig();

        if (Math.Abs(prior.TechnicalWeight - proposedConfig.TechnicalWeight) > 0.001m)
            changes.Add(new TuningChangeDto
            {
                Parameter = "TechnicalWeight",
                OldValue = prior.TechnicalWeight,
                NewValue = proposedConfig.TechnicalWeight,
                Justification = "Adjusted based on signal correlation analysis"
            });

        if (Math.Abs(prior.NewsWeight - proposedConfig.NewsWeight) > 0.001m)
            changes.Add(new TuningChangeDto
            {
                Parameter = "NewsWeight",
                OldValue = prior.NewsWeight,
                NewValue = proposedConfig.NewsWeight,
                Justification = "Adjusted based on news signal performance"
            });

        if (Math.Abs(prior.SectorWeight - proposedConfig.SectorWeight) > 0.001m)
            changes.Add(new TuningChangeDto
            {
                Parameter = "SectorWeight",
                OldValue = prior.SectorWeight,
                NewValue = proposedConfig.SectorWeight,
                Justification = "Adjusted to improve sector alignment"
            });

        if (Math.Abs(prior.MinimumFusionScore - proposedConfig.MinimumFusionScore) > 0.001m)
            changes.Add(new TuningChangeDto
            {
                Parameter = "MinimumFusionScore",
                OldValue = prior.MinimumFusionScore,
                NewValue = proposedConfig.MinimumFusionScore,
                Justification = "Adjusted selectivity based on performance"
            });

        if (Math.Abs(prior.NewsNegativeBoundary - proposedConfig.NewsNegativeBoundary) > 0.001m)
            changes.Add(new TuningChangeDto
            {
                Parameter = "NewsNegativeBoundary",
                OldValue = prior.NewsNegativeBoundary,
                NewValue = proposedConfig.NewsNegativeBoundary,
                Justification = "Adjusted veto sensitivity for LONG trades"
            });

        if (Math.Abs(prior.NewsPositiveBoundary - proposedConfig.NewsPositiveBoundary) > 0.001m)
            changes.Add(new TuningChangeDto
            {
                Parameter = "NewsPositiveBoundary",
                OldValue = prior.NewsPositiveBoundary,
                NewValue = proposedConfig.NewsPositiveBoundary,
                Justification = "Adjusted veto sensitivity for SHORT trades"
            });

        return changes;
    }

    private string FormatSignalCorrelationInsights(Dictionary<string, float> correlations)
    {
        var parts = correlations
            .OrderByDescending(x => x.Value)
            .Select(x => $"{x.Key}: {x.Value:+0.000;-0.000;0.000}")
            .ToList();
        return "Signal correlations: " + string.Join(", ", parts);
    }

    private FusionConfigSnapshotDto MapConfigToDto(FusionLearningConfig config)
    {
        return new FusionConfigSnapshotDto
        {
            Iteration = config.Iteration,
            TechnicalWeight = config.TechnicalWeight,
            NewsWeight = config.NewsWeight,
            SectorWeight = config.SectorWeight,
            MinimumFusionScore = config.MinimumFusionScore,
            NewsNegativeBoundary = config.NewsNegativeBoundary,
            NewsPositiveBoundary = config.NewsPositiveBoundary,
            AppliedAt = config.AppliedAt,
            Status = config.Status
        };
    }

    private PortfolioPerformanceMetricsDto MapMetricsToDto(PortfolioPerformanceMetrics metrics)
    {
        return new PortfolioPerformanceMetricsDto
        {
            WinRate = metrics.WinRate,
            SharpeRatio = metrics.SharpeRatio,
            MaxDrawdown = metrics.MaxDrawdown,
            ProfitFactor = metrics.ProfitFactor,
            AverageHoldDays = metrics.AverageHoldDays,
            AverageHoldEfficiency = metrics.AverageHoldEfficiency,
            AverageFusionScore = metrics.AverageFusionScore,
            VetoRejectionRate = metrics.VetoRejectionRate,
            TotalTrades = metrics.TotalTrades,
            WinningTrades = metrics.WinningTrades,
            LosingTrades = metrics.LosingTrades,
            TotalPnL = metrics.TotalPnL,
            ComputedAt = metrics.ComputedAt
        };
    }
}
