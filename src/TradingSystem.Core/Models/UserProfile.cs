namespace TradingSystem.Core.Models;

public class UserProfile
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UpstoxAccessToken { get; set; }
    public string? UpstoxRefreshToken { get; set; }
    public DateTime? TokenIssuedAt { get; set; }
    public decimal? PreferredBudget { get; set; }
    public string? PreferredRiskProfile { get; set; }
    public List<string> PreferredSectors { get; set; } = new();
    public List<string> PreferredThemes { get; set; } = new();
    public bool AutoRebalanceEnabled { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
}
