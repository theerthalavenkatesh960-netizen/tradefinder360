namespace TradingSystem.Api.DTOs;

public class RadarItemDto
{
    public int instrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string MarketState { get; set; } = string.Empty;
    public int SetupScore { get; set; }
    public string QualityLabel { get; set; } = string.Empty;
    public string Bias { get; set; } = string.Empty;
    public decimal LastClose { get; set; }
    public decimal ATR { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class RadarResponseDto
{
    public List<RadarItemDto> Items { get; set; } = new();
    public int TotalScanned { get; set; }
    public int HighQuality { get; set; }
    public int Watchlist { get; set; }
    public DateTime ScannedAt { get; set; }
}
