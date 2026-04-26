using TradingSystem.Api.DTOs;

namespace TradingSystem.Api.Services;

public interface IPortfolioManagerService
{
    Task<PortfolioSessionSummaryDto> CreateSessionAsync(string userId, CreatePortfolioSessionRequest request, CancellationToken cancellationToken = default);
    Task<PortfolioSessionSummaryDto?> UpdateSessionAsync(string userId, long sessionId, UpdatePortfolioSessionRequest request, CancellationToken cancellationToken = default);
    Task<PortfolioSessionSummaryDto?> CloneSessionAsync(string userId, long sourceSessionId, ClonePortfolioSessionRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteSessionAsync(string userId, long sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PortfolioSessionSummaryDto>> GetSessionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<PortfolioSessionDetailDto?> GetSessionDetailAsync(string userId, long sessionId, CancellationToken cancellationToken = default);
    Task<PortfolioRunResponseDto?> RunSessionAsync(string userId, long sessionId, CancellationToken cancellationToken = default);
    Task<PortfolioSessionSummaryDto?> SetScheduledStateAsync(string userId, long sessionId, bool isRunning, CancellationToken cancellationToken = default);
    Task<byte[]?> ExportSessionCsvAsync(string userId, long sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PortfolioNewsItemDto>> GetSessionNewsAsync(string userId, long sessionId, int hoursBack = 24, int limit = 100, CancellationToken cancellationToken = default);
    Task<PortfolioEventTriggerResultDto> TriggerEventDrivenRunsAsync(CancellationToken cancellationToken = default);
}
