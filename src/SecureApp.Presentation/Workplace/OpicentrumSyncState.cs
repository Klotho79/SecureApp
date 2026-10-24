using System.Globalization;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// When the Opicentrum schedule was last synced successfully, and for which date range (2026-10-24, user:
/// "rozpis se otevira strasne dlouho"). Written by <see cref="OpicentrumSyncService.SyncAsync"/> itself, so
/// the ~10-minute background job and the Rozpis page share one answer to "is the local data fresh?" —
/// before, opening Rozpis ran its own full login + ~8 requests on nearly every open even though the
/// background job had just done the same. Persisted (per profile), so it also survives an app restart.
/// </summary>
public static class OpicentrumSyncState
{
    private const string Format = "yyyyMMdd";
    private static readonly object Gate = new();

    private static string TicksKey => Profiles.ActiveProfile.PrefKey("opicentrum_sync_ok_utc_ticks");
    private static string StartKey => Profiles.ActiveProfile.PrefKey("opicentrum_sync_ok_start");
    private static string EndKey => Profiles.ActiveProfile.PrefKey("opicentrum_sync_ok_end");

    private static (DateTimeOffset At, DateOnly Start, DateOnly End)? Load()
    {
        try
        {
            var prefs = Microsoft.Maui.Storage.Preferences.Default;
            var ticks = prefs.Get(TicksKey, 0L);
            if (ticks <= 0) return null;
            if (!DateOnly.TryParseExact(prefs.Get(StartKey, string.Empty), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                || !DateOnly.TryParseExact(prefs.Get(EndKey, string.Empty), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                return null;
            return (new DateTimeOffset(ticks, TimeSpan.Zero), start, end);
        }
        catch
        {
            return null; // unreadable preferences just mean "not fresh" — the next sync rewrites them
        }
    }

    /// <summary>Call after a sync that actually reached Opicentrum and wrote back successfully.</summary>
    public static void RecordOk(DateOnly start, DateOnly end)
    {
        try
        {
            lock (Gate)
            {
                // A narrower sync (one week from the page) must not shrink the range a wider recent one (the
                // background job: last week through next month) already vouches for.
                if (Load() is { } current && DateTimeOffset.UtcNow - current.At < TimeSpan.FromMinutes(10) && start >= current.Start && end <= current.End)
                    return;

                var prefs = Microsoft.Maui.Storage.Preferences.Default;
                prefs.Set(StartKey, start.ToString(Format, CultureInfo.InvariantCulture));
                prefs.Set(EndKey, end.ToString(Format, CultureInfo.InvariantCulture));
                prefs.Set(TicksKey, DateTimeOffset.UtcNow.UtcTicks);
            }
        }
        catch
        {
            // best-effort bookkeeping — a failed write only costs one extra sync later
        }
    }

    /// <summary>True when a successful sync covering the whole of [start, end] finished within <paramref name="window"/>.</summary>
    public static bool IsFresh(DateOnly start, DateOnly end, TimeSpan window)
    {
        lock (Gate)
        {
            return Load() is { } s && DateTimeOffset.UtcNow - s.At < window && start >= s.Start && end <= s.End;
        }
    }

    /// <summary>The status line shown next to "Dnes" when the page did not sync itself because the data was already fresh.</summary>
    public static string? DescribeLastOk()
    {
        lock (Gate)
        {
            return Load() is { } s ? $"Synchronizováno v {s.At.ToLocalTime():HH:mm}." : null;
        }
    }
}
