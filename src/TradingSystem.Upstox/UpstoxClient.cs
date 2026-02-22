using System.Net.Http.Headers;
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

        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void SetAccessToken(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        }
    }

    public async Task InitializeWithStoredTokenAsync(Func<Task<string?>> getTokenFunc)
    {
        var token = await getTokenFunc();
        if (!string.IsNullOrWhiteSpace(token))
        {
            SetAccessToken(token);
        }
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

        var url = $"historical-candle/{instrumentKey}/{interval}/{toDate:yyyy-MM-dd}/{fromDate:yyyy-MM-dd}";

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
            catch (HttpRequestException ex) when (retry < _config.MaxRetries - 1)
            {

                await Task.Delay(_config.RetryDelayMs * (retry + 1));
            }
        }

        return new List<Candle>();
    }

    public async Task<Dictionary<string, InstrumentPrice>> GetQuotesAsync(string commaSeparatedKeys)
    {
        await WaitForRateLimit();

        var url = $"market-quote/quotes?instrument_key={commaSeparatedKeys}";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<UpstoxQuoteResponse>();

            if (result?.Data == null || !result.Data.Any())
                return new Dictionary<string, InstrumentPrice>();

            var quotes = new Dictionary<string, InstrumentPrice>();

            foreach (var (key, quoteData) in result.Data)
            {
                if (quoteData?.Ohlc == null || string.IsNullOrEmpty(quoteData.Symbol))
                    continue;

                var instrumentToken = quoteData.Instrument_Token ?? string.Empty;
                if (string.IsNullOrEmpty(instrumentToken))
                    continue;

                var price = new InstrumentPrice
                {
                    Timestamp = quoteData.Timestamp == default ? DateTime.UtcNow : quoteData.Timestamp,
                    Open = quoteData.Ohlc.Open,
                    High = quoteData.Ohlc.High,
                    Low = quoteData.Ohlc.Low,
                    Close = quoteData.Ohlc.Close,
                    Volume = quoteData.Volume,
                    Timeframe = "1D"
                };

                quotes[instrumentToken] = price;
            }

            return quotes;
        }
        catch (Exception)
        {
            return new Dictionary<string, InstrumentPrice>();
        }
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

    public async Task<TokenResponse> FetchTokenFromUpstoxAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new Exception("Upstox authorization code not available. Login first.");

        var form = new Dictionary<string, string>
        {
            {"code", code},
            {"client_id", _config.ClientId},
            {"client_secret", _config.ClientSecret},
            {"redirect_uri", _config.RedirectUri},
            {"grant_type", "authorization_code"}
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "login/authorization/token") { Content = new FormUrlEncodedContent(form) };

        var response = await _httpClient.SendAsync(request);

        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"Upstox Token API Failed.\n" +
                $"Status: {(int)response.StatusCode} {response.StatusCode}\n" +
                $"Response: {responseContent}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(
            responseContent,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            throw new Exception($"Upstox returned invalid token response: {responseContent}");

        SetAccessToken(tokenResponse.AccessToken);

        return tokenResponse;
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
                    Timestamp = ((JsonElement)candle[0]).GetDateTimeOffset().UtcDateTime,
                    Open = ((JsonElement)candle[1]).GetDecimal(),
                    High = ((JsonElement)candle[2]).GetDecimal(),
                    Low = ((JsonElement)candle[3]).GetDecimal(),
                    Close = ((JsonElement)candle[4]).GetDecimal(),
                    Volume = ((JsonElement)candle[5]).GetInt64(),
                    TimeframeMinutes = timeframeMinutes
                };

                candles.Add(parsedCandle);
            }
            catch (Exception ex)
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
