namespace TradingSystem.Core.Models;

/// <summary>
/// Stores a snapshot of fusion algorithm tuning at a point in time
/// Each learning iteration creates a new config (candidate or approved)
/// Forms an immutable audit trail of algorithm evolution
/// </summary>
public class FusionLearningConfig
{
    public long Id { get; set; }

    /// <summary>
    /// Sequential iteration number (1, 2, 3...)
    /// Helps identify learning sequence
    /// </summary>
    public int Iteration { get; set; }

    /// <summary>
    /// When this config was generated/proposed
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this config became active (null if never approved)
    /// </summary>
    public DateTime? AppliedAt { get; set; }

    /// <summary>
    /// Current fusion algorithm weights (range 0-1, should sum to 1.0)
    /// </summary>
    public decimal TechnicalWeight { get; set; } = 0.50m;
    public decimal NewsWeight { get; set; } = 0.35m;
    public decimal SectorWeight { get; set; } = 0.15m;

    /// <summary>
    /// Minimum fusion score required to include a position (0.45-0.75 typical)
    /// Higher = more selective; lower = more inclusive but riskier
    /// </summary>
    public decimal MinimumFusionScore { get; set; } = 0.55m;

    /// <summary>
    /// Veto boundaries: news signal bounds that reject trades despite good technical signals
    /// Lower (more negative) = stricter bearish veto for BUY trades
    /// Higher (more positive) = stricter bullish veto for SELL trades
    /// </summary>
    public decimal NewsNegativeBoundary { get; set; } = -0.35m; // LONG veto threshold
    public decimal NewsPositiveBoundary { get; set; } = 0.35m;   // SHORT veto threshold

    /// <summary>
    /// JSON serialized record of performance metrics that triggered learning
    /// Allows reconstruction of why this config was proposed
    /// </summary>
    public string? PriorPerformanceMetricsJson { get; set; }

    /// <summary>
    /// JSON serialized record of previous config (for comparison/rollback)
    /// </summary>
    public string? PriorConfigJson { get; set; }

    /// <summary>
    /// Human-readable explanation of why these specific changes were made
    /// Example: "Win rate dropped to 45%; increased TechnicalWeight from 0.48 to 0.53"
    /// </summary>
    public string? ReasoningText { get; set; }

    /// <summary>
    /// How many portfolio sessions were analyzed before proposing this config
    /// </summary>
    public int SessionsAnalyzed { get; set; }

    /// <summary>
    /// Risk assessment of this config change
    /// "SAFE" = conservative adjustments, can auto-apply
    /// "MODERATE" = balanced changes, wait for approval
    /// "AGGRESSIVE" = risky changes, require manual approval
    /// </summary>
    public string RiskAssessment { get; set; } = "MODERATE";

    /// <summary>
    /// Current state of this config
    /// CANDIDATE = proposed, awaiting approval
    /// ACTIVE = currently in use
    /// REJECTED = user rejected this config
    /// ROLLED_BACK = was active, but rolled back due to underperformance
    /// </summary>
    public string Status { get; set; } = "CANDIDATE";

    /// <summary>
    /// If rolled back, when did rollback occur
    /// </summary>
    public DateTime? RolledBackAt { get; set; }

    /// <summary>
    /// Count of sessions that ran under this config
    /// Used to evaluate if this config worked well
    /// </summary>
    public int SessionsCompletedUnderThisConfig { get; set; }

    /// <summary>
    /// Aggregate metrics from all sessions run under this config
    /// Determines if this config should be kept or needs adjustment
    /// </summary>
    public string? PerformanceUnderThisConfigJson { get; set; }
}
