namespace TradingSystem.Core.Models;

public enum TradeDirection
{
    CALL,
    PUT
}

public enum TradeState
{
    WAIT,
    READY,
    IN_TRADE,
    EXITED
}

public class Trade
{
    public long Id { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime? ExitTime { get; set; }
    public TradeDirection Direction { get; set; }
    public TradeState State { get; set; }

    public decimal SpotEntryPrice { get; set; }
    public decimal? SpotExitPrice { get; set; }

    public string OptionSymbol { get; set; } = string.Empty;
    public decimal OptionStrike { get; set; }
    public decimal OptionEntryPrice { get; set; }
    public decimal? OptionExitPrice { get; set; }

    public int Quantity { get; set; }

    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal ATRAtEntry { get; set; }

    public string EntryReason { get; set; } = string.Empty;
    public string? ExitReason { get; set; }

    public decimal? PnL { get; set; }
    public decimal? PnLPercent { get; set; }

    public Dictionary<string, decimal> EntryIndicators { get; set; } = new();
    public Dictionary<string, decimal>? ExitIndicators { get; set; }
}
