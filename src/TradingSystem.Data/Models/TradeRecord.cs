using Postgrest.Attributes;
using Postgrest.Models;

namespace TradingSystem.Data.Models;

[Table("trades")]
public class TradeRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("entry_time")]
    public DateTime EntryTime { get; set; }

    [Column("exit_time")]
    public DateTime? ExitTime { get; set; }

    [Column("direction")]
    public string Direction { get; set; } = string.Empty;

    [Column("state")]
    public string State { get; set; } = string.Empty;

    [Column("spot_entry_price")]
    public decimal SpotEntryPrice { get; set; }

    [Column("spot_exit_price")]
    public decimal? SpotExitPrice { get; set; }

    [Column("option_symbol")]
    public string OptionSymbol { get; set; } = string.Empty;

    [Column("option_strike")]
    public decimal OptionStrike { get; set; }

    [Column("option_entry_price")]
    public decimal OptionEntryPrice { get; set; }

    [Column("option_exit_price")]
    public decimal? OptionExitPrice { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; }

    [Column("stop_loss")]
    public decimal StopLoss { get; set; }

    [Column("target")]
    public decimal Target { get; set; }

    [Column("atr_at_entry")]
    public decimal ATRAtEntry { get; set; }

    [Column("entry_reason")]
    public string EntryReason { get; set; } = string.Empty;

    [Column("exit_reason")]
    public string? ExitReason { get; set; }

    [Column("pnl")]
    public decimal? PnL { get; set; }

    [Column("pnl_percent")]
    public decimal? PnLPercent { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("candles")]
public class CandleRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    [Column("open")]
    public decimal Open { get; set; }

    [Column("high")]
    public decimal High { get; set; }

    [Column("low")]
    public decimal Low { get; set; }

    [Column("close")]
    public decimal Close { get; set; }

    [Column("volume")]
    public long Volume { get; set; }

    [Column("timeframe_minutes")]
    public int TimeframeMinutes { get; set; }
}

[Table("market_states")]
public class MarketStateRecord : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    [Column("state")]
    public string State { get; set; } = string.Empty;

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;

    [Column("adx")]
    public decimal ADX { get; set; }

    [Column("rsi")]
    public decimal RSI { get; set; }

    [Column("macd")]
    public decimal MACD { get; set; }
}
