using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Data.Services.Interfaces;

public interface IIndicatorService
{
    Task SaveAsync(int instrumentId, int timeframeMinutes, IndicatorValues indicators);
    Task<IndicatorSnapshot?> GetLatestAsync(int instrumentId, int timeframeMinutes);
    Task<List<IndicatorSnapshot>> GetRecentAsync(int instrumentId, int timeframeMinutes, int count);
}
