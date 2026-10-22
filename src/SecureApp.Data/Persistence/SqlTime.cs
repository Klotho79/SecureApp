using System.Globalization;

namespace SecureApp.Data.Persistence;

/// <summary>ISO-8601 round-trip ("O") text form every repository uses for DateTimeOffset columns (stored as TEXT) — one shared copy instead of one per repository.</summary>
internal static class SqlTime
{
    public static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    public static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
