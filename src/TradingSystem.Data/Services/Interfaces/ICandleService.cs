using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface ICandleService
{
    Task SaveAsync(int instrumentId, Candle candle);
    Task SaveBatchAsync(int instrumentId, List<Candle> candles);
    Task<List<Candle>> GetCandlesAsync(int instrumentId, int timeframeMinutes, DateTime fromDate, DateTime toDate);
    Task<List<Candle>> GetRecentCandlesAsync(int instrumentId, int timeframeMinutes, int daysBack = 30);
    Task<Candle?> GetLatestCandleAsync(int instrumentId, int timeframeMinutes);

    /// <summary>
    /// Returns candles already stored in the database without fetching from external APIs.
    /// Use this when you only need locally available data (e.g., indicator backfill).
    /// </summary>
    Task<List<Candle>> GetCandlesFromDbAsync(int instrumentId, int timeframeMinutes, DateTime fromDate, DateTime toDate);
}
