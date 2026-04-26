namespace TradingSystem.Core.Models;

public enum PortfolioSessionStatus
{
    DRAFT,
    READY,
    RUNNING,
    STOPPED,
    COMPLETED,
    FAILED
}

public enum PortfolioSessionMode
{
    MANUAL,
    SCHEDULED
}

public enum PortfolioTradeStatus
{
    OPEN,
    CLOSED,
    CANCELLED
}

public class PortfolioManagerSession
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
    public decimal InitialCapital { get; set; }
    public string RiskProfile { get; set; } = "balanced";
    public List<string> PreferredSectors { get; set; } = new();
    public List<string> PreferredThemes { get; set; } = new();
    public bool AutoRebalanceEnabled { get; set; }
    public int MaxPositions { get; set; } = 10;
    public int TimeframeMinutes { get; set; } = 15;
    public int MinConfidence { get; set; } = 60;
    public PortfolioSessionMode Mode { get; set; } = PortfolioSessionMode.MANUAL;
    public PortfolioSessionStatus Status { get; set; } = PortfolioSessionStatus.DRAFT;
    public string LastProvider { get; set; } = string.Empty;
    public string LastModel { get; set; } = string.Empty;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public int TotalRuns { get; set; }
    public decimal AllocatedCapital { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal WinRatePercent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<PortfolioManagerTrade> Trades { get; set; } = new();
}

public class PortfolioManagerTrade
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public int Quantity { get; set; }
    public decimal AllocatedCapital { get; set; }
    public decimal AllocationPercent { get; set; }
    public decimal Confidence { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal? FusionScore { get; set; }
    public decimal? FusionNewsSignal { get; set; }
    public decimal? FusionTechnicalSignal { get; set; }
    public decimal? FusionSectorSignal { get; set; }
    public bool? FusionDirectionVeto { get; set; }
    public bool? FusionIncluded { get; set; }
    public string? FusionEvidence { get; set; }
    public decimal? Pnl { get; set; }
    public decimal? PnlPercent { get; set; }
    public string EntryReasoning { get; set; } = string.Empty;
    public string? ExitReasoning { get; set; }
    public List<string> Signals { get; set; } = new();
    public string ModelProvider { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public PortfolioTradeStatus Status { get; set; } = PortfolioTradeStatus.OPEN;
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public PortfolioManagerSession? Session { get; set; }
    public TradingInstrument? Instrument { get; set; }
}
