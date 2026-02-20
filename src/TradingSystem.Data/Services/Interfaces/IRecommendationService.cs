using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface IRecommendationService
{
    Task SaveAsync(Recommendation recommendation);
    Task<List<Recommendation>> GetActiveAsync();
    Task<Recommendation?> GetLatestForInstrumentAsync(string instrumentKey);
    Task ExpireOldAsync(int olderThanMinutes = 60);
}
