using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface IInstrumentService
{
    Task<TradingInstrument?> GetByIdAsync(int id);
    Task<TradingInstrument?> GetByKeyAsync(string instrumentKey);
    Task<TradingInstrument?> GetBySymbolAsync(string symbol);
    Task<List<TradingInstrument>> GetActiveAsync();
    Task<Dictionary<int, string>> GetIdToSymbolMapAsync();
    Task AddAsync(TradingInstrument instrument);
    Task UpdateAsync(TradingInstrument instrument);
    Task<List<Sector>> GetSectorsAsync();
}
