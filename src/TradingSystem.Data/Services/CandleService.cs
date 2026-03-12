using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;
using Microsoft.Extensions.Logging;

namespace TradingSystem.Data.Services;

public class CandleService : ICandleService
{
    private readonly IMarketCandleRepository _candleRepository;
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<CandleService> _logger;

    public CandleService(
        IMarketCandleRepository candleRepository,
        IInstrumentRepository instrumentRepository,
        UpstoxClient upstoxClient,
        ILogger<CandleService> logger)
    {
        _candleRepository = candleRepository;
        _instrumentRepository = instrumentRepository;
        _upstoxClient = upstoxClient;
        _logger = logger;
    }

    public async Task SaveAsync(int instrumentId, Candle candle)
    {
        var marketCandle = ToMarketCandle(instrumentId, candle);
        await _candleRepository.AddAsync(marketCandle);
    }

    public async Task SaveBatchAsync(int instrumentId, List<Candle> candles)
    {
        var marketCandles = candles.Select(c => ToMarketCandle(instrumentId, c)).ToList();
        await _candleRepository.BulkUpsertAsync(marketCandles);
    }

    public async Task<List<Candle>> GetCandlesAsync(int instrumentId, int timeframeMinutes, DateTime fromDate, DateTime toDate)
    {
        // Check for missing data ranges
        var missingRanges = await _candleRepository.GetMissingDataRangesAsync(
            instrumentId, 
            fromDate, 
            toDate);

        // Fetch missing data from Upstox API if needed
        if (missingRanges.Any())
        {
            _logger.LogInformation(
                "Found {Count} missing data ranges for instrument {InstrumentId}. Fetching from Upstox API...",
                missingRanges.Count,
                instrumentId);

            var instrument = await _instrumentRepository.GetByIdAsync(instrumentId);
            if (instrument != null)
            {
                await FetchAndStoreMissingDataAsync(instrument, missingRanges);
            }
        }

        // Repository handles aggregation per day, never mixing days
        var marketCandles = await _candleRepository.GetByInstrumentIdAsync(
            instrumentId,
            timeframeMinutes,
            fromDate,
            toDate);

        return marketCandles.Select(c => c.ToCandle()).ToList();
    }

    private async Task FetchAndStoreMissingDataAsync(
        TradingInstrument instrument, 
        List<DateRange> missingRanges)
    {
        foreach (var range in missingRanges)
        {
            try
            {
                _logger.LogInformation(
                    "Fetching data for {InstrumentKey} from {FromDate} to {ToDate}",
                    instrument.InstrumentKey,
                    range.FromDate,
                    range.ToDate);

                // Fetch 1-minute candles from Upstox
                var candles = await _upstoxClient.GetHistoricalCandlesAsync(
                    instrument.InstrumentKey,
                    "1minute",
                    range.FromDate,
                    range.ToDate);

                if (candles.Any())
                {
                    // Convert to MarketCandles
                    var marketCandles = candles.Select(c => new MarketCandle
                    {
                        InstrumentId = instrument.Id,
                        TimeframeMinutes = 1,
                        Timestamp = c.Timestamp,
                        Open = c.Open,
                        High = c.High,
                        Low = c.Low,
                        Close = c.Close,
                        Volume = c.Volume
                    }).ToList();

                    // Store in database
                    await _candleRepository.BulkUpsertAsync(marketCandles);

                    _logger.LogInformation(
                        "Successfully fetched and stored {Count} candles for {InstrumentKey}",
                        candles.Count,
                        instrument.InstrumentKey);
                }
                else
                {
                    _logger.LogWarning(
                        "No data returned from Upstox for {InstrumentKey} in range {FromDate} to {ToDate}",
                        instrument.InstrumentKey,
                        range.FromDate,
                        range.ToDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error fetching missing data for {InstrumentKey} from {FromDate} to {ToDate}",
                    instrument.InstrumentKey,
                    range.FromDate,
                    range.ToDate);
                // Continue with other ranges even if one fails
            }
        }
    }

    public async Task<List<Candle>> GetRecentCandlesAsync(int instrumentId, int timeframeMinutes, int daysBack = 30)
    {
        var toDate = DateTime.Today.AddDays(1); // Include today
        var fromDate = DateTime.Today.AddDays(-daysBack);

        return await GetCandlesAsync(instrumentId, timeframeMinutes, fromDate, toDate);
    }

    public async Task<Candle?> GetLatestCandleAsync(int instrumentId, int timeframeMinutes)
    {
        var marketCandle = await _candleRepository.GetLatestCandleAsync(instrumentId, timeframeMinutes);
        return marketCandle?.ToCandle();
    }

    private static MarketCandle ToMarketCandle(int instrumentId, Candle candle) => new()
    {
        InstrumentId = instrumentId,
        TimeframeMinutes = candle.TimeframeMinutes,
        Timestamp = candle.Timestamp, // Already in IST
        Open = candle.Open,
        High = candle.High,
        Low = candle.Low,
        Close = candle.Close,
        Volume = candle.Volume,
        CreatedAt = DateTime.UtcNow
    };
}
