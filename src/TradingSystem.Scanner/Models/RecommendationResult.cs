using TradingSystem.Core.Models;

namespace TradingSystem.Scanner.Models;

/// <summary>
/// Wraps a recommendation or the reason one could not be generated.
/// </summary>
public class RecommendationResult
{
    public Recommendation? Recommendation { get; init; }
    public bool IsGenerated => Recommendation != null;
    public string? BlockedReason { get; init; }

    public static RecommendationResult Success(Recommendation rec) => new() { Recommendation = rec };
    public static RecommendationResult Blocked(string reason) => new() { BlockedReason = reason };
}