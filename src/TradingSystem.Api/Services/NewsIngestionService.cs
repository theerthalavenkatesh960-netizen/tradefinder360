using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data;

namespace TradingSystem.Api.Services;

public class NewsIngestionService : INewsIngestionService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "with", "from", "that", "this", "have", "will", "their", "about", "after", "before", "under",
        "into", "across", "amid", "amidst", "market", "stock", "stocks", "india", "today", "update",
        "news", "says", "said", "report", "reports", "outlook", "while", "during", "against", "there"
    };

    private readonly TradingDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NewsIngestionService> _logger;

    public NewsIngestionService(
        TradingDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<NewsIngestionService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<int> IngestMorningNewsAsync(CancellationToken cancellationToken = default)
    {
        return IngestNewsInternalAsync("morning", cancellationToken);
    }

    public Task<int> IngestHourlyNewsAsync(CancellationToken cancellationToken = default)
    {
        return IngestNewsInternalAsync("hourly", cancellationToken);
    }

    public async Task<IReadOnlyList<NewsArticle>> GetRecentNewsAsync(int hoursBack = 24, int limit = 100, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddHours(-Math.Abs(hoursBack));
        return await _dbContext.NewsArticles
            .AsNoTracking()
            .Include(x => x.Keywords)
            .Include(x => x.Impacts)
            .Where(x => x.PublishedAt >= since)
            .OrderByDescending(x => x.PublishedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
    }

    private async Task<int> IngestNewsInternalAsync(string mode, CancellationToken cancellationToken)
    {
        var feeds = _configuration.GetSection("NewsIngestion:RssFeeds").Get<string[]>() ?? Array.Empty<string>();
        var maxArticlesPerRun = int.TryParse(_configuration["NewsIngestion:MaxArticlesPerRun"], out var parsedMax)
            ? Math.Clamp(parsedMax, 1, 500)
            : 100;

        var items = new List<RawNewsItem>();
        if (feeds.Length > 0)
        {
            foreach (var feed in feeds)
            {
                if (string.IsNullOrWhiteSpace(feed))
                {
                    continue;
                }

                try
                {
                    var loaded = await LoadRssItemsAsync(feed.Trim(), cancellationToken);
                    items.AddRange(loaded);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed loading feed {FeedUrl}", feed);
                }
            }
        }

        if (items.Count == 0)
        {
            items.AddRange(BuildFallbackItems(mode));
        }

        items = items
            .OrderByDescending(x => x.PublishedAt)
            .Take(maxArticlesPerRun)
            .ToList();

        var externalIds = items
            .Select(x => BuildExternalId(x.Source, x.Url, x.Headline, x.PublishedAt))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var existingIds = await _dbContext.NewsArticles
            .AsNoTracking()
            .Where(x => externalIds.Contains(x.ExternalId))
            .Select(x => x.ExternalId)
            .ToListAsync(cancellationToken);

        var existingSet = existingIds.ToHashSet(StringComparer.Ordinal);

        var instruments = await _dbContext.Instruments
            .AsNoTracking()
            .Select(x => new InstrumentRef(x.Id, x.Symbol))
            .ToListAsync(cancellationToken);

        var sectors = await _dbContext.Sectors
            .AsNoTracking()
            .Select(x => new SectorRef(x.Id, x.Name))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var inserted = 0;

        foreach (var item in items)
        {
            var externalId = BuildExternalId(item.Source, item.Url, item.Headline, item.PublishedAt);
            if (existingSet.Contains(externalId))
            {
                continue;
            }

            var sentiment = InferSentiment(item.Headline, item.Summary);
            var sentimentScore = InferSentimentScore(sentiment);

            var article = new NewsArticle
            {
                ExternalId = externalId,
                Source = item.Source,
                Url = item.Url,
                Headline = item.Headline,
                Summary = item.Summary,
                PublishedAt = item.PublishedAt,
                IngestedAt = now,
                Sentiment = sentiment,
                SentimentScore = sentimentScore
            };

            var keywords = ExtractKeywords(item.Headline, item.Summary)
                .Select(x => new NewsKeyword
                {
                    Keyword = x.Keyword,
                    RelevanceScore = x.Relevance,
                    CreatedAt = now
                })
                .ToList();

            foreach (var keyword in keywords)
            {
                article.Keywords.Add(keyword);
            }

            var impacts = InferImpacts(item, sentiment, sentimentScore, instruments, sectors, now);
            foreach (var impact in impacts)
            {
                article.Impacts.Add(impact);
            }

            await _dbContext.NewsArticles.AddAsync(article, cancellationToken);
            existingSet.Add(externalId);
            inserted++;
        }

        if (inserted > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("News ingestion {Mode} inserted {Count} records.", mode, inserted);
        return inserted;
    }

    private async Task<IReadOnlyList<RawNewsItem>> LoadRssItemsAsync(string feedUrl, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        using var response = await client.GetAsync(feedUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<RawNewsItem>();
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return Array.Empty<RawNewsItem>();
        }

        var sourceHost = new Uri(feedUrl).Host;
        var doc = XDocument.Parse(xml);

        var rssItems = doc.Descendants("item")
            .Select(x => new RawNewsItem
            {
                Source = sourceHost,
                Url = (x.Element("link")?.Value ?? string.Empty).Trim(),
                Headline = (x.Element("title")?.Value ?? string.Empty).Trim(),
                Summary = (x.Element("description")?.Value ?? string.Empty).Trim(),
                PublishedAt = ParseDate(x.Element("pubDate")?.Value)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Headline))
            .ToList();

        if (rssItems.Count > 0)
        {
            return rssItems;
        }

        XNamespace atom = "http://www.w3.org/2005/Atom";
        var atomEntries = doc.Descendants(atom + "entry")
            .Select(x => new RawNewsItem
            {
                Source = sourceHost,
                Url = (x.Element(atom + "link")?.Attribute("href")?.Value ?? string.Empty).Trim(),
                Headline = (x.Element(atom + "title")?.Value ?? string.Empty).Trim(),
                Summary = (x.Element(atom + "summary")?.Value
                           ?? x.Element(atom + "content")?.Value
                           ?? string.Empty).Trim(),
                PublishedAt = ParseDate(x.Element(atom + "updated")?.Value)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Headline))
            .ToList();

        return atomEntries;
    }

    private static List<RawNewsItem> BuildFallbackItems(string mode)
    {
        var now = DateTime.UtcNow;
        var suffix = mode.Equals("morning", StringComparison.OrdinalIgnoreCase) ? "opening bell" : "intraday";

        return new List<RawNewsItem>
        {
            new()
            {
                Source = "fallback.news",
                Url = "https://fallback.news/local-market-1",
                Headline = $"Banking and IT sectors show mixed sentiment in {suffix} update",
                Summary = "Large cap banking stocks remain resilient while selective IT counters face profit booking pressure.",
                PublishedAt = now.AddMinutes(-30)
            },
            new()
            {
                Source = "fallback.news",
                Url = "https://fallback.news/local-market-2",
                Headline = $"Auto and energy counters gain momentum as risk appetite improves {suffix}",
                Summary = "Institutional activity suggests selective accumulation in auto and energy names with stable macro backdrop.",
                PublishedAt = now.AddMinutes(-15)
            }
        };
    }

    private static string BuildExternalId(string source, string url, string headline, DateTime publishedAt)
    {
        var raw = $"{source}|{url}|{headline}|{publishedAt:O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static DateTime ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.UtcNow;
    }

    private static NewsSentiment InferSentiment(string headline, string summary)
    {
        var text = (headline + " " + summary).ToLowerInvariant();
        var positiveSignals = new[] { "gain", "surge", "up", "beat", "strong", "bull", "growth", "positive", "rally" };
        var negativeSignals = new[] { "fall", "drop", "down", "miss", "weak", "bear", "decline", "negative", "selloff" };

        var positiveCount = positiveSignals.Count(text.Contains);
        var negativeCount = negativeSignals.Count(text.Contains);

        if (positiveCount > negativeCount)
        {
            return NewsSentiment.POSITIVE;
        }

        if (negativeCount > positiveCount)
        {
            return NewsSentiment.NEGATIVE;
        }

        return NewsSentiment.NEUTRAL;
    }

    private static decimal InferSentimentScore(NewsSentiment sentiment)
    {
        return sentiment switch
        {
            NewsSentiment.POSITIVE => 0.65m,
            NewsSentiment.NEGATIVE => -0.65m,
            _ => 0m
        };
    }

    private static List<(string Keyword, decimal Relevance)> ExtractKeywords(string headline, string summary)
    {
        var combined = (headline + " " + summary)
            .ToLowerInvariant()
            .Replace(".", " ")
            .Replace(",", " ")
            .Replace(";", " ")
            .Replace(":", " ")
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace("/", " ");

        var words = combined
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 4 && !StopWords.Contains(x) && x.All(char.IsLetter))
            .ToList();

        var frequency = words
            .GroupBy(x => x)
            .Select(g => new { Word = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToList();

        if (frequency.Count == 0)
        {
            return new List<(string Keyword, decimal Relevance)> { ("market", 0.2m) };
        }

        var maxCount = frequency.Max(x => x.Count);
        return frequency
            .Select(x => (x.Word, Math.Round((decimal)x.Count / maxCount, 4)))
            .ToList();
    }

    private static List<NewsImpact> InferImpacts(
        RawNewsItem item,
        NewsSentiment sentiment,
        decimal sentimentScore,
        List<InstrumentRef> instruments,
        List<SectorRef> sectors,
        DateTime createdAt)
    {
        var text = (item.Headline + " " + item.Summary).ToUpperInvariant();
        var direction = sentiment switch
        {
            NewsSentiment.POSITIVE => NewsImpactDirection.BULLISH,
            NewsSentiment.NEGATIVE => NewsImpactDirection.BEARISH,
            _ => NewsImpactDirection.NEUTRAL
        };

        var impacts = new List<NewsImpact>();

        foreach (var instrument in instruments)
        {
            var symbol = (instrument.Symbol ?? string.Empty).ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(symbol) && text.Contains(symbol, StringComparison.Ordinal))
            {
                impacts.Add(new NewsImpact
                {
                    InstrumentId = instrument.Id,
                    Direction = direction,
                    ImpactScore = Math.Abs(sentimentScore),
                    Confidence = 0.7m,
                    Rationale = $"Headline mentions symbol {symbol}.",
                    CreatedAt = createdAt
                });
            }
        }

        foreach (var sector in sectors)
        {
            var sectorName = (sector.Name ?? string.Empty).ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(sectorName) && text.Contains(sectorName, StringComparison.Ordinal))
            {
                impacts.Add(new NewsImpact
                {
                    SectorId = sector.Id,
                    Direction = direction,
                    ImpactScore = Math.Abs(sentimentScore),
                    Confidence = 0.65m,
                    Rationale = $"Headline references sector {sectorName}.",
                    CreatedAt = createdAt
                });
            }
        }

        if (impacts.Count == 0)
        {
            impacts.Add(new NewsImpact
            {
                Direction = direction,
                ImpactScore = Math.Abs(sentimentScore),
                Confidence = 0.4m,
                Rationale = "General market impact inferred from sentiment.",
                CreatedAt = createdAt
            });
        }

        return impacts;
    }

    private sealed class RawNewsItem
    {
        public string Source { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Headline { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
    }

    private sealed record InstrumentRef(int Id, string Symbol);
    private sealed record SectorRef(int Id, string Name);
}
