namespace TradingSystem.Core.Models;

/// <summary>
/// Configuration for running a backtest
/// </summary>
public class BacktestConfig
{
    public StrategyType Strategy { get; set; }
    public int InstrumentId { get; set; }
    public int TimeframeMinutes { get; set; } = 15;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal InitialCapital { get; set; } = 100000m;
    public decimal PositionSizePercent { get; set; } = 10m; // % of capital per trade
    public decimal CommissionPercent { get; set; } = 0.05m; // % commission per trade
    public bool UseStopLoss { get; set; } = true;
    public bool UseTarget { get; set; } = true;
}

/// <summary>
/// Individual trade executed during backtest
/// </summary>
public class BacktestTrade
{
    public int TradeNumber { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public string Direction { get; set; } = string.Empty; // BUY/SELL
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public int Quantity { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public string ExitReason { get; set; } = string.Empty; // TARGET_HIT, STOP_LOSS, SIGNAL_REVERSAL, END_OF_PERIOD
    public decimal Commission { get; set; }
    public int BarsHeld { get; set; }
}

/// <summary>
/// Comprehensive backtest results with performance metrics
/// </summary>
public class BacktestResult
{
    public StrategyType Strategy { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public int TimeframeMinutes { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalBars { get; set; }
    public decimal InitialCapital { get; set; }
    public decimal FinalCapital { get; set; }
    
    // Trade Statistics
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; } // Percentage
    
    // Return Metrics
    public decimal TotalReturn { get; set; } // Absolute
    public decimal TotalReturnPercent { get; set; }
    public decimal AverageReturn { get; set; }
    public decimal AverageReturnPercent { get; set; }
    public decimal AverageWinPercent { get; set; }
    public decimal AverageLossPercent { get; set; }
    
    // Risk Metrics
    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public DateTime MaxDrawdownDate { get; set; }
    public decimal ProfitFactor { get; set; } // Gross profit / Gross loss
    public decimal SharpeRatio { get; set; }
    public decimal SortinoRatio { get; set; }
    
    // Additional Metrics
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal AverageBarsHeld { get; set; }
    public decimal TotalCommission { get; set; }
    public int ConsecutiveWins { get; set; }
    public int ConsecutiveLosses { get; set; }
    
    // Trade History
    public List<BacktestTrade> Trades { get; set; } = new();
    
    // Equity Curve (Date -> Capital)
    public Dictionary<DateTime, decimal> EquityCurve { get; set; } = new();
}