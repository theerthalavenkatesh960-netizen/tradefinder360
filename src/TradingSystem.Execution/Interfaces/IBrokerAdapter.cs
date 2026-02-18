using TradingSystem.Core.Models;

namespace TradingSystem.Execution.Interfaces;

public interface IBrokerAdapter
{
    Task<List<Option>> GetOptionChain(string underlying, DateTime? expiry = null);

    Task<Option?> GetATMOption(string underlying, decimal spotPrice, TradeDirection direction, DateTime? expiry = null);

    Task<string> PlaceOrder(Option option, int quantity, string orderType = "MARKET");

    Task<bool> CancelOrder(string orderId);

    Task<decimal> GetOptionPrice(string optionSymbol);

    Task<decimal> GetSpotPrice(string symbol);

    Task<bool> IsMarketOpen();
}
