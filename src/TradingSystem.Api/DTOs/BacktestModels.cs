namespace TradingSystem.Api.DTOs;

public record BacktestRunRequest(
    string Symbol,
    DateTime From,
    DateTime To,
    StrategyConfig Strategy,
    double? InitialCapital
);

public record StrategyConfig(
    string Name,
    StrategyParams Params
);

public record StrategyParams(
    int Timeframe,
    double RiskPercent,
    string StopLossType,
    string TargetType,
    double? RrRatio,
    double? SlPercent,
    int? FastEMA,
    int? SlowEMA,
    double? RsiOverbought,
    double? RsiOversold,
    bool? IncludeOrderBlocks = false,
    // Enhanced EMA Crossover params (all optional - zero impact on existing strategies)
    string? EmaFilterType = null,
    bool? UseTripleEma = null,
    int? MiddleEma = null,
    int? EmaRsiPeriod = null,
    int? EmaRsiMidline = null,
    int? VolumeAvgPeriod = null,
    double? VolumeMultiplier = null,
    int? SRLookbackPeriod = null,
    double? SRBuffer = null,
    string[]? AllowedPatterns = null,
    int? CandleLookback = null,
    string? EmaSlType = null,
    double? EmaSlValue = null,
    int? EmaAtrPeriod = null,
    double? TargetRRR = null,
    int? MaxHoldingPeriods = null,
    string? TradeDirection = null,
    string? EmaTimeframeMode = null,     // INTRADAY | SWING | BOTH
    // Unified strategy mode selectors
    string? EmaMode = null,              // CROSSOVER | PULLBACK | SPEED | PULLBACK_SPEED
    string? OrbMode = null,              // CLASSIC | FVG_RETEST
    string? SmcMode = null               // FVG_OB (reserved for future expansion)
);

// Result record returned by each IBacktestStrategy class
public record BacktestStrategyResult(
    List<BacktestTradeResult> Trades,
    BacktestAnnotations? Annotations = null
);

public record BacktestResponse(
    List<BacktestTradeResult> Trades,
    BacktestMetrics Metrics,
    BacktestAnnotations? Annotations = null,
    BacktestComparison? Comparison = null
);

public record BacktestComparisonProfile(
    string Mode,
    List<BacktestTradeResult> Trades,
    BacktestMetrics Metrics,
    BacktestAnnotations? Annotations = null
);

public record BacktestComparison(
    BacktestComparisonProfile Intraday,
    BacktestComparisonProfile Swing
);

public record BacktestTradeResult(
    string Id,
    DateTime EntryTime,
    double EntryPrice,
    DateTime? ExitTime,
    double ExitPrice,
    double StopLoss,
    double Target,
    int Quantity,
    double Pnl,
    double PnlPercent,
    string TradeType
);

public record BacktestMetrics(
    int TotalTrades,
    double WinRate,
    double TotalPnl,
    double MaxDrawdown,
    double AvgRR,
    int WinningTrades,
    int LosingTrades,
    double TotalReturn,
    double ProfitFactor,
    List<EquityPoint> EquityCurve,
    double InitialCapital,
    double FinalCapital,
    double AvgWinPnl,
    double AvgLossPnl
);

public record EquityPoint(
    DateTime Timestamp,
    double Equity
);

// Replay Annotation Models
public record OrbZone(
    int OrbStartIdx,
    int OrbEndIdx,
    double OrbHigh,
    double OrbLow,
    string? TradeNotTakenReason = null
);

public record FvgZone(
    int FvgStartIdx,
    int FvgEndIdx,
    double FvgHigh,
    double FvgLow,
    string? Direction = null
);

public record OrderBlockZone(
    int ObStartIdx,
    int ObEndIdx,
    double ObHigh,
    double ObLow
);

public record ReplayEventData(
    int CandleIdx,
    double Price
);

public record OrbAnnotation(
    DateTime Timestamp,
    double High,
    double Low
);

public record FvgAnnotation(
    DateTime FormedAt,
    double GapLow,
    double GapHigh,
    string Direction
);

public record OrderBlockAnnotation(
    DateTime Timestamp,
    double High,
    double Low,
    string Direction
);

public record SignalEventAnnotation(
    DateTime Timestamp,
    string EventType,
    string Description
);

public record BacktestAnnotations(
    List<OrbZone>? OrbZones = null,
    List<FvgZone>? FvgZones = null,
    List<OrderBlockZone>? ObZones = null,
    ReplayEventData? RetraceEvent = null,
    ReplayEventData? EngulfingEvent = null,
    List<OrbAnnotation>? Orbs = null,
    List<FvgAnnotation>? Fvgs = null,
    List<OrderBlockAnnotation>? OrderBlocks = null,
    List<SignalEventAnnotation>? Events = null
);
