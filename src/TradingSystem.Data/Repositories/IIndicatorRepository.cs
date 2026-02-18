using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Data.Repositories;

public interface IIndicatorRepository
{
    Task SaveAsync(string instrumentKey, int timeframeMinutes, IndicatorValues indicators);
    Task<List<IndicatorSnapshot>> GetRecentAsync(string instrumentKey, int timeframeMinutes, int count);
}
