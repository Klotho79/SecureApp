namespace SecureApp.Relay;

/// <summary>
/// Sets every sál back to "Volno" (note cleared) after local midnight (2026-11-10, user's ask: "stavy sálu by se
/// měly o půlnoci vyresetovat na volno"). Runs on the relay, so it happens whether or not any phone is open.
/// A row is reset when it was last changed before the start of the current local day — checked every minute
/// instead of waiting for one exact tick, so a relay restart or redeploy around midnight still catches up, and
/// a status somebody sets just after midnight is left alone.
/// </summary>
public sealed class RoomStatusMidnightResetService(RelayDatabase db, ILogger<RoomStatusMidnightResetService> logger) : BackgroundService
{
    /// <summary>The curated "everything is fine" status (first entry of the status picker).</summary>
    private const string FreeStatusText = "Volno";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeZoneInfo LocalZone = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"); }
        catch { return TimeZoneInfo.Local; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        do
        {
            try
            {
                var nowLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, LocalZone);
                var startOfLocalDay = new DateTimeOffset(nowLocal.Date, LocalZone.GetUtcOffset(nowLocal.Date));
                var changed = db.ResetStaleRoomStatuses(startOfLocalDay.ToUniversalTime(), FreeStatusText);
                if (changed > 0)
                    logger.LogInformation("Room statuses reset to '{Status}' after midnight: {Count} sál(s).", FreeStatusText, changed);
            }
            catch (Exception ex)
            {
                // Best-effort — never take the relay down; the next minute tries again.
                logger.LogWarning(ex, "Midnight room-status reset failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
