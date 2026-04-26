using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

/// <summary>
/// Repository for persisting portfolio performance history records
/// Used to track performance over time for baselines and trending analysis
/// </summary>
public interface IPortfolioPerformanceHistoryRepository
{
    Task<PortfolioPerformanceHistory> AddAsync(
        PortfolioPerformanceHistory history,
        CancellationToken cancellationToken = default);

    Task<PortfolioPerformanceHistory?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all history records for a session, ordered by recorded date descending (newest first)
    /// </summary>
    Task<List<PortfolioPerformanceHistory>> GetBySessionIdAsync(
        long sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get last N history records for a session
    /// </summary>
    Task<List<PortfolioPerformanceHistory>> GetLastNForSessionAsync(
        long sessionId,
        int count = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get history for user's sessions across all sessions within date range
    /// Used for comparing performance before/after learning
    /// </summary>
    Task<List<PortfolioPerformanceHistory>> GetByUserIdAndDateRangeAsync(
        string userId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
}
