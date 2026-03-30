namespace TradingSystem.Api.Strategy.Models;

public sealed class TradeSignal
{
    // Always "NIFTY"
    public string Symbol { get; init; } = "NIFTY";

    // "BUY_CE" for bullish, "BUY_PE" for bearish
    public string SignalDirection { get; init; } = string.Empty;

    // Price at which to enter (close of confirmation candle)
    public decimal EntryPrice { get; init; }

    // Stop loss price
    public decimal StopLoss { get; init; }

    // Full exit target (3R)
    public decimal Target { get; init; }

    // Partial exit target (2R) — exit 50% of position here
    public decimal PartialTarget { get; init; }

    // Always 3.0
    public decimal RiskReward { get; init; }

    // Integer 0–100. See confidence score rules in SignalGenerator.
    public int ConfidenceScore { get; init; }

    // Timestamp of the confirmation candle
    public DateTime Timestamp { get; init; }

    // Human-readable explanation of why the trade was taken. Use pipe separator.
    public string Reason { get; init; } = string.Empty;

    // Every filter that was evaluated and rejected, in order.
    // Empty array if signal was successfully generated.
    public string[] RejectionLog { get; init; } = Array.Empty<string>();
}
