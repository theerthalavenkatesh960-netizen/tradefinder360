using TradingSystem.Core.Models;
using TradingSystem.Configuration.Models;
using TradingSystem.Execution.Interfaces;

namespace TradingSystem.Execution;

public class ExecutionEngine
{
    private readonly IBrokerAdapter _broker;
    private readonly ExecutionConfig _config;

    public ExecutionEngine(IBrokerAdapter broker, ExecutionConfig config)
    {
        _broker = broker;
        _config = config;
    }

    public async Task<(bool success, Option? option, string orderId, string message)> ExecuteEntry(
        TradeDirection direction,
        decimal spotPrice,
        int quantity)
    {
        try
        {
            if (!await _broker.IsMarketOpen())
            {
                return (false, null, string.Empty, "Market is closed");
            }

            DateTime? targetExpiry = _config.UseWeeklyOptions
                ? OptionsSelector.GetNearestWeeklyExpiry(DateTime.Now)
                : null;

            var atmOption = await _broker.GetATMOption(
                _config.UnderlyingSymbol,
                spotPrice,
                direction,
                targetExpiry);

            if (atmOption == null)
            {
                return (false, null, string.Empty, "No ATM option found");
            }

            if (!OptionsSelector.IsOptionLiquid(atmOption))
            {
                return (false, atmOption, string.Empty, "Option is not liquid enough");
            }

            var orderId = await _broker.PlaceOrder(atmOption, quantity);

            if (string.IsNullOrEmpty(orderId))
            {
                return (false, atmOption, string.Empty, "Order placement failed");
            }

            return (true, atmOption, orderId, "Order placed successfully");
        }
        catch (Exception ex)
        {
            return (false, null, string.Empty, $"Execution error: {ex.Message}");
        }
    }

    public async Task<(bool success, decimal exitPrice, string message)> ExecuteExit(
        Option option,
        int quantity)
    {
        try
        {
            if (!await _broker.IsMarketOpen())
            {
                return (false, 0, "Market is closed");
            }

            var currentPrice = await _broker.GetOptionPrice(option.Symbol);

            var orderId = await _broker.PlaceOrder(option, quantity, "MARKET");

            if (string.IsNullOrEmpty(orderId))
            {
                return (false, 0, "Exit order placement failed");
            }

            return (true, currentPrice, "Exit order placed successfully");
        }
        catch (Exception ex)
        {
            return (false, 0, $"Exit execution error: {ex.Message}");
        }
    }

    public async Task<decimal> GetCurrentSpotPrice()
    {
        return await _broker.GetSpotPrice(_config.UnderlyingSymbol);
    }

    public async Task<decimal> GetCurrentOptionPrice(string optionSymbol)
    {
        return await _broker.GetOptionPrice(optionSymbol);
    }
}
