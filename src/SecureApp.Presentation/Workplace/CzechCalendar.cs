namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Czech public holidays and weekends, for labelling Rozpis days that have no roster entry of their own
/// (2026-09-26, user's own ask: "Bez záznamu" only when nothing has been recorded yet — a weekend says
/// "Víkend", a public holiday says its name). Pure date arithmetic, no network: the holiday list is
/// fixed by Czech law (zákon č. 245/2000 Sb.), and the two movable ones follow Easter.
/// </summary>
public static class CzechCalendar
{
    private static readonly Dictionary<(int Month, int Day), string> FixedHolidays = new()
    {
        [(1, 1)] = "Den obnovy samostatného českého státu",
        [(5, 1)] = "Svátek práce",
        [(5, 8)] = "Den vítězství",
        [(7, 5)] = "Den slovanských věrozvěstů Cyrila a Metoděje",
        [(7, 6)] = "Den upálení mistra Jana Husa",
        [(9, 28)] = "Den české státnosti",
        [(10, 28)] = "Den vzniku samostatného československého státu",
        [(11, 17)] = "Den boje za svobodu a demokracii",
        [(12, 24)] = "Štědrý den",
        [(12, 25)] = "1. svátek vánoční",
        [(12, 26)] = "2. svátek vánoční",
    };

    /// <summary>The holiday's name, or null for an ordinary day.</summary>
    public static string? HolidayName(DateOnly date)
    {
        if (FixedHolidays.TryGetValue((date.Month, date.Day), out var name))
            return name;

        var easterSunday = EasterSunday(date.Year);
        if (date == easterSunday.AddDays(-2)) return "Velký pátek";
        if (date == easterSunday.AddDays(1)) return "Velikonoční pondělí";
        return null;
    }

    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>"Den české státnosti" on a holiday, "Víkend" on a Saturday/Sunday, otherwise empty. A holiday falling on a weekend shows its name.</summary>
    public static string DayKindLabel(DateOnly date) =>
        HolidayName(date) ?? (IsWeekend(date) ? "Víkend" : string.Empty);

    /// <summary>Gregorian Easter Sunday (the anonymous "Meeus/Jones/Butcher" algorithm).</summary>
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
