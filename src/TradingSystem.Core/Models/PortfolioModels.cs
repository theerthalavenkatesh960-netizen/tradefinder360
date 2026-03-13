namespace TradingSystem.Core.Models;

/// <summary>
/// Portfolio optimization request configuration
/// </summary>
public class PortfolioOptimizationRequest
{
    public decimal TotalCapital { get; set; }
    public decimal MaxRiskPerTradePercent { get; set; } = 2.0m; // Max 2% risk per trade
    public decimal MaxPortfolioRiskPercent { get; set; } = 6.0m; // Max 6% total portfolio risk
    public int MaxPositions { get; set; } = 10;
    public bool EnableSectorDiversification { get; set; } = true;
    public decimal MaxSectorAllocationPercent { get; set; } = 30m; // Max 30% per sector
    public decimal MinPositionSizePercent { get; set; } = 5m; // Min 5% per position
    public List<StrategyType> AllowedStrategies { get; set; } = new();
    public int TimeframeMinutes { get; set; } = 15;
    public int MinConfidence { get; set; } = 60;
}

/// <summary>
/// Individual position in optimized portfolio
/// </summary>
public class OptimizedPosition
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public StrategyType Strategy { get; set; }
    public string Direction { get; set; } = string.Empty; // BUY/SELL
    
    // Price levels
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    
    // Position sizing
    public decimal AllocatedCapital { get; set; }
    public decimal AllocationPercent { get; set; }
    public int Quantity { get; set; }
    
    // Risk metrics
    public decimal RiskAmount { get; set; }
    public decimal RiskPercent { get; set; }
    public decimal RiskRewardRatio { get; set; }
    
    // Signal quality
    public decimal Confidence { get; set; }
    public int Score { get; set; }
    public decimal ExpectedReturn { get; set; }
    public decimal ExpectedReturnPercent { get; set; }
    
    // Rationale
    public List<string> Signals { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>
/// Optimized portfolio result
/// </summary>
public class OptimizedPortfolio
{
    public decimal TotalCapital { get; set; }
    public decimal AllocatedCapital { get; set; }
    public decimal UnallocatedCapital { get; set; }
    public decimal AllocationPercent { get; set; }
    
    // Risk metrics
    public decimal TotalRiskAmount { get; set; }
    public decimal TotalRiskPercent { get; set; }
    public decimal MaxRiskPerTrade { get; set; }
    public decimal MaxPortfolioRisk { get; set; }
    
    // Diversification
    public int TotalPositions { get; set; }
    public int UniqueSectors { get; set; }
    public Dictionary<string, decimal> SectorAllocation { get; set; } = new();
    public Dictionary<StrategyType, int> StrategyDistribution { get; set; } = new();
    
    // Expected performance
    public decimal TotalExpectedReturn { get; set; }
    public decimal TotalExpectedReturnPercent { get; set; }
    public decimal AverageConfidence { get; set; }
    public decimal AverageRiskReward { get; set; }
    
    // Positions
    public List<OptimizedPosition> Positions { get; set; } = new();
    
    // Rejected opportunities
    public List<RejectedOpportunity> RejectedOpportunities { get; set; } = new();
    
    // Summary
    public DateTime GeneratedAt { get; set; }
    public List<string> OptimizationNotes { get; set; } = new();
    public PortfolioHealthScore HealthScore { get; set; } = new();
}

/// <summary>
/// Opportunity rejected during optimization
/// </summary>
public class RejectedOpportunity
{
    public string Symbol { get; set; } = string.Empty;
    public StrategyType Strategy { get; set; }
    public decimal Confidence { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
}

/// <summary>
/// Portfolio health assessment
/// </summary>
public class PortfolioHealthScore
{
    public decimal OverallScore { get; set; } // 0-100
    public decimal DiversificationScore { get; set; }
    public decimal RiskManagementScore { get; set; }
    public decimal QualityScore { get; set; }
    public string HealthRating { get; set; } = string.Empty; // EXCELLENT, GOOD, FAIR, POOR
    public List<string> Strengths { get; set; } = new();
    public List<string> Concerns { get; set; } = new();
}