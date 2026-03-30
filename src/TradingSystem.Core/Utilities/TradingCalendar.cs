using Microsoft.Extensions.Logging;

namespace TradingSystem.Core.Utilities;

/// <summary>
/// Determines whether a given date is a valid NSE trading day.
/// All holiday data sourced directly from official NSE circulars:
///   2022 — NSE/CMTR/50560 (Dec 10, 2021)
///   2023 — NSE/CMTR/54757 (Dec 08, 2022)
///   2024 — NSE/CMTR/59722 (Dec 12, 2023)
///   2025 — NSE/CMTR/65587 (Dec 13, 2024)
///   2026 — NSE/CMTR/71775 (Dec 12, 2025)
///
/// MAINTENANCE: Update every December when NSE publishes the next year's circular.
/// URL pattern: https://nsearchives.nseindia.com/content/circulars/CMTR[ref].pdf
/// </summary>
public static class TradingCalendar
{
    // Only weekday holidays are listed here.
    // Weekend holidays are already handled by the Saturday/Sunday check below.
    // Source: NSE/CMTR/50560 dated December 10, 2021
    private static readonly HashSet<DateOnly> Holidays2022 =
    [
        new(2022, 1, 26),   // Republic Day
        new(2022, 3, 1),    // Mahashivratri
        new(2022, 3, 18),   // Holi
        new(2022, 4, 14),   // Dr. Baba Saheb Ambedkar Jayanti / Mahavir Jayanti
        new(2022, 4, 15),   // Good Friday
        new(2022, 5, 3),    // Id-Ul-Fitr (Ramzan ID)
        new(2022, 8, 9),    // Moharram
        new(2022, 8, 15),   // Independence Day
        new(2022, 8, 31),   // Ganesh Chaturthi
        new(2022, 10, 5),   // Dussehra
        new(2022, 10, 24),  // Diwali-Laxmi Pujan (Muhurat Trading day)
        new(2022, 10, 26),  // Diwali-Balipratipada
        new(2022, 11, 8),   // Gurunanak Jayanti
    ];

    // Source: NSE/CMTR/54757 dated December 08, 2022
    private static readonly HashSet<DateOnly> Holidays2023 =
    [
        new(2023, 1, 26),   // Republic Day
        new(2023, 3, 7),    // Holi
        new(2023, 3, 30),   // Ram Navami
        new(2023, 4, 4),    // Mahavir Jayanti
        new(2023, 4, 7),    // Good Friday
        new(2023, 4, 14),   // Dr. Baba Saheb Ambedkar Jayanti
        new(2023, 5, 1),    // Maharashtra Day
        new(2023, 6, 28),   // Bakri Id
        new(2023, 8, 15),   // Independence Day
        new(2023, 9, 19),   // Ganesh Chaturthi
        new(2023, 10, 2),   // Mahatma Gandhi Jayanti
        new(2023, 10, 24),  // Dussehra
        new(2023, 11, 14),  // Diwali-Balipratipada
        new(2023, 11, 27),  // Gurunanak Jayanti
        new(2023, 12, 25),  // Christmas
        // Note: Diwali Laxmi Pujan (Nov 12) fell on Sunday — excluded
    ];

    // Source: NSE/CMTR/59722 dated December 12, 2023
    private static readonly HashSet<DateOnly> Holidays2024 =
    [
        new(2024, 1, 22),   // Ayodhya Ram Mandir consecration (special holiday)
        new(2024, 1, 26),   // Republic Day
        new(2024, 3, 8),    // Mahashivratri
        new(2024, 3, 25),   // Holi
        new(2024, 3, 29),   // Good Friday
        new(2024, 4, 11),   // Id-Ul-Fitr (Ramadan Eid)
        new(2024, 4, 17),   // Shri Ram Navami
        new(2024, 5, 1),    // Maharashtra Day
        new(2024, 5, 20),   // General Election Day (special holiday, NSE/CMTR/61518)
        new(2024, 6, 17),   // Bakri Id
        new(2024, 7, 17),   // Moharram
        new(2024, 8, 15),   // Independence Day / Parsi New Year
        new(2024, 10, 2),   // Mahatma Gandhi Jayanti
        new(2024, 11, 1),   // Diwali Laxmi Pujan (Muhurat Trading day)
        new(2024, 11, 15),  // Gurunanak Jayanti
        new(2024, 12, 25),  // Christmas
        // Note: Ambedkar Jayanti (Apr 14), Mahavir Jayanti (Apr 21),
        //       Ganesh Chaturthi (Sep 7), Dussehra (Oct 12),
        //       Diwali-Balipratipada (Nov 2) all fell on Saturday/Sunday — excluded
    ];

