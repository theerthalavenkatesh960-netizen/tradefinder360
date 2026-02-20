namespace TradingSystem.Core.Models;

public class UserProfile
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UpstoxAccessToken { get; set; }
    public string? UpstoxRefreshToken { get; set; }
    public DateTime? TokenIssuedAt { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
}
