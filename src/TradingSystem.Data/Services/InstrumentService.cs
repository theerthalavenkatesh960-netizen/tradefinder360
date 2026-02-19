using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Data.Services;

public class InstrumentService : IInstrumentService
{
    private readonly IInstrumentRepository _repository;

    public InstrumentService(IInstrumentRepository repository)
    {
        _repository = repository;
    }

    public async Task<TradingInstrument?> GetByKeyAsync(string instrumentKey)
        => await _repository.GetByInstrumentKeyAsync(instrumentKey);

    public async Task<List<TradingInstrument>> GetActiveAsync()
        => (await _repository.GetActiveInstrumentsAsync()).ToList();

    public async Task<Dictionary<string, string>> GetKeyToSymbolMapAsync()
    {
        var instruments = await _repository.GetAllAsync();
        return instruments.ToDictionary(i => i.InstrumentKey, i => i.Symbol);
    }

    public async Task AddAsync(TradingInstrument instrument)
        => await _repository.AddAsync(instrument);

    public async Task UpdateAsync(TradingInstrument instrument)
        => await _repository.UpdateAsync(instrument);
}
