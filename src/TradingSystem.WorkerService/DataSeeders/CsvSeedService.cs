using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.WorkerService.DataSeeders;

public class CsvSeedService
{
    private readonly ISectorRepository _sectorRepository;
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly ILogger<CsvSeedService> _logger;

    public CsvSeedService(
        ISectorRepository sectorRepository,
        IInstrumentRepository instrumentRepository,
        ILogger<CsvSeedService> logger)
    {
        _sectorRepository = sectorRepository;
        _instrumentRepository = instrumentRepository;
        _logger = logger;
    }

    public async Task<int> SeedSectorsFromCsvAsync(string csvFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(csvFilePath))
            {
                _logger.LogError("Sectors CSV file not found at: {Path}", csvFilePath);
                return 0;
            }

            var lines = await File.ReadAllLinesAsync(csvFilePath, cancellationToken);

            var csvSectors = lines
                .Skip(1)
                .Select(line =>
                {
                    var parts = ParseCsvLine(line.Trim());
                    if (parts.Length < 2) return null;

                    var description = parts[0].Trim();
                    var sectorName = parts[1].Trim();

                    if (string.IsNullOrWhiteSpace(sectorName) || sectorName.Equals("sector", StringComparison.OrdinalIgnoreCase))
                        return null;

                    return new
                    {
                        Description = description,
                        Name = sectorName,
                        Code = GenerateSectorCode(sectorName)
                    };
                })
                .Where(x => x != null)
                .GroupBy(x => x!.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()!)
                .ToList();

            _logger.LogInformation("Parsed {Count} unique sectors from CSV", csvSectors.Count);

