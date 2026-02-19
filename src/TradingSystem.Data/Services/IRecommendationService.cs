using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public interface IRecommendationService
{
    Task SaveAsync(Recommendation recommendation);
    Task<List<Recommendation>> GetActiveAsync();
    Task<Recommendation?> GetLatestForInstrumentAsync(string instrumentKey);
    Task ExpireOldAsync(int olderThanMinutes = 60);
}
