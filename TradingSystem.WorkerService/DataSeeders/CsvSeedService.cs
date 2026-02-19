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

            var sectors = new List<Sector>();
            var lines = await File.ReadAllLinesAsync(csvFilePath, cancellationToken);

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = ParseCsvLine(line);
                if (parts.Length < 2) continue;

                var description = parts[0].Trim();
                var sectorName = parts[1].Trim();

                if (string.IsNullOrWhiteSpace(sectorName) || sectorName.Equals("sector", StringComparison.OrdinalIgnoreCase))
                    continue;

                var code = GenerateSectorCode(sectorName);

                if (!sectors.Any(s => s.Code == code))
                {
                    sectors.Add(new Sector
                    {
                        Name = sectorName,
                        Code = code,
                        Description = description,
                        IsActive = true
                    });
                }
            }

            _logger.LogInformation("Parsed {Count} sectors from CSV", sectors.Count);

            var saved = await _sectorRepository.BulkUpsertAsync(sectors, cancellationToken);
            _logger.LogInformation("Successfully seeded {Count} sectors", saved);

            return saved;
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

            var instruments = new List<TradingInstrument>();
            var lines = await File.ReadAllLinesAsync(csvFilePath, cancellationToken);

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = ParseCsvLine(line);
                if (parts.Length < 6) continue;

                var symbol = parts[0].Trim();
                var name = parts[1].Trim();
                var exchange = parts[2].Trim();
                var instrumentKey = parts[3].Trim();
                var industry = parts[4].Trim();
                var sectorName = parts[5].Trim();

                if (string.IsNullOrWhiteSpace(symbol) || symbol.Equals("Symbol", StringComparison.OrdinalIgnoreCase))
                    continue;

                var exchangeCode = exchange == "2" ? "BSE" : "NSE";

                int? sectorId = null;
                if (!string.IsNullOrWhiteSpace(sectorName) && sectorMap.TryGetValue(sectorName, out var secId))
                {
                    sectorId = secId;
                }

                var isin = ExtractISIN(instrumentKey);

                instruments.Add(new TradingInstrument
                {
                    InstrumentKey = instrumentKey,
                    Exchange = exchangeCode,
                    Symbol = symbol,
                    Name = name,
                    SectorId = sectorId,
                    Industry = industry,
                    ISIN = isin,
                    InstrumentType = InstrumentType.STOCK,
                    LotSize = 1,
                    TickSize = 0.05m,
                    IsDerivativesEnabled = false,
                    DefaultTradingMode = TradingMode.EQUITY,
                    IsActive = true
                });
            }

            _logger.LogInformation("Parsed {Count} instruments from CSV", instruments.Count);

            var saved = await _instrumentRepository.BulkUpsertAsync(instruments, cancellationToken);
            _logger.LogInformation("Successfully seeded {Count} instruments", saved);

            return saved;
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

