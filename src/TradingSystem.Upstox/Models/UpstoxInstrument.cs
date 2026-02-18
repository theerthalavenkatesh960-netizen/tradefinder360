using System.Text.Json.Serialization;

namespace TradingSystem.Upstox.Models;

public class UpstoxInstrumentResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<UpstoxInstrumentData>? Data { get; set; }
}

public class UpstoxInstrumentData
{
    [JsonPropertyName("instrument_key")]
    public string InstrumentKey { get; set; } = string.Empty;

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = string.Empty;

    [JsonPropertyName("trading_symbol")]
    public string TradingSymbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("strike")]
    public decimal? Strike { get; set; }

    [JsonPropertyName("tick_size")]
    public decimal TickSize { get; set; }

    [JsonPropertyName("lot_size")]
    public int LotSize { get; set; }

    [JsonPropertyName("instrument_type")]
    public string InstrumentType { get; set; } = string.Empty;

    [JsonPropertyName("option_type")]
    public string? OptionType { get; set; }
}
