namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Week arithmetic shared by the Rozpis (<c>WorkplaceViewModel</c>), the Pozice board and the background
/// roster publish (2026-10-24): Monday-based weeks, the week a view opens on, and its Czech label.
/// </summary>
public static class WeekMath
{
    private static readonly string[] CzechMonthGenitive =
    [
        "ledna", "února", "března", "dubna", "května", "června",
        "července", "srpna", "září", "října", "listopadu", "prosince"
    ];

    public static DateOnly StartOfWeek(DateOnly date)
    {
        var diff = (int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1;
        return date.AddDays(-diff);
    }

    /// <summary>The week a Rozpis/Pozice view opens on: from Saturday on the working week is over, so it is the next one (user's ask, 2026-10-24: "v sobotu uz bych chtel videt rozpis na dalsi tyden").</summary>
    public static DateOnly DefaultViewWeek(DateOnly today) =>
        today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? StartOfWeek(today).AddDays(7) : StartOfWeek(today);

    public static string FormatWeekLabel(DateOnly start, DateOnly end)
    {
        if (start.Month == end.Month)
            return $"{start.Day}.–{end.Day}. {CzechMonthGenitive[end.Month - 1]} {end.Year}";
        return $"{start.Day}. {CzechMonthGenitive[start.Month - 1]} – {end.Day}. {CzechMonthGenitive[end.Month - 1]} {end.Year}";
    }
}
