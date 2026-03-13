namespace TradingSystem.Api.DTOs;

public class PortfolioOptimizationRequestDto
{
    public decimal TotalCapital { get; set; }
    public decimal MaxRiskPerTradePercent { get; set; } = 2.0m;
    public decimal MaxPortfolioRiskPercent { get; set; } = 6.0m;
    public int MaxPositions { get; set; } = 10;
    public bool EnableSectorDiversification { get; set; } = true;
    public decimal MaxSectorAllocationPercent { get; set; } = 30m;
    public decimal MinPositionSizePercent { get; set; } = 5m;
    public List<string> AllowedStrategies { get; set; } = new();
    public int TimeframeMinutes { get; set; } = 15;
    public int MinConfidence { get; set; } = 60;
}

public class OptimizedPortfolioDto
{
    public decimal TotalCapital { get; set; }
    public decimal AllocatedCapital { get; set; }
    public decimal UnallocatedCapital { get; set; }
    public decimal AllocationPercent { get; set; }
    
    public decimal TotalRiskAmount { get; set; }
    public decimal TotalRiskPercent { get; set; }
    public decimal MaxRiskPerTrade { get; set; }
    public decimal MaxPortfolioRisk { get; set; }
    
    public int TotalPositions { get; set; }
    public int UniqueSectors { get; set; }
    public Dictionary<string, decimal> SectorAllocation { get; set; } = new();
    public Dictionary<string, int> StrategyDistribution { get; set; } = new();
    
    public decimal TotalExpectedReturn { get; set; }
    public decimal TotalExpectedReturnPercent { get; set; }
    public decimal AverageConfidence { get; set; }
    public decimal AverageRiskReward { get; set; }
    
    public List<OptimizedPositionDto> Positions { get; set; } = new();
    public List<RejectedOpportunityDto> RejectedOpportunities { get; set; } = new();
    
    public DateTime GeneratedAt { get; set; }
    public List<string> OptimizationNotes { get; set; } = new();
    public PortfolioHealthScoreDto HealthScore { get; set; } = new();
}

public class OptimizedPositionDto
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    
    public decimal AllocatedCapital { get; set; }
    public decimal AllocationPercent { get; set; }
    public int Quantity { get; set; }
    
    public decimal RiskAmount { get; set; }
    public decimal RiskPercent { get; set; }
    public decimal RiskRewardRatio { get; set; }
    
    public decimal Confidence { get; set; }
    public int Score { get; set; }
    public decimal ExpectedReturn { get; set; }
    public decimal ExpectedReturnPercent { get; set; }
    
    public List<string> Signals { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

public class RejectedOpportunityDto
{
    public string Symbol { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
}

public class PortfolioHealthScoreDto
{
    public decimal OverallScore { get; set; }
    public decimal DiversificationScore { get; set; }
    public decimal RiskManagementScore { get; set; }
    public decimal QualityScore { get; set; }
    public string HealthRating { get; set; } = string.Empty;
    public List<string> Strengths { get; set; } = new();
    public List<string> Concerns { get; set; } = new();
}