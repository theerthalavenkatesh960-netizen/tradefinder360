using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories;

public interface ICandleRepository
{
    Task SaveAsync(string instrumentKey, Candle candle);
    Task SaveBatchAsync(string instrumentKey, List<Candle> candles);
    Task<List<Candle>> GetRecentAsync(string instrumentKey, int timeframeMinutes, int count);
    Task<List<Candle>> GetRangeAsync(string instrumentKey, int timeframeMinutes, DateTime startTime, DateTime endTime);
}
