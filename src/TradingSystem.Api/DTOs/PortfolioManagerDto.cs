namespace TradingSystem.Api.DTOs;

public class CreatePortfolioSessionRequest
{
    public string SessionName { get; set; } = "My Portfolio";
    public decimal Budget { get; set; }
    public string RiskProfile { get; set; } = "balanced";
    public List<string> PreferredSectors { get; set; } = new();
    public List<string> PreferredThemes { get; set; } = new();
    public bool AutoRebalanceEnabled { get; set; }
    public int MaxPositions { get; set; } = 10;
    public int TimeframeMinutes { get; set; } = 15;
    public int MinConfidence { get; set; } = 60;
}

public class RunPortfolioSessionRequest
{
    public bool UseLatestPreferences { get; set; } = true;
}

public class UpdatePortfolioSessionRequest
{
    public string SessionName { get; set; } = "My Portfolio";
    public decimal Budget { get; set; }
    public string RiskProfile { get; set; } = "balanced";
    public List<string> PreferredSectors { get; set; } = new();
    public List<string> PreferredThemes { get; set; } = new();
    public bool AutoRebalanceEnabled { get; set; }
    public int MaxPositions { get; set; } = 10;
    public int TimeframeMinutes { get; set; } = 15;
    public int MinConfidence { get; set; } = 60;
}

public class ClonePortfolioSessionRequest
{
    public string? SessionName { get; set; }
}

public class PortfolioSessionSummaryDto
{
    public long SessionId { get; set; }
    public string SessionName { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public string RiskProfile { get; set; } = string.Empty;
    public bool AutoRebalanceEnabled { get; set; }
    public int MaxPositions { get; set; }
    public int TimeframeMinutes { get; set; }
    public int MinConfidence { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public int OpenPositions { get; set; }
    public int ClosedPositions { get; set; }
    public decimal AllocatedCapital { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal WinRatePercent { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PortfolioPositionDto
{
    public long TradeId { get; set; }
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal AllocationPercent { get; set; }
    public decimal AllocatedCapital { get; set; }
    public decimal Confidence { get; set; }
    public decimal? FusionScore { get; set; }
    public decimal? FusionNewsSignal { get; set; }
    public decimal? FusionTechnicalSignal { get; set; }
    public decimal? FusionSectorSignal { get; set; }
    public bool? FusionDirectionVeto { get; set; }
    public bool? FusionIncluded { get; set; }
    public string? FusionEvidence { get; set; }
    public decimal? Pnl { get; set; }
    public decimal? PnlPercent { get; set; }
    public string Status { get; set; } = string.Empty;
    public string EntryReasoning { get; set; } = string.Empty;
    public string? ExitReasoning { get; set; }
    public List<string> Signals { get; set; } = new();
    public string ModelProvider { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class PortfolioSessionDetailDto
{
    public PortfolioSessionSummaryDto Summary { get; set; } = new();
    public List<string> PreferredSectors { get; set; } = new();
    public List<string> PreferredThemes { get; set; } = new();
    public List<PortfolioPositionDto> OpenPositions { get; set; } = new();
    public List<PortfolioPositionDto> ClosedPositions { get; set; } = new();
}

public class PortfolioRunResponseDto
{
    public long SessionId { get; set; }
    public int OpenPositions { get; set; }
    public decimal AllocatedCapital { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime RunAt { get; set; }
}

public class PortfolioNewsItemDto
{
    public long ArticleId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public string Sentiment { get; set; } = string.Empty;
    public decimal SentimentScore { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal ImpactScore { get; set; }
    public decimal Confidence { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
}

public class PortfolioEventTriggerResultDto
{
    public DateTime TriggeredAt { get; set; }
    public int SessionsScanned { get; set; }
    public int EventsDetected { get; set; }
    public int TriggeredRuns { get; set; }
    public int SkippedRecentRuns { get; set; }
    public List<PortfolioEventTriggerSessionDto> TriggeredSessions { get; set; } = new();
}

public class PortfolioEventTriggerSessionDto
{
    public long SessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
