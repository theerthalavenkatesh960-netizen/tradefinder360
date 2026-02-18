using TradingSystem.Core.Models;

namespace TradingSystem.Upstox;

public class UpstoxMarketDataService
{
    private readonly UpstoxClient _client;

    public UpstoxMarketDataService(UpstoxClient client)
    {
        _client = client;
    }

    public async Task<List<Candle>> FetchHistoricalDataAsync(
        string instrumentKey,
        int timeframeMinutes,
        DateTime startDate,
        DateTime endDate)
    {
        var interval = ConvertTimeframeToInterval(timeframeMinutes);
        return await _client.GetHistoricalCandlesAsync(instrumentKey, interval, startDate, endDate);
    }

    public async Task<decimal?> FetchLivePriceAsync(string instrumentKey)
    {
        return await _client.GetLivePrice(instrumentKey);
    }

    public async Task<Candle?> FetchLatestCandleAsync(string instrumentKey, int timeframeMinutes)
    {
        var endDate = DateTime.Now;
        var startDate = endDate.AddDays(-1);

        var candles = await FetchHistoricalDataAsync(instrumentKey, timeframeMinutes, startDate, endDate);

        return candles.LastOrDefault();
    }

    private string ConvertTimeframeToInterval(int timeframeMinutes)
    {
        return timeframeMinutes switch
        {
            1 => "1minute",
            5 => "5minute",
            15 => "15minute",
            30 => "30minute",
            60 => "60minute",
            1440 => "1day",
            _ => "15minute"
        };
    }
}
