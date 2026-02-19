using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public interface IInstrumentService
{
    Task<TradingInstrument?> GetByKeyAsync(string instrumentKey);
    Task<List<TradingInstrument>> GetActiveAsync();
    Task<Dictionary<string, string>> GetKeyToSymbolMapAsync();
    Task AddAsync(TradingInstrument instrument);
    Task UpdateAsync(TradingInstrument instrument);
}
