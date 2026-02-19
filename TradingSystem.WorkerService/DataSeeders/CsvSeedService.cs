using Microsoft.EntityFrameworkCore;
using SwingLyne.Domain;
using SwingLyne.Domain.Enums;
using SwingLyne.Domain.Models;
using SwingLyne.Domain.Repositories.Interfaces;

namespace SwingLyne.Migrator.DataSeeders;

public class CsvSeedService
{
    private readonly ICommonRepository<Sector> _sectorRepo;
    private readonly ICommonRepository<Stock> _stockRepo;

    private readonly string _dataSeedPath;

    public CsvSeedService(
        ICommonRepository<Sector> sectorRepo,
        ICommonRepository<Stock> stockRepo)
    {
        _sectorRepo = sectorRepo;
        _stockRepo = stockRepo;

        _dataSeedPath = Path.Combine(AppContext.BaseDirectory, "Data");
    }

    public async Task SeedAsync()
    {
        await SeedSectorsAsync();
        await SeedStocksAsync();
    }

    // ----------------------------------------
    // SECTORS
    // ----------------------------------------
    private async Task SeedSectorsAsync()
    {
        var file = Path.Combine(_dataSeedPath, "sectors.csv");
        if (!File.Exists(file))
        {
            Console.WriteLine("Sector CSV not found");
            return;
        }

        var lines = await File.ReadAllLinesAsync(file);

        var csvSectors = lines
            .Skip(1)
            .Select(l =>
            {
                var p = l.Split(',');
                return new
                {
                    Description = p[0].Trim(),
                    Name = p[1].Trim()
                };
            })
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase) // remove duplicates
            .Select(g => g.First())
            .ToList();

        // Load existing from DB once
        var existing = await _sectorRepo.GetListAsync(s => true);
        var existingLookup = existing.ToDictionary(
            x => x.Name.Trim(),
            x => x,
            StringComparer.OrdinalIgnoreCase);

        var toInsert = new List<Sector>();
        var toUpdate = new List<Sector>();

        foreach (var s in csvSectors)
        {
            if (existingLookup.TryGetValue(s.Name, out var dbSector))
            {
                dbSector.Description = s.Description;
                toUpdate.Add(dbSector);
            }
            else
            {
                toInsert.Add(new Sector
                {
                    Name = s.Name,
                    Description = s.Description
                });
            }
        }

        if (toInsert.Count > 0)
            await _sectorRepo.InsertBulkAsync(toInsert);

        if (toUpdate.Count > 0)
            await _sectorRepo.UpdateBulkAsync(toUpdate);

        Console.WriteLine($"Sectors: Inserted {toInsert.Count}, Updated {toUpdate.Count}");
    }

    // ----------------------------------------
    // STOCKS
    // ----------------------------------------
    private async Task SeedStocksAsync()
    {
        var file = Path.Combine(_dataSeedPath, "stocks.csv");
        if (!File.Exists(file))
        {
            Console.WriteLine("Stocks CSV not found");
            return;
        }

        var lines = await File.ReadAllLinesAsync(file);
        var rows = lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        if (!rows.Any())
            return;

        // Load sectors once
        var sectors = await _sectorRepo.GetAllAsync();
        var sectorMap = sectors.ToDictionary(s => s.Name.Trim(), s => s.Id, StringComparer.OrdinalIgnoreCase);

        // Load existing stocks once
        var existingStocks = await _stockRepo.GetAllAsync();
        var stockMap = existingStocks.ToDictionary(s => s.Symbol.Trim(), s => s, StringComparer.OrdinalIgnoreCase);

        var toInsert = new List<Stock>();
        var toUpdate = new List<Stock>();

        foreach (var line in rows)
        {
            var parts = line.Split(',');

            var symbol = parts[0].Trim();
            var name = parts[1].Trim();
            var exchange = parts[2].Trim();
            var instrumentKey = parts[3].Trim();
            var sector = parts[5].Trim();

            var sectorId = sectorMap.TryGetValue(sector, out var sid) ? sid : sectorMap.GetValueOrDefault("Default");


            if (!stockMap.TryGetValue(symbol, out var dbStock))
            {
                toInsert.Add(new Stock
                {
                    Symbol = symbol,
                    Name = name,
                    Description = name,
                    Exchange = exchange == "1" ? Exchange.NSE : Exchange.BSE,
                    InstrumentKey = instrumentKey,
                    SectorId = sectorId,
                    CurrentPrice = 0
                });
            }
            else
            {
                bool changed = false;

                if (dbStock.Name != name)
                {
                    dbStock.Name = name;
                    changed = true;
                }

                if (dbStock.Description != name)
                {
                    dbStock.Description = name;
                    changed = true;
                }

                var ex = exchange == "1" ? Exchange.NSE : Exchange.BSE;
                if (dbStock.Exchange != ex)
                {
                    dbStock.Exchange = ex;
                    changed = true;
                }

                if (dbStock.InstrumentKey != instrumentKey)
                {
                    dbStock.InstrumentKey = instrumentKey;
                    changed = true;
                }

                if (dbStock.SectorId != sectorId)
                {
                    dbStock.SectorId = sectorId;
                    changed = true;
                }

                if (changed)
                    toUpdate.Add(dbStock);
            }
        }

        if (toInsert.Any())
            await _stockRepo.InsertBulkAsync(toInsert);

        if (toUpdate.Any())
            await _stockRepo.UpdateBulkAsync(toUpdate);

        Console.WriteLine($"Stocks → Inserted: {toInsert.Count}, Updated: {toUpdate.Count}");
    }
}
