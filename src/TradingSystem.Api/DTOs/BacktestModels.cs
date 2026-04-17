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
    int OrbStartIdx,  // candle index of first ORB candle
    int OrbEndIdx,    // candle index of last candle of that trading day
    double OrbHigh,
    double OrbLow,
    string? TradeNotTakenReason = null  // non-null when no trade was entered that day
);

public record FvgZone(
    int FvgStartIdx,  // candle index where FVG was detected
    int FvgEndIdx,    // candle index of last candle of that trading day
    double FvgHigh,
    double FvgLow,
    string? Direction = null  // "BULLISH" or "BEARISH"
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
    // Multi-day ORB zones — one OrbZone per trading day
    List<OrbZone>? OrbZones = null,
    List<FvgZone>? FvgZones = null,
    List<OrderBlockZone>? ObZones = null,
    ReplayEventData? RetraceEvent = null,
    ReplayEventData? EngulfingEvent = null,
    // Raw annotation data (timestamp + price, used to build index-based zones)
    List<OrbAnnotation>? Orbs = null,
    List<FvgAnnotation>? Fvgs = null,
    List<OrderBlockAnnotation>? OrderBlocks = null,
    // All signal events including TRADE_NOT_TAKEN
    List<SignalEventAnnotation>? Events = null
);
