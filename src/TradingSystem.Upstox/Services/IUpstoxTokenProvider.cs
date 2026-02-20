namespace TradingSystem.Upstox.Services;

public interface IUpstoxTokenProvider
{
    Task<string?> GetAccessTokenAsync();
}
