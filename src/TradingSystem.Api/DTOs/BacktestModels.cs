namespace TradingSystem.Api.DTOs;

public record BacktestRunRequest(
    string Symbol,
    DateTime From,
    DateTime To,
    StrategyConfig Strategy
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
    double? RsiOversold
);

public record BacktestResponse(
    List<BacktestTradeResult> Trades,
    BacktestMetrics Metrics
);

public record BacktestTradeResult(
    string Id,
    DateTime EntryTime,
    double EntryPrice,
    DateTime ExitTime,
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
    List<EquityPoint> EquityCurve
);

public record EquityPoint(
    DateTime Timestamp,
    double Equity
);
