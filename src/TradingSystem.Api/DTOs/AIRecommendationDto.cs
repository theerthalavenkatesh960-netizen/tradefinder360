namespace TradingSystem.Api.DTOs;

public class AIRecommendationRequest
{
    public int TopCount { get; set; } = 10;
    public int MinConfidence { get; set; } = 60;
    public float MinAIProbability { get; set; } = 0.5f;
    public int TimeframeMinutes { get; set; } = 15;
    public List<string> AllowedStrategies { get; set; } = new();
}

public class AIRecommendationDto
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal RiskRewardRatio { get; set; }
    
    // AI Metrics
    public float SuccessProbability { get; set; }
    public float AIScore { get; set; }
    public string PredictionConfidence { get; set; } = string.Empty;
    
    // Combined Scoring
    public decimal StrategyScore { get; set; }
    public decimal StrategyConfidence { get; set; }
    public decimal CompositeScore { get; set; }
    
    // Feature Importance
    public Dictionary<string, float> TopFeatures { get; set; } = new();
    
    // Trade Details
    public string Strategy { get; set; } = string.Empty;
    public List<string> Signals { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    
    // Market Context
    public decimal MarketSentiment { get; set; }
    public string MarketCondition { get; set; } = string.Empty;
    
    // Risk Assessment
    public string RiskLevel { get; set; } = string.Empty;
    public List<string> RiskFactors { get; set; } = new();
    public List<string> OpportunityFactors { get; set; } = new();
}