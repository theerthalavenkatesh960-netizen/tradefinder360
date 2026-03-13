using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Data.Services;

public class InstrumentService : IInstrumentService
{
    private readonly IInstrumentRepository _repository;
    private readonly ISectorRepository _sectorRepository;

    public InstrumentService(IInstrumentRepository repository, ISectorRepository sectorRepository)
    {
        _repository = repository;
        _sectorRepository = sectorRepository;
    }

    public async Task<TradingInstrument?> GetByIdAsync(int id)
        => await _repository.GetByIdAsync(id);
    public async Task<TradingInstrument?> GetByKeyAsync(string instrumentKey)
        => await _repository.GetByInstrumentKeyAsync(instrumentKey);
    
    public async Task<TradingInstrument?> GetBySymbolAsync(string symbol)
        => await _repository.GetBySymbolAsync(symbol);

    public async Task<List<TradingInstrument>> GetActiveAsync()
        => (await _repository.GetActiveInstrumentsAsync()).ToList();

    public async Task<Dictionary<int, string>> GetIdToSymbolMapAsync()
    {
        var instruments = await _repository.GetAllAsync();
        return instruments.ToDictionary(i => i.Id, i => i.Symbol);
    }

    public async Task AddAsync(TradingInstrument instrument)
        => await _repository.AddAsync(instrument);

    public async Task UpdateAsync(TradingInstrument instrument)
        => await _repository.UpdateAsync(instrument);

    public async Task<List<Sector>> GetSectorsAsync()
        => (await _sectorRepository.GetAllAsync()).ToList();
}
