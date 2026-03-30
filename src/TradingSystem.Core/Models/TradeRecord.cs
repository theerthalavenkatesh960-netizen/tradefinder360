using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace TradingSystem.Core.Models;

public class TradeRecord
{
    public long Id { get; set; }
    public int InstrumentId { get; set; }
    public string TradeType { get; set; } = string.Empty;
    public DateTime EntryTime { get; set; }
    public DateTime? ExitTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal ATRAtEntry { get; set; }
    public string? OptionSymbol { get; set; }
    public decimal? OptionStrike { get; set; }
    public decimal? OptionEntryPrice { get; set; }
    public decimal? OptionExitPrice { get; set; }
    public string EntryReason { get; set; } = string.Empty;
    public string? ExitReason { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal PnL { get; set; }
    public decimal PnLPercent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public TradingInstrument? Instrument { get; set; }

    // Store indicators as JSON
    public string? EntryIndicatorsJson { get; set; }
    public string? ExitIndicatorsJson { get; set; }

    [NotMapped]
    public Dictionary<string, decimal>? EntryIndicators
    {
        get => string.IsNullOrEmpty(EntryIndicatorsJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, decimal>>(EntryIndicatorsJson);
        set => EntryIndicatorsJson = value == null
            ? null
            : JsonSerializer.Serialize(value);
    }
}
