using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface IInstrumentService
{
    Task<TradingInstrument?> GetByKeyAsync(string instrumentKey);
    Task<TradingInstrument?> GetBySymbolAsync(string symbol);
    Task<List<TradingInstrument>> GetActiveAsync();
    Task<Dictionary<string, string>> GetKeyToSymbolMapAsync();
    Task AddAsync(TradingInstrument instrument);
    Task UpdateAsync(TradingInstrument instrument);
    Task<List<Sector>> GetSectorsAsync();
}
