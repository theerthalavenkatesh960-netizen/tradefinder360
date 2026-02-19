using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public interface IScanService
{
    Task SaveAsync(ScanSnapshot snapshot);
    Task<List<ScanSnapshot>> GetTopAsync(int minScore, int limit);
}
