using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface IRecommendationService
{
    Task SaveAsync(Recommendation recommendation);
    Task<List<Recommendation>> GetActiveAsync();
    Task<Recommendation?> GetLatestForInstrumentAsync(int instrumentId);
    Task ExpireOldAsync(int olderThanMinutes = 60);
}
