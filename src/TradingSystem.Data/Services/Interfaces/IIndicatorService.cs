using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Data.Services.Interfaces;

public interface IIndicatorService
{
    Task SaveAsync(string instrumentKey, int timeframeMinutes, IndicatorValues indicators);
    Task<IndicatorSnapshot?> GetLatestAsync(string instrumentKey, int timeframeMinutes);
    Task<List<IndicatorSnapshot>> GetRecentAsync(string instrumentKey, int timeframeMinutes, int count);
}
