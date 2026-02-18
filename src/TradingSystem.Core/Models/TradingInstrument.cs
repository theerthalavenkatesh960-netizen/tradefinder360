namespace TradingSystem.Core.Models;

public enum InstrumentType
{
    INDEX,
    STOCK
}

public enum TradingMode
{
    OPTIONS,
    EQUITY
}

public class TradingInstrument
{
    public int Id { get; set; }
    public string InstrumentKey { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public InstrumentType InstrumentType { get; set; }
    public int LotSize { get; set; }
    public decimal TickSize { get; set; }
    public bool IsDerivativesEnabled { get; set; }
    public TradingMode DefaultTradingMode { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public string GetDisplayName() => $"{Exchange}:{Symbol}";
}
