using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Utilities;

namespace TradingSystem.Data.Services;

public class CandleService : ICandleService
{
    private const int MinTradingDaysGap = 3;

    private readonly IMarketCandleRepository _candleRepository;
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<CandleService> _logger;

    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

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
        // Get raw missing ranges from repository
        var rawMissingRanges = await _candleRepository.GetMissingDataRangesAsync(
            instrumentId,
            fromDate,
            toDate,
            timeframeMinutes);

        if (rawMissingRanges.Any())
        {
            var instrument = await _instrumentRepository.GetByIdAsync(instrumentId);
            if (instrument != null)
            {
                // Filter for genuine gaps with actual trading days
                var genuineRanges = GetGenuineMissingRanges(rawMissingRanges.ToList(), fromDate, toDate, timeframeMinutes);

                if (genuineRanges.Count > 0)
                {
                    _logger.LogInformation(
                        "Found {Count} genuine missing data ranges for instrument {InstrumentId} ({TF}m). Fetching from Upstox API...",
                        genuineRanges.Count,
                        instrumentId,
                        timeframeMinutes);

                    await FetchAndStoreMissingDataAsync(instrument, genuineRanges, timeframeMinutes);
                }
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

    private List<(DateTime From, DateTime To)> GetGenuineMissingRanges(
        List<DateRange> rawRanges,
        DateTime fromDate,
        DateTime toDate,
        int timeframeMinutes)
    {
        var genuine = new List<(DateTime From, DateTime To)>();

        foreach (var range in rawRanges)
        {
            // Clamp range to requested boundaries - DB gap may extend past safe toDate
            var clampedFrom = range.FromDate < fromDate ? fromDate : range.FromDate;
            var clampedTo = range.ToDate > toDate ? toDate : range.ToDate;

            if (clampedTo < clampedFrom)
                continue;

            // Count actual trading days in this range using TradingCalendar
            var tradingDays = TradingCalendar.CountTradingDays(clampedFrom, clampedTo);

            // Skip gaps with insufficient trading days
            if (tradingDays < MinTradingDaysGap)
            {
                _logger.LogDebug(
                    "Skipping gap {From:yyyy-MM-dd}→{To:yyyy-MM-dd}: only {Days} trading days (min {Min})",
                    clampedFrom, clampedTo, tradingDays, MinTradingDaysGap);
                continue;
            }

            genuine.Add((clampedFrom, clampedTo));
        }

        return genuine;
    }

    private async Task FetchAndStoreMissingDataAsync(
        TradingInstrument instrument,
        List<(DateTime From, DateTime To)> missingRanges,
        int timeframeMinutes)
    {
        // Determine API parameters based on timeframe
        var (unit, interval) = GetApiParameters(timeframeMinutes);

        foreach (var (rangeFrom, rangeTo) in missingRanges)
        {
            // Daily data: fetch entire range in one call
            // Intraday data: split into monthly batches to avoid overwhelming API
            var batches = timeframeMinutes == 1440 ? [(rangeFrom, rangeTo)] : GenerateMonthlyBatches(rangeFrom, rangeTo);

            foreach (var (batchFrom, batchTo) in batches)
            {
                try
                {
                    _logger.LogInformation(
                        "Fetching {TF}m data for {InstrumentKey} from {FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd}",
                        timeframeMinutes,
                        instrument.InstrumentKey,
                        batchFrom,
                        batchTo);

                    // Fetch candles from Upstox using V3 API
                    var candles = await _upstoxClient.GetHistoricalCandlesV3Async(
                        instrument.InstrumentKey,
                        unit,
                        interval,
                        batchFrom,
                        batchTo);

                    if (candles?.Any() == true)
                    {
                        // Convert to MarketCandles
                        var marketCandles = candles.Select(c => new MarketCandle
                        {
                            InstrumentId = instrument.Id,
                            TimeframeMinutes = timeframeMinutes,
                            Timestamp = c.Timestamp,
                            Open = c.Open,
                            High = c.High,
                            Low = c.Low,
                            Close = c.Close,
                            Volume = c.Volume,
                            CreatedAt = DateTimeOffset.UtcNow
                        }).ToList();

                        // Store in database
                        await _candleRepository.BulkUpsertAsync(marketCandles);

                        _logger.LogInformation(
                            "Successfully fetched and stored {Count} {TF}m candles for {InstrumentKey}",
                            marketCandles.Count,
                            timeframeMinutes,
                            instrument.InstrumentKey);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "No data returned from Upstox for {InstrumentKey} in range {FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd}",
                            instrument.InstrumentKey,
                            batchFrom,
                            batchTo);
                    }

                    // Small delay between batches to avoid API throttling
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error fetching missing {TF}m data for {InstrumentKey} from {FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd}",
                        timeframeMinutes,
                        instrument.InstrumentKey,
                        batchFrom,
                        batchTo);
                    // Continue with other batches even if one fails
                }
            }
        }
    }

    private static (string Unit, int Interval) GetApiParameters(int timeframeMinutes)
    {
        return timeframeMinutes switch
        {
            1 => ("minutes", 1),
            15 => ("minutes", 15),
            1440 => ("days", 1),
            _ => ("minutes", timeframeMinutes)
        };
    }

    private static List<(DateTime From, DateTime To)> GenerateMonthlyBatches(DateTime from, DateTime to)
    {
        var batches = new List<(DateTime, DateTime)>();

        // Start from the 1st of the month containing 'from'
        var monthStart = new DateTime(from.Year, from.Month, 1);

        while (monthStart <= to)
        {
            // Last day of this calendar month
            var monthEnd = new DateTime(
                monthStart.Year,
                monthStart.Month,
                DateTime.DaysInMonth(monthStart.Year, monthStart.Month));

            // Clamp end to our range ceiling
            var batchTo = monthEnd > to ? to : monthEnd;

            batches.Add((monthStart, batchTo));

            // Move to 1st of next month
            monthStart = monthStart.AddMonths(1);
        }

        return batches;
    }

    public async Task<List<Candle>> GetRecentCandlesAsync(int instrumentId, int timeframeMinutes, int daysBack = 90)
    {
        var toDate = DateTime.Today.AddDays(1); // Include today
        var fromDate = DateTime.Today.AddDays(-daysBack);

        return await GetCandlesAsync(instrumentId, timeframeMinutes, fromDate, toDate);
    }

    public async Task<List<Candle>> GetCandlesFromDbAsync(int instrumentId, int timeframeMinutes, DateTime fromDate, DateTime toDate)
    {
        var marketCandles = await _candleRepository.GetByInstrumentIdAsync(
            instrumentId,
            timeframeMinutes,
            fromDate,
            toDate);

        return marketCandles.Select(c => c.ToCandle()).ToList();
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
        CreatedAt = DateTimeOffset.UtcNow
    };
}