using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface IScanService
{
    Task SaveAsync(ScanSnapshot snapshot);
    Task<List<ScanSnapshot>> GetTopAsync(int minScore, int limit);
    Task<ScanSnapshot?> GetLatestSnapshotAsync(int instrumentId);
}
