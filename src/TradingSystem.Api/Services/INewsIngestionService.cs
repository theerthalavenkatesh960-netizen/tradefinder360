using TradingSystem.Core.Models;

namespace TradingSystem.Api.Services;

public interface INewsIngestionService
{
    Task<int> IngestMorningNewsAsync(CancellationToken cancellationToken = default);
    Task<int> IngestHourlyNewsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NewsArticle>> GetRecentNewsAsync(int hoursBack = 24, int limit = 100, CancellationToken cancellationToken = default);
}
