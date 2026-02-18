using System.Net.Http.Json;
using System.Text.Json;
using TradingSystem.Core.Models;
using TradingSystem.Upstox.Models;

namespace TradingSystem.Upstox;

public class UpstoxClient
{
    private readonly HttpClient _httpClient;
    private readonly UpstoxConfig _config;
    private readonly SemaphoreSlim _rateLimiter;
    private DateTime _lastRequestTime = DateTime.MinValue;

    public UpstoxClient(HttpClient httpClient, UpstoxConfig config)
    {
        _httpClient = httpClient;
        _config = config;
        _rateLimiter = new SemaphoreSlim(_config.RateLimitPerSecond);

        _httpClient.BaseAddress = new Uri(_config.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.AccessToken}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    private async Task WaitForRateLimit()
    {
        await _rateLimiter.WaitAsync();

        var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
        if (timeSinceLastRequest.TotalMilliseconds < 1000.0 / _config.RateLimitPerSecond)
        {
            var delay = (int)(1000.0 / _config.RateLimitPerSecond - timeSinceLastRequest.TotalMilliseconds);
            await Task.Delay(delay);
        }

        _lastRequestTime = DateTime.UtcNow;
        _rateLimiter.Release();
    }

    public async Task<List<Candle>> GetHistoricalCandlesAsync(
        string instrumentKey,
        string interval,
        DateTime fromDate,
        DateTime toDate)
    {
        await WaitForRateLimit();

        var url = $"/historical-candle/{instrumentKey}/{interval}/{fromDate:yyyy-MM-dd}/{toDate:yyyy-MM-dd}";

        for (int retry = 0; retry < _config.MaxRetries; retry++)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<UpstoxCandleResponse>();

                if (result?.Data?.Candles == null || result.Data.Candles.Count == 0)
                    return new List<Candle>();

                return ParseCandles(result.Data.Candles, interval);
            }
            catch (HttpRequestException) when (retry < _config.MaxRetries - 1)
            {
                await Task.Delay(_config.RetryDelayMs * (retry + 1));
            }
        }

        return new List<Candle>();
    }

    public async Task<decimal?> GetLivePrice(string instrumentKey)
    {
        await WaitForRateLimit();

        var url = $"/market-quote/ltp?instrument_key={instrumentKey}";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (result.TryGetProperty("data", out var data) &&
                data.TryGetProperty(instrumentKey, out var instrumentData) &&
                instrumentData.TryGetProperty("last_price", out var lastPrice))
            {
                return lastPrice.GetDecimal();
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    public async Task<List<UpstoxInstrumentData>> GetInstrumentsAsync(string exchange)
    {
        await WaitForRateLimit();

        var url = $"/market-quote/instruments?exchange={exchange}";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<UpstoxInstrumentResponse>();

            return result?.Data ?? new List<UpstoxInstrumentData>();
        }
        catch (Exception)
        {
            return new List<UpstoxInstrumentData>();
        }
    }

    private List<Candle> ParseCandles(List<List<object>> candleData, string interval)
    {
        var candles = new List<Candle>();
        int timeframeMinutes = ParseInterval(interval);

        foreach (var candle in candleData)
        {
            if (candle.Count < 6) continue;

            try
            {
                var timestamp = candle[0].ToString();
                var parsedCandle = new Candle
                {
                    Timestamp = DateTime.Parse(timestamp!),
                    Open = Convert.ToDecimal(candle[1]),
                    High = Convert.ToDecimal(candle[2]),
                    Low = Convert.ToDecimal(candle[3]),
                    Close = Convert.ToDecimal(candle[4]),
                    Volume = Convert.ToInt64(candle[5]),
                    TimeframeMinutes = timeframeMinutes
                };

                candles.Add(parsedCandle);
            }
            catch (Exception)
            {
                continue;
            }
        }

        return candles;
    }

    private int ParseInterval(string interval)
    {
        return interval switch
        {
            "1minute" => 1,
            "5minute" => 5,
            "15minute" => 15,
            "30minute" => 30,
            "60minute" => 60,
            "1day" => 1440,
            _ => 15
        };
    }
}
