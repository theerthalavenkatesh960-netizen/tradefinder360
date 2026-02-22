using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface ICandleService
{
    Task SaveAsync(int instrumentId, Candle candle);
    Task SaveBatchAsync(int instrumentId, List<Candle> candles);
    Task<List<Candle>> GetCandlesAsync(int instrumentId, int timeframeMinutes, DateTime fromDate, DateTime toDate);
    Task<List<Candle>> GetRecentCandlesAsync(int instrumentId, int timeframeMinutes, int daysBack = 30);
    Task<Candle?> GetLatestCandleAsync(int instrumentId, int timeframeMinutes);
}
