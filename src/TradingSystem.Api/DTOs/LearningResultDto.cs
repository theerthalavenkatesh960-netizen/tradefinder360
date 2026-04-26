namespace TradingSystem.Api.DTOs;

/// <summary>
/// Request to trigger learning analysis
/// </summary>
public class TriggerLearningRequest
{
    /// <summary>
    /// User ID (for multi-tenant; default: "default_user")
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Trigger source: "USER_MANUAL", "AUTO_THRESHOLD", "SCHEDULED"
    /// </summary>
    public string? TriggerSource { get; set; }

    /// <summary>
    /// Number of past sessions to analyze (default: 5)
    /// </summary>
    public int? SessionsToAnalyze { get; set; }
}

/// <summary>
/// Represents a single tuning change made to the fusion algorithm
/// Part of learning result to show exact what changed and why
/// </summary>
public class TuningChangeDto
{
    /// <summary>
    /// Parameter name: TechnicalWeight, NewsWeight, SectorWeight, MinFusionScore, etc.
    /// </summary>
    public string Parameter { get; set; } = string.Empty;

    /// <summary>
    /// Previous value
    /// </summary>
    public decimal OldValue { get; set; }

    /// <summary>
    /// New proposed value
    /// </summary>
    public decimal NewValue { get; set; }

    /// <summary>
    /// Why this change was made
    /// Example: "Win rate dropped to 45%; increased technical weight"
    /// </summary>
    public string Justification { get; set; } = string.Empty;
}

/// <summary>
/// Snapshot of fusion algorithm configuration
/// Used to/from fusion algorithm config for comparison
/// </summary>
public class FusionConfigSnapshotDto
{
    /// <summary>
    /// Configuration iteration number
    /// </summary>
    public int Iteration { get; set; }

    /// <summary>
    /// Algorithm weights
    /// </summary>
    public decimal TechnicalWeight { get; set; }
    public decimal NewsWeight { get; set; }
    public decimal SectorWeight { get; set; }

    /// <summary>
    /// Decision thresholds
    /// </summary>
    public decimal MinimumFusionScore { get; set; }
    public decimal NewsNegativeBoundary { get; set; }    // Veto threshold for BUY
    public decimal NewsPositiveBoundary { get; set; }    // Veto threshold for SELL

    /// <summary>
    /// When this config became/will become active
    /// </summary>
    public DateTime? AppliedAt { get; set; }

    /// <summary>
    /// Status: CANDIDATE, ACTIVE, REJECTED, ROLLED_BACK
    /// </summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Complete learning result: shows metrics, proposed config, and justifications
/// Used for user approval/rejection workflow
/// </summary>
public class LearningResultDto
{
    /// <summary>
    /// Learning iteration number (1, 2, 3...)
    /// </summary>
    public int IterationNumber { get; set; }

    /// <summary>
    /// When learning was triggered
    /// </summary>
    public DateTime TriggeredAt { get; set; }

    /// <summary>
    /// What triggered the learning: AUTO_THRESHOLD, USER_MANUAL, SCHEDULED
    /// </summary>
    public string TriggerSource { get; set; } = string.Empty;

    /// <summary>
    /// Performance metrics before this learning event (from most recent closed sessions)
    /// </summary>
    public PortfolioPerformanceMetricsDto CurrentMetrics { get; set; } = new();

    /// <summary>
    /// Performance metrics from sessions run under prior config
    /// For user to compare: "was the old config better?"
    /// </summary>
    public PortfolioPerformanceMetricsDto? PriorMetrics { get; set; }

    /// <summary>
    /// Previous fusion config (what was active)
    /// </summary>
    public FusionConfigSnapshotDto? PriorConfig { get; set; }

    /// <summary>
    /// Proposed new fusion config (if PENDING_ACTIVATION)
    /// </summary>
    public FusionConfigSnapshotDto? ProposedConfig { get; set; }

    /// <summary>
    /// List of specific parameter changes
    /// Each shows old value, new value, and justification
    /// </summary>
    public List<TuningChangeDto> Changes { get; set; } = new();

    /// <summary>
    /// Human-readable explanation of why learning happened
    /// Example: "Win rate dropped from 60% to 45%; News veto was too strict"
    /// </summary>
    public string ReasoningText { get; set; } = string.Empty;

    /// <summary>
    /// Risk assessment of proposed changes: SAFE, MODERATE, AGGRESSIVE
    /// SAFE: can be auto-approved
    /// MODERATE: needs manual review
    /// AGGRESSIVE: needs approval + override confirmation
    /// </summary>
    public string RiskAssessment { get; set; } = string.Empty;

    /// <summary>
    /// Insights from AI model prediction accuracy
    /// Example: "Technical signal correlation: +0.72 with wins"
    /// </summary>
    public string? AIModelInsights { get; set; }

    /// <summary>
    /// Current status of this learning result
    /// PENDING_ACTIVATION: waiting for user approval
    /// APPLIED: learning approved and config is now active
    /// REJECTED: user rejected the proposed config
    /// ROLLED_BACK: was active but manually rolled back
    /// </summary>
    public string Status { get; set; } = "PENDING_ACTIVATION";

    /// <summary>
    /// When this result was applied or rolled back (if applicable)
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// How many sessions were analyzed to make this decision
    /// </summary>
    public int SessionsAnalyzed { get; set; }
}

/// <summary>
/// Portfolio performance metrics DTO for API responses
/// Mirrors PortfolioPerformanceMetrics from Core, with same data
/// </summary>
public class PortfolioPerformanceMetricsDto
{
    public decimal WinRate { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal AverageHoldDays { get; set; }
    public decimal AverageHoldEfficiency { get; set; }
    public decimal AverageFusionScore { get; set; }
    public decimal VetoRejectionRate { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal TotalPnL { get; set; }
    public DateTime ComputedAt { get; set; }
}
