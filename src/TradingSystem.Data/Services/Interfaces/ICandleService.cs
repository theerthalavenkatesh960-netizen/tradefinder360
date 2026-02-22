using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface ICandleService
{
    Task SaveAsync(int instrumentId, Candle candle);
    Task SaveBatchAsync(int instrumentId, List<Candle> candles);
    Task<List<Candle>> GetRecentAsync(int instrumentId, int timeframeMinutes, int count);
    Task<List<Candle>> GetRangeAsync(int instrumentId, int timeframeMinutes, DateTime startTime, DateTime endTime);
}
