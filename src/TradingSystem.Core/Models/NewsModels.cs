namespace TradingSystem.Core.Models;

public enum NewsSentiment
{
    POSITIVE,
    NEGATIVE,
    NEUTRAL
}

public enum NewsImpactDirection
{
    BULLISH,
    BEARISH,
    NEUTRAL
}

public class NewsArticle
{
    public long Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;
    public NewsSentiment Sentiment { get; set; } = NewsSentiment.NEUTRAL;
    public decimal SentimentScore { get; set; }

    public List<NewsKeyword> Keywords { get; set; } = new();
    public List<NewsImpact> Impacts { get; set; } = new();
}

public class NewsKeyword
{
    public long Id { get; set; }
    public long ArticleId { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public decimal RelevanceScore { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NewsArticle? Article { get; set; }
}

public class NewsImpact
{
    public long Id { get; set; }
    public long ArticleId { get; set; }
    public int? InstrumentId { get; set; }
    public int? SectorId { get; set; }
    public NewsImpactDirection Direction { get; set; } = NewsImpactDirection.NEUTRAL;
    public decimal ImpactScore { get; set; }
    public decimal Confidence { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NewsArticle? Article { get; set; }
    public TradingInstrument? Instrument { get; set; }
    public Sector? Sector { get; set; }
}
