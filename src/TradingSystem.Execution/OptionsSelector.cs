using TradingSystem.Core.Models;

namespace TradingSystem.Execution;

public class OptionsSelector
{
    public static Option? SelectATMOption(
        List<Option> optionChain,
        decimal spotPrice,
        TradeDirection direction)
    {
        if (optionChain.Count == 0)
            return null;

        var relevantOptions = optionChain
            .Where(o => o.Type == direction)
            .OrderBy(o => Math.Abs(o.Strike - spotPrice))
            .ToList();

        if (relevantOptions.Count == 0)
            return null;

        var atmOption = relevantOptions.First();
        atmOption.IsATM = true;

        return atmOption;
    }

    public static DateTime GetNearestWeeklyExpiry(DateTime currentDate)
    {
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)currentDate.DayOfWeek + 7) % 7;
        if (daysUntilThursday == 0 && currentDate.TimeOfDay > new TimeSpan(15, 30, 0))
        {
            daysUntilThursday = 7;
        }

        return currentDate.Date.AddDays(daysUntilThursday);
    }

    public static List<Option> FilterByExpiry(List<Option> options, DateTime targetExpiry)
    {
        return options.Where(o => o.Expiry.Date == targetExpiry.Date).ToList();
    }

    public static bool IsOptionLiquid(Option option, long minVolume = 100)
    {
        return option.Volume >= minVolume;
    }
}
