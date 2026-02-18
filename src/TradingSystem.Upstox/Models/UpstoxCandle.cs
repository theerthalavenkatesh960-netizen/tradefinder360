using System.Text.Json.Serialization;

namespace TradingSystem.Upstox.Models;

public class UpstoxCandleResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public UpstoxCandleData? Data { get; set; }
}

public class UpstoxCandleData
{
    [JsonPropertyName("candles")]
    public List<List<object>>? Candles { get; set; }
}

public class UpstoxCandle
{
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public long OpenInterest { get; set; }
}
