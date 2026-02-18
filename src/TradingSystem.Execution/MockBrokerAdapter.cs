using TradingSystem.Core.Models;
using TradingSystem.Execution.Interfaces;

namespace TradingSystem.Execution;

public class MockBrokerAdapter : IBrokerAdapter
{
    private decimal _currentSpotPrice = 22000m;
    private readonly Random _random = new();

    public Task<List<Option>> GetOptionChain(string underlying, DateTime? expiry = null)
    {
        var options = new List<Option>();
        var targetExpiry = expiry ?? OptionsSelector.GetNearestWeeklyExpiry(DateTime.Now);

        for (int i = -5; i <= 5; i++)
        {
            var strike = Math.Round(_currentSpotPrice / 50) * 50 + (i * 50);

            options.Add(new Option
            {
                Symbol = $"{underlying}{targetExpiry:yyMMdd}C{strike}",
                Strike = strike,
                Type = TradeDirection.CALL,
                Expiry = targetExpiry,
                LastPrice = Math.Max(1, _currentSpotPrice - strike + 50),
                Bid = Math.Max(1, _currentSpotPrice - strike + 45),
                Ask = Math.Max(1, _currentSpotPrice - strike + 55),
                Volume = _random.Next(100, 10000),
                ImpliedVolatility = 15 + _random.Next(-5, 5)
            });

            options.Add(new Option
            {
                Symbol = $"{underlying}{targetExpiry:yyMMdd}P{strike}",
                Strike = strike,
                Type = TradeDirection.PUT,
                Expiry = targetExpiry,
                LastPrice = Math.Max(1, strike - _currentSpotPrice + 50),
                Bid = Math.Max(1, strike - _currentSpotPrice + 45),
                Ask = Math.Max(1, strike - _currentSpotPrice + 55),
                Volume = _random.Next(100, 10000),
                ImpliedVolatility = 15 + _random.Next(-5, 5)
            });
        }

        return Task.FromResult(options);
    }

    public async Task<Option?> GetATMOption(string underlying, decimal spotPrice, TradeDirection direction, DateTime? expiry = null)
    {
        _currentSpotPrice = spotPrice;
        var chain = await GetOptionChain(underlying, expiry);
        return OptionsSelector.SelectATMOption(chain, spotPrice, direction);
    }

    public Task<string> PlaceOrder(Option option, int quantity, string orderType = "MARKET")
    {
        var orderId = $"ORD{DateTime.Now:yyyyMMddHHmmss}{_random.Next(1000, 9999)}";
        return Task.FromResult(orderId);
    }

    public Task<bool> CancelOrder(string orderId)
    {
        return Task.FromResult(true);
    }

    public Task<decimal> GetOptionPrice(string optionSymbol)
    {
        var basePrice = 50m + (decimal)_random.NextDouble() * 50;
        return Task.FromResult(basePrice);
    }

    public Task<decimal> GetSpotPrice(string symbol)
    {
        _currentSpotPrice += (decimal)(_random.NextDouble() - 0.5) * 10;
        return Task.FromResult(_currentSpotPrice);
    }

    public Task<bool> IsMarketOpen()
    {
        var now = DateTime.Now.TimeOfDay;
        return Task.FromResult(now >= new TimeSpan(9, 15, 0) && now <= new TimeSpan(15, 30, 0));
    }

    public void SetSpotPrice(decimal price)
    {
        _currentSpotPrice = price;
    }
}
