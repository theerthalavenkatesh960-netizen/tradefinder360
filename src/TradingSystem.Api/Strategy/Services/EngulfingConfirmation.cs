using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Strategy.Services;

public sealed class EngulfingConfirmation : IEngulfingConfirmation
{
    public bool IsEngulfing(Candle previous, Candle current, Direction direction)
    {
        if (direction == Direction.Bullish)
        {
            return current.Open < previous.Low && current.Close > previous.High;
        }
        else
        {
            return current.Open > previous.High && current.Close < previous.Low;
        }
    }
}
