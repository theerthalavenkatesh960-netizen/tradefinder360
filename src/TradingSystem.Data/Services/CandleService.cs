using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Services;

public class CandleService : ICandleService
{
    private readonly IMarketCandleRepository _candleRepository;

    public CandleService(IMarketCandleRepository candleRepository)
    {
        _candleRepository = candleRepository;
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
        // Repository handles aggregation per day, never mixing days
        var marketCandles = await _candleRepository.GetByInstrumentIdAsync(
            instrumentId,
            timeframeMinutes,
            fromDate,
            toDate);

        return marketCandles.Select(c => c.ToCandle()).ToList();
    }

    public async Task<List<Candle>> GetRecentCandlesAsync(int instrumentId, int timeframeMinutes, int daysBack = 30)
    {
        var toDate = DateTime.Today.AddDays(1); // Include today
        var fromDate = DateTime.Today.AddDays(-daysBack);

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
        CreatedAt = DateTime.UtcNow
    };
}
