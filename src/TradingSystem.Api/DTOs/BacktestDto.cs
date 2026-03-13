namespace TradingSystem.Api.DTOs;

public class BacktestRequest
{
    public string Strategy { get; set; } = string.Empty; // MOMENTUM, BREAKOUT, etc.
    public int InstrumentId { get; set; }
    public int TimeframeMinutes { get; set; } = 15;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal InitialCapital { get; set; } = 100000m;
    public decimal PositionSizePercent { get; set; } = 10m;
    public decimal CommissionPercent { get; set; } = 0.05m;
    public bool UseStopLoss { get; set; } = true;
    public bool UseTarget { get; set; } = true;
}

public class BacktestResultDto
{
    public string Strategy { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int TimeframeMinutes { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalBars { get; set; }
    public decimal InitialCapital { get; set; }
    public decimal FinalCapital { get; set; }
    
    // Key Performance Indicators
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal AverageReturn { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public decimal ProfitFactor { get; set; }
    
    // Return Metrics
    public decimal TotalReturn { get; set; }
    public decimal TotalReturnPercent { get; set; }
    public decimal AverageReturnPercent { get; set; }
    public decimal AverageWinPercent { get; set; }
    public decimal AverageLossPercent { get; set; }
    
    // Risk Metrics
    public decimal SharpeRatio { get; set; }
    public decimal SortinoRatio { get; set; }
    
    // Additional Metrics
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal AverageBarsHeld { get; set; }
    public decimal TotalCommission { get; set; }
    public int ConsecutiveWins { get; set; }
    public int ConsecutiveLosses { get; set; }
    public DateTime MaxDrawdownDate { get; set; }
    
    // Optional: Trade history (can be filtered for performance)
    public List<BacktestTradeDto>? Trades { get; set; }
    
    // Optional: Equity curve (can be sampled for performance)
    public Dictionary<DateTime, decimal>? EquityCurve { get; set; }
}

public class BacktestTradeDto
{
    public int TradeNumber { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public int Quantity { get; set; }
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public string ExitReason { get; set; } = string.Empty;
    public decimal Commission { get; set; }
    public int BarsHeld { get; set; }
}