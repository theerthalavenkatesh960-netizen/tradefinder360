namespace TradingSystem.Api.DTOs;

public class InstrumentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;

    // market data
    public decimal? Price { get; set; }
    public long? Volume { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }

    // derived/analysis
    public string? Trend { get; set; }

    // recommendation information
    public decimal? EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? ExpectedProfit { get; set; }
    public int? Confidence { get; set; }
}
