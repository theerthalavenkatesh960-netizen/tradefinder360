namespace TradingSystem.Upstox.Models;

public class UpstoxConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.upstox.com/v2";
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
    public int RateLimitPerSecond { get; set; } = 10;
}
