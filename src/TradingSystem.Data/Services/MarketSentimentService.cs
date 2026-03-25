using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Data.Services;

public class MarketSentimentService : IMarketSentimentService
{
    private readonly IMarketSentimentRepository _sentimentRepository;
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly IInstrumentService _instrumentService;
    private readonly ILogger<MarketSentimentService> _logger;

    public MarketSentimentService(
        IMarketSentimentRepository sentimentRepository,
        IInstrumentRepository instrumentRepository,
        IMarketCandleRepository candleRepository,
        IInstrumentService instrumentService,
        ILogger<MarketSentimentService> logger)
    {
        _sentimentRepository = sentimentRepository;
        _instrumentRepository = instrumentRepository;
        _candleRepository = candleRepository;
        _instrumentService = instrumentService;
        _logger = logger;
    }

    public async Task<MarketContext> GetCurrentMarketContextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var latestSentiment = await _sentimentRepository.GetLatestAsync(cancellationToken);
            if (latestSentiment == null || (DateTimeOffset.UtcNow - latestSentiment.Timestamp).TotalMinutes > 30)
            {
                // Data is stale or missing, generate new analysis
                return await AnalyzeAndUpdateMarketSentimentAsync(cancellationToken);
            }
            return MapToMarketContext(latestSentiment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current market context");
            throw;
        }

    }

    public async Task<MarketContext> AnalyzeAndUpdateMarketSentimentAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting market sentiment analysis at {Time}", DateTime.UtcNow);

        try
        {
            // Analyze major indices from database
            var indexPerformances = await AnalyzeIndicesAsync(cancellationToken);

            // Analyze sector performance from database
            var sectorPerformances = await AnalyzeSectorsAsync(cancellationToken);

            // Calculate market breadth
            var breadth = await CalculateMarketBreadthAsync(cancellationToken);

            // Get volatility index (India VIX)
            var volatilityIndex = await GetVolatilityIndexAsync(cancellationToken);

            // Calculate sentiment score
            var sentimentScore = CalculateSentimentScore(indexPerformances, sectorPerformances, breadth, volatilityIndex);
            var sentiment = DetermineSentiment(sentimentScore);

            // Identify key factors
            var keyFactors = IdentifyKeyFactors(indexPerformances, sectorPerformances, breadth, volatilityIndex);

            // Create market sentiment entity
            var marketSentiment = new MarketSentiment
            {
                Timestamp = DateTimeOffset.UtcNow,
                Sentiment = sentiment,
                SentimentScore = sentimentScore,
                VolatilityIndex = volatilityIndex,
                MarketBreadth = breadth,
                IndexPerformance = indexPerformances,
                SectorPerformance = sectorPerformances,
                KeyFactors = keyFactors,
                CreatedAt = DateTimeOffset.UtcNow
            };

            // Save to database
            await _sentimentRepository.AddAsync(marketSentiment, cancellationToken);

            _logger.LogInformation("Market sentiment analysis completed: {Sentiment} ({Score})", sentiment, sentimentScore);

            return MapToMarketContext(marketSentiment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing market sentiment");
            throw;
        }
    }

    public async Task<List<MarketSentiment>> GetHistoryAsync(
        DateTime fromDate, 
        DateTime toDate, 
        CancellationToken cancellationToken = default)
    {
        return await _sentimentRepository.GetHistoryAsync(fromDate, toDate, cancellationToken);
    }

    public decimal AdjustConfidenceForMarketSentiment(decimal baseConfidence, SentimentType sentiment)
    {
        // Adjust confidence based on market sentiment
        var adjustment = sentiment switch
        {
            SentimentType.BULLISH => 1.1m,  // Boost confidence by 10% in bullish market
            SentimentType.BEARISH => 0.9m,  // Reduce confidence by 10% in bearish market
            SentimentType.NEUTRAL => 1.0m,  // No adjustment in neutral market
            _ => 1.0m
        };

        return Math.Min(Math.Max(baseConfidence * adjustment, 0), 100);
    }

    /// <summary>
    /// Analyze major indices performance from database using InstrumentType.INDEX
    /// </summary>
    private async Task<List<IndexPerformance>> AnalyzeIndicesAsync(CancellationToken cancellationToken)
    {
        var performances = new List<IndexPerformance>();
        var today = DateTime.Today;

        try
        {
            // Get all active INDEX instruments from database
            var indices = await _instrumentRepository.GetListAsync(
                i => i.InstrumentType == InstrumentType.INDEX && i.IsActive,
                cancellationToken);

            _logger.LogInformation("Found {Count} active indices to analyze", indices.Count);

            foreach (var instrument in indices)
            {
                try
                {
                    // Get today's candles
                    var candles = await _candleRepository.GetByInstrumentIdAsync(
                        instrument.Id,
                        1,
                        today,
                        DateTime.UtcNow,
                        cancellationToken);

                    if (!candles.Any())
                    {
                        _logger.LogDebug("No candles found for index {Symbol}", instrument.Symbol);
                        continue;
                    }

                    var latestCandle = candles.OrderByDescending(c => c.Timestamp).First();
                    var firstCandle = candles.OrderBy(c => c.Timestamp).First();

                    var changePercent = firstCandle.Open != 0 
                        ? ((latestCandle.Close - firstCandle.Open) / firstCandle.Open) * 100 
                        : 0;

                    performances.Add(new IndexPerformance
                    {
                        IndexName = instrument.Name,
                        Symbol = instrument.Symbol,
                        CurrentValue = latestCandle.Close,
                        ChangePercent = changePercent,
                        DayHigh = candles.Max(c => c.High),
                        DayLow = candles.Min(c => c.Low)
                    });

                    _logger.LogDebug("Analyzed index {Symbol}: {Change}%", 
                        instrument.Symbol, changePercent);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error analyzing index {Symbol}", instrument.Symbol);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting indices from database");
        }

        return performances;
    }

    /// <summary>
    /// Analyze sector performance from database using Sector table and TradingInstrument.SectorId
    /// </summary>
    private async Task<List<SectorPerformance>> AnalyzeSectorsAsync(CancellationToken cancellationToken)
    {
        var sectorPerformances = new List<SectorPerformance>();
        var today = DateTime.Today;

        try
        {
            // Get all sectors from database
            var sectors = await _instrumentService.GetSectorsAsync();
            
            _logger.LogInformation("Found {Count} sectors to analyze", sectors.Count);

            foreach (var sector in sectors.Where(s => s.IsActive))
            {
                try
                {
                    // Get all active stocks in this sector
                    var sectorStocks = await _instrumentRepository.GetListAsync(
                        i => i.SectorId == sector.Id 
                             && i.InstrumentType == InstrumentType.STOCK 
                             && i.IsActive,
                        cancellationToken);

                    if (!sectorStocks.Any())
                    {
                        _logger.LogDebug("No stocks found for sector {SectorName}", sector.Name);
                        continue;
                    }

                    var advancing = 0;
                    var declining = 0;
                    var unchanged = 0;
                    var totalChange = 0m;
                    var processedCount = 0;

                    // Analyze each stock in the sector
                    foreach (var stock in sectorStocks)
                    {
                        try
                        {
                            var candles = await _candleRepository.GetByInstrumentIdAsync(
                                stock.Id,
                                1,
                                today,
                                DateTime.UtcNow,
                                cancellationToken);

                            if (!candles.Any()) continue;

                            var firstCandle = candles.OrderBy(c => c.Timestamp).First();
                            var latestCandle = candles.OrderByDescending(c => c.Timestamp).First();

                            var change = firstCandle.Open != 0 
                                ? ((latestCandle.Close - firstCandle.Open) / firstCandle.Open) * 100 
                                : 0;

                            totalChange += change;
                            processedCount++;

                            if (change > 0.1m) advancing++;
                            else if (change < -0.1m) declining++;
                            else unchanged++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Error analyzing stock {Symbol} in sector {SectorName}", 
                                stock.Symbol, sector.Name);
                        }
                    }

                    if (processedCount > 0)
                    {
                        var avgChange = totalChange / processedCount;
                        var relativeStrength = (advancing + declining) > 0 
                            ? (decimal)advancing / (advancing + declining) 
                            : 0.5m;

                        sectorPerformances.Add(new SectorPerformance
                        {
                            SectorName = sector.Name,
                            ChangePercent = avgChange,
                            StocksAdvancing = advancing,
                            StocksDeclining = declining,
                            RelativeStrength = relativeStrength
                        });

                        _logger.LogDebug("Analyzed sector {SectorName}: {Change}% (A:{Advancing} D:{Declining} U:{Unchanged})", 
                            sector.Name, avgChange, advancing, declining, unchanged);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error analyzing sector {SectorName}", sector.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sectors from database");
        }

        return sectorPerformances;
    }

    /// <summary>
    /// Calculate market breadth (advance/decline ratio) for all active stocks
    /// </summary>
    private async Task<decimal> CalculateMarketBreadthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get all active stocks (not indices)
            var activeStocks = await _instrumentRepository.GetListAsync(
                i => i.InstrumentType == InstrumentType.STOCK && i.IsActive,
                cancellationToken);

            var today = DateTime.Today;
            var advancing = 0;
            var declining = 0;
            var unchanged = 0;

            // Limit to top stocks by market cap or first 200 for performance
            var stocksToAnalyze = activeStocks
                .OrderByDescending(s => s.MarketCap ?? 0)
                .Take(200)
                .ToList();

            _logger.LogInformation("Calculating market breadth for {Count} stocks", stocksToAnalyze.Count);

            foreach (var stock in stocksToAnalyze)
            {
                try
                {
                    var candles = await _candleRepository.GetByInstrumentIdAsync(
                        stock.Id,
                        1,
                        today,
                        DateTime.UtcNow,
                        cancellationToken);

                    if (!candles.Any()) continue;

                    var firstCandle = candles.OrderBy(c => c.Timestamp).First();
                    var latestCandle = candles.OrderByDescending(c => c.Timestamp).First();

                    var change = latestCandle.Close - firstCandle.Open;

                    if (change > 0.01m) advancing++;
                    else if (change < -0.01m) declining++;
                    else unchanged++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error calculating breadth for stock {Symbol}", stock.Symbol);
                }
            }

            var breadthRatio = declining > 0 ? (decimal)advancing / declining : (advancing > 0 ? 10m : 1.0m);

            _logger.LogInformation("Market breadth: A:{Advancing} D:{Declining} U:{Unchanged} Ratio:{Ratio:F2}", 
                advancing, declining, unchanged, breadthRatio);

            return breadthRatio;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating market breadth");
            return 1.0m; // Neutral
        }
    }

    /// <summary>
    /// Get India VIX volatility index from database
    /// </summary>
    private async Task<decimal> GetVolatilityIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Try to find India VIX in the database (it should be marked as INDEX type)
            var vixInstrument = await _instrumentRepository.GetListAsync(
                i => i.Symbol.Contains("VIX") 
                     && i.InstrumentType == InstrumentType.INDEX 
                     && i.IsActive,
                cancellationToken);

            var vix = vixInstrument.FirstOrDefault();
            if (vix == null)
            {
                _logger.LogWarning("India VIX instrument not found in database");
                return 20m; // Default moderate volatility
            }

            var candles = await _candleRepository.GetByInstrumentIdAsync(
                vix.Id,
                1,
                DateTime.Today,
                DateTime.UtcNow,
                cancellationToken);

            if (!candles.Any())
            {
                _logger.LogWarning("No VIX data available for today");
                return 20m;
            }

            var latestCandle = candles.OrderByDescending(c => c.Timestamp).First();
            _logger.LogInformation("India VIX: {VIX}", latestCandle.Close);
            
            return latestCandle.Close;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting volatility index");
            return 20m; // Default
        }
    }

    private decimal CalculateSentimentScore(
        List<IndexPerformance> indices,
        List<SectorPerformance> sectors,
        decimal breadth,
        decimal volatility)
    {
        var score = 0m;

        // Index performance (40% weight)
        if (indices.Any())
        {
            var avgIndexChange = indices.Average(i => i.ChangePercent);
            score += avgIndexChange * 4; // Convert to -40 to +40 range
        }

        // Sector performance (30% weight)
        if (sectors.Any())
        {
            var avgSectorChange = sectors.Average(s => s.ChangePercent);
            score += avgSectorChange * 3; // Convert to -30 to +30 range
        }

        // Market breadth (20% weight)
        var breadthScore = (breadth - 1) * 20; // Above 1 is positive, below 1 is negative
        score += breadthScore;

        // Volatility (10% weight, inverse)
        var volScore = volatility < 15 ? 10 : (volatility > 25 ? -10 : 0);
        score += volScore;

        return Math.Max(Math.Min(score, 100), -100);
    }

    private SentimentType DetermineSentiment(decimal score)
    {
        return score switch
        {
            > 30 => SentimentType.BULLISH,
            < -30 => SentimentType.BEARISH,
            _ => SentimentType.NEUTRAL
        };
    }

    private List<string> IdentifyKeyFactors(
        List<IndexPerformance> indices,
        List<SectorPerformance> sectors,
        decimal breadth,
        decimal volatility)
    {
        var factors = new List<string>();

        // Index factors
        var strongIndices = indices.Where(i => i.ChangePercent > 1).ToList();
        var weakIndices = indices.Where(i => i.ChangePercent < -1).ToList();

        if (strongIndices.Any())
            factors.Add($"Strong performance: {string.Join(", ", strongIndices.Select(i => i.IndexName))}");

        if (weakIndices.Any())
            factors.Add($"Weak performance: {string.Join(", ", weakIndices.Select(i => i.IndexName))}");

        // Sector factors
        var topSector = sectors.OrderByDescending(s => s.ChangePercent).FirstOrDefault();
        var bottomSector = sectors.OrderBy(s => s.ChangePercent).FirstOrDefault();

        if (topSector != null && topSector.ChangePercent > 0.5m)
            factors.Add($"{topSector.SectorName} sector leading ({topSector.ChangePercent:F2}%)");

        if (bottomSector != null && bottomSector.ChangePercent < -0.5m)
            factors.Add($"{bottomSector.SectorName} sector lagging ({bottomSector.ChangePercent:F2}%)");

        // Breadth factors
        if (breadth > 1.5m)
            factors.Add($"Strong market breadth (A/D: {breadth:F2})");
        else if (breadth < 0.67m)
            factors.Add($"Weak market breadth (A/D: {breadth:F2})");

        // Volatility factors
        if (volatility > 25)
            factors.Add($"High volatility (VIX: {volatility:F2})");
        else if (volatility < 12)
            factors.Add($"Low volatility (VIX: {volatility:F2})");

        return factors;
    }

    private MarketContext MapToMarketContext(MarketSentiment sentiment)
    {
        var summary = GenerateSummary(
            sentiment.Sentiment,
            sentiment.SentimentScore,
            sentiment.VolatilityIndex);

        return new MarketContext
        {
            Timestamp = sentiment.Timestamp,
            Sentiment = sentiment.Sentiment,
            SentimentScore = sentiment.SentimentScore,
            VolatilityIndex = sentiment.VolatilityIndex,
            MarketBreadth = sentiment.MarketBreadth,

            // ✅ Direct mapping (no serialization)
            MajorIndices = sentiment.IndexPerformance ?? new List<IndexPerformance>(),
            Sectors = sentiment.SectorPerformance ?? new List<SectorPerformance>(),

            KeyFactors = sentiment.KeyFactors ?? new List<string>(),
            Summary = summary
        };
    }
    private string GenerateSummary(SentimentType sentiment, decimal score, decimal volatility)
    {
        var sentimentText = sentiment switch
        {
            SentimentType.BULLISH => "bullish",
            SentimentType.BEARISH => "bearish",
            _ => "neutral"
        };

        var volText = volatility switch
        {
            < 12 => "low volatility",
            > 25 => "high volatility",
            _ => "moderate volatility"
        };

        return $"Market sentiment is {sentimentText} (score: {score:F1}) with {volText} (VIX: {volatility:F1}). " +
               $"Trading recommendations are adjusted accordingly.";
    }
}