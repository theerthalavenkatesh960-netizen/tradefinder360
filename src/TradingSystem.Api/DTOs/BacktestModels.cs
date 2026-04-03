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
    bool? IncludeOrderBlocks = false
);

public record BacktestResponse(
    List<BacktestTradeResult> Trades,
    BacktestMetrics Metrics,
    BacktestAnnotations? Annotations = null
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

// ── Replay Annotation Models ──
public record OrbZone(
    int OrbStartIdx,
    int OrbEndIdx,
    double OrbHigh,
    double OrbLow
);

public record FvgZone(
    int FvgStartIdx,
    int FvgEndIdx,
    double FvgHigh,
    double FvgLow
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
    string EventType, // "BREAKOUT", "FVG_FORMED", "CONFLUENCE", "VOLUME_CONFIRMED", "ENGULF_CONFIRMED", "RETEST"
    string Description
);

public record BacktestAnnotations(
    OrbZone? OrbZone = null,
    List<FvgZone>? FvgZones = null,
    List<OrderBlockZone>? ObZones = null,
    ReplayEventData? RetraceEvent = null,
    ReplayEventData? EngulfingEvent = null,
    // Legacy support
    List<OrbAnnotation>? Orbs = null,
    List<FvgAnnotation>? Fvgs = null,
    List<OrderBlockAnnotation>? OrderBlocks = null,
    List<SignalEventAnnotation>? Events = null
);