    // Source: NSE/CMTR/65587 dated December 13, 2024
    private static readonly HashSet<DateOnly> Holidays2025 =
    [
        new(2025, 2, 26),   // Mahashivratri
        new(2025, 3, 14),   // Holi
        new(2025, 3, 31),   // Id-Ul-Fitr (Ramadan Eid)
        new(2025, 4, 10),   // Shri Mahavir Jayanti
        new(2025, 4, 14),   // Dr. Baba Saheb Ambedkar Jayanti
        new(2025, 4, 18),   // Good Friday
        new(2025, 5, 1),    // Maharashtra Day
        new(2025, 8, 15),   // Independence Day
        new(2025, 8, 27),   // Ganesh Chaturthi
        new(2025, 10, 2),   // Mahatma Gandhi Jayanti
        new(2025, 10, 2),   // Dussehra (same date as Gandhi Jayanti in 2025)
        new(2025, 10, 21),  // Diwali-Laxmi Pujan (Muhurat Trading day)
        new(2025, 10, 22),  // Diwali-Balipratipada
        new(2025, 11, 5),   // Prakash Gurpurb Sri Guru Nanak Dev Ji
        new(2025, 12, 25),  // Christmas
        // Note: Ram Navami (Apr 6) fell on Sunday — excluded
    ];

    // Source: NSE/CMTR/71775 dated December 12, 2025
    private static readonly HashSet<DateOnly> Holidays2026 =
    [
        new(2026, 1, 26),   // Republic Day
        new(2026, 3, 3),    // Holi
        new(2026, 3, 26),   // Shri Ram Navami
        new(2026, 3, 31),   // Shri Mahavir Jayanti
        new(2026, 4, 3),    // Good Friday
        new(2026, 4, 14),   // Dr. Baba Saheb Ambedkar Jayanti
        new(2026, 5, 1),    // Maharashtra Day
        new(2026, 5, 28),   // Bakri Id
        new(2026, 6, 26),   // Muharram
        new(2026, 9, 14),   // Ganesh Chaturthi
        new(2026, 10, 2),   // Mahatma Gandhi Jayanti
        new(2026, 10, 20),  // Dussehra
        new(2026, 11, 10),  // Diwali-Balipratipada
        new(2026, 11, 24),  // Prakash Gurpurb Sri Guru Nanak Dev Ji
        new(2026, 12, 25),  // Christmas
        // Note: Mahashivratri (Feb 15), Id-Ul-Fitr (Mar 21),
        //       Independence Day (Aug 15), Diwali Laxmi Pujan (Nov 8)
        //       all fell on Saturday/Sunday — excluded.
        //       Muhurat Trading on Nov 8 (Sunday) will be notified separately.
    ];

    private static readonly IReadOnlyDictionary<int, HashSet<DateOnly>> HolidaysByYear =
        new Dictionary<int, HashSet<DateOnly>>
        {
            { 2022, Holidays2022 },
            { 2023, Holidays2023 },
            { 2024, Holidays2024 },
            { 2025, Holidays2025 },
            { 2026, Holidays2026 },
        };

    /// <summary>
    /// Returns true if NSE was/is open for trading on this date.
    /// </summary>
    public static bool IsTradingDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        if (HolidaysByYear.TryGetValue(date.Year, out var holidays))
            return !holidays.Contains(date);

        // Year not in our table — weekday-only fallback.
        // This will log a warning at startup via ValidateHolidayData().
        return true;
    }

    public static bool IsTradingDay(DateTime date) =>
        IsTradingDay(DateOnly.FromDateTime(date));

    /// <summary>
    /// Counts actual NSE trading days between two dates, inclusive.
    /// </summary>
    public static int CountTradingDays(DateOnly from, DateOnly to)
    {
        int count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (IsTradingDay(d)) count++;
        return count;
    }

    public static int CountTradingDays(DateTime from, DateTime to) =>
        CountTradingDays(DateOnly.FromDateTime(from), DateOnly.FromDateTime(to));

    /// <summary>
    /// Call at application startup to catch missing holiday data before it causes
    /// silent fallback to weekday-only filtering.
    /// Checks current year and next year (so you're warned before Jan 1).
    /// </summary>
    public static void ValidateHolidayData(ILogger logger)
    {
        var currentYear = DateTime.UtcNow.Year;

        foreach (var year in new[] { currentYear, currentYear + 1 })
        {
            if (!HolidaysByYear.ContainsKey(year))
            {
                logger.LogWarning(
                    "TradingCalendar: No holiday data for {Year}. " +
                    "Weekend-only gap filtering is active — fetch the NSE circular and update TradingCalendar.cs. " +
                    "URL pattern: https://nsearchives.nseindia.com/content/circulars/CMTR[ref].pdf",
                    year);
            }
        }
    }
}
