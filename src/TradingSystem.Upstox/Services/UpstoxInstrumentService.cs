using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;

namespace TradingSystem.Upstox.Services;

public class UpstoxInstrumentService : IUpstoxInstrumentService
{
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<UpstoxInstrumentService> _logger;

    public UpstoxInstrumentService(UpstoxClient upstoxClient, ILogger<UpstoxInstrumentService> logger)
    {
        _upstoxClient = upstoxClient;
        _logger = logger;
    }

    public async Task<List<TradingInstrument>> FetchInstrumentsAsync(string exchange, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching instruments from Upstox for exchange: {Exchange}", exchange);

            var upstoxInstruments = await _upstoxClient.GetInstrumentsAsync(exchange);
            var instruments = new List<TradingInstrument>();

            foreach (var upstoxInst in upstoxInstruments)
            {
                var instrument = new TradingInstrument
                {
                    InstrumentKey = upstoxInst.InstrumentKey,
                    Exchange = upstoxInst.Exchange,
                    Symbol = upstoxInst.TradingSymbol,
                    Name = upstoxInst.Name,
                    InstrumentType = upstoxInst.InstrumentType == "EQ" ? InstrumentType.STOCK : InstrumentType.INDEX,
                    LotSize = upstoxInst.LotSize,
                    TickSize = upstoxInst.TickSize,
                    IsDerivativesEnabled = false,
                    DefaultTradingMode = TradingMode.EQUITY,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                instruments.Add(instrument);
            }

            _logger.LogInformation("Fetched {Count} instruments from {Exchange}", instruments.Count, exchange);
            return instruments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching instruments from Upstox for exchange: {Exchange}", exchange);
            throw;
        }
    }

    public async Task<List<TradingInstrument>> FetchAllEquityInstrumentsAsync(CancellationToken cancellationToken = default)
    {
        var allInstruments = new List<TradingInstrument>();
        var exchanges = new[] { "NSE", "BSE" };

        foreach (var exchange in exchanges)
        {
            try
            {
                var instruments = await FetchInstrumentsAsync(exchange, cancellationToken);
                allInstruments.AddRange(instruments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching instruments for exchange: {Exchange}", exchange);
            }
        }

        return allInstruments;
    }
}