            var existing = await _sectorRepository.GetListAsync(s => true, cancellationToken);
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
                    dbSector.Code = s.Code;
                    toUpdate.Add(dbSector);
                }
                else
                {
                    toInsert.Add(new Sector
                    {
                        Name = s.Name,
                        Code = s.Code,
                        Description = s.Description,
                        IsActive = true
                    });
                }
            }

            if (toInsert.Count > 0)
            {
                await _sectorRepository.InsertBulkAsync(toInsert, cancellationToken);
                _logger.LogInformation("Inserted {Count} new sectors", toInsert.Count);
            }

            if (toUpdate.Count > 0)
            {
                await _sectorRepository.UpdateBulkAsync(toUpdate, cancellationToken);
                _logger.LogInformation("Updated {Count} existing sectors", toUpdate.Count);
            }

            var totalProcessed = toInsert.Count + toUpdate.Count;
            _logger.LogInformation("Successfully processed {Count} sectors", totalProcessed);

            return totalProcessed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding sectors from CSV: {Path}", csvFilePath);
            throw;
        }
    }

    public async Task<int> SeedInstrumentsFromCsvAsync(string csvFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(csvFilePath))
            {
                _logger.LogError("Instruments CSV file not found at: {Path}", csvFilePath);
                return 0;
            }

            var allSectors = await _sectorRepository.GetAllAsync(cancellationToken);
            var sectorMap = allSectors.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);

            var lines = await File.ReadAllLinesAsync(csvFilePath, cancellationToken);

            var csvInstruments = lines
                .Skip(1)
                .Select(line =>
                {
                    var parts = ParseCsvLine(line.Trim());
                    if (parts.Length < 6) return null;

                    var symbol = parts[0].Trim();
                    var name = parts[1].Trim();
                    var exchange = parts[2].Trim();
                    var instrumentKey = parts[3].Trim();
                    var industry = parts[4].Trim();
                    var sectorName = parts[5].Trim();

                    if (string.IsNullOrWhiteSpace(symbol) || symbol.Equals("Symbol", StringComparison.OrdinalIgnoreCase))
                        return null;

                    var exchangeCode = exchange == "2" ? "BSE" : "NSE";

                    int? sectorId = null;
                    if (!string.IsNullOrWhiteSpace(sectorName) && sectorMap.TryGetValue(sectorName, out var secId))
                    {
                        sectorId = secId;
                    }

                    var isin = ExtractISIN(instrumentKey);

                    return new
                    {
                        InstrumentKey = instrumentKey,
                        Exchange = exchangeCode,
                        Symbol = symbol,
                        Name = name,
                        SectorId = sectorId,
                        Industry = industry,
                        ISIN = isin
                    };
                })
                .Where(x => x != null)
                .GroupBy(x => x!.InstrumentKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()!)
                .ToList();

            _logger.LogInformation("Parsed {Count} unique instruments from CSV", csvInstruments.Count);

            var existing = await _instrumentRepository.GetListAsync(i => true, cancellationToken);
            var existingLookup = existing.ToDictionary(
                x => x.InstrumentKey.Trim(),
                x => x,
                StringComparer.OrdinalIgnoreCase);

            var toInsert = new List<TradingInstrument>();
            var toUpdate = new List<TradingInstrument>();

            foreach (var i in csvInstruments)
            {
                if (existingLookup.TryGetValue(i.InstrumentKey, out var dbInstrument))
                {
                    dbInstrument.Exchange = i.Exchange;
                    dbInstrument.Symbol = i.Symbol;
                    dbInstrument.Name = i.Name;
                    dbInstrument.SectorId = i.SectorId;
                    dbInstrument.Industry = i.Industry;
                    dbInstrument.ISIN = i.ISIN;
                    toUpdate.Add(dbInstrument);
                }
                else
                {
                    toInsert.Add(new TradingInstrument
                    {
                        InstrumentKey = i.InstrumentKey,
                        Exchange = i.Exchange,
                        Symbol = i.Symbol,
                        Name = i.Name,
                        SectorId = i.SectorId,
                        Industry = i.Industry,
                        ISIN = i.ISIN,
                        InstrumentType = InstrumentType.STOCK,
                        LotSize = 1,
                        TickSize = 0.05m,
                        IsDerivativesEnabled = false,
                        DefaultTradingMode = TradingMode.EQUITY,
                        IsActive = true
                    });
                }
            }

            if (toInsert.Count > 0)
            {
                await _instrumentRepository.InsertBulkAsync(toInsert, cancellationToken);
                _logger.LogInformation("Inserted {Count} new instruments", toInsert.Count);
            }

            if (toUpdate.Count > 0)
            {
                await _instrumentRepository.UpdateBulkAsync(toUpdate, cancellationToken);
                _logger.LogInformation("Updated {Count} existing instruments", toUpdate.Count);
            }

            var totalProcessed = toInsert.Count + toUpdate.Count;
            _logger.LogInformation("Successfully processed {Count} instruments", totalProcessed);

            return totalProcessed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding instruments from CSV: {Path}", csvFilePath);
            throw;
        }
    }

    private string[] ParseCsvLine(string line)
    {
        var parts = new List<string>();
        var currentPart = string.Empty;
        var insideQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                insideQuotes = !insideQuotes;
            }
            else if (c == ',' && !insideQuotes)
            {
                parts.Add(currentPart.Trim('"', ' '));
                currentPart = string.Empty;
            }
            else
            {
                currentPart += c;
            }
        }

        parts.Add(currentPart.Trim('"', ' '));
        return parts.ToArray();
    }

    private string GenerateSectorCode(string sectorName)
    {
        var cleanName = new string(sectorName
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .ToArray());

        var words = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 1)
        {
            return words[0].Length > 10 ? words[0].Substring(0, 10).ToUpper() : words[0].ToUpper();
        }

        var code = string.Join("_", words.Select(w => w.Length > 3 ? w.Substring(0, 3) : w));
        code = code.Length > 20 ? code.Substring(0, 20) : code;

        return code.ToUpper();
    }

    private string ExtractISIN(string instrumentKey)
    {
        if (string.IsNullOrWhiteSpace(instrumentKey)) return string.Empty;

        var parts = instrumentKey.Split('|');
        if (parts.Length > 1)
        {
            var isin = parts[1].Trim();
            if (isin.Length == 12 && isin.StartsWith("INE"))
            {
                return isin;
            }
        }

        return string.Empty;
    }
}

