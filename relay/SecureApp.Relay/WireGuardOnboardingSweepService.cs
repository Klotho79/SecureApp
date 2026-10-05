namespace SecureApp.Relay;

/// <summary>
/// Revokes an onboarding WireGuard peer nobody ever actually used (2026-10-05, user's own ask).
/// <c>/admin/wireguard/clients</c> creates a real wg-easy peer the instant an admin walks a new
/// member through onboarding — that's real network access from that moment on, independent of
/// whether the person ever finishes installing the app. wg-easy itself has no concept of "unused";
/// nothing else revokes it. This sweeps <c>pending_wireguard_peers</c> every 10 minutes and, for any
/// entry older than an hour, deletes the peer from wg-easy UNLESS a new device was registered in
/// the relay's own <c>devices</c> table sometime in that hour — the best available "onboarding
/// actually completed" signal, given WireGuard access and SecureApp device identity are deliberately
/// separate systems (the wg-easy credential never leaves the Pi — see that endpoint's own remarks),
/// so there's no exact 1:1 link between a specific peer and a specific later device. Matches the
/// project's own documented onboarding workflow ("a new member is walked through this in person,
/// out loud, while their WireGuard tunnel is being handed over") closely enough in practice — one
/// onboarding at a time, not a precise guarantee for two overlapping ones within the same hour.
/// </summary>
public sealed class WireGuardOnboardingSweepService(RelayDatabase db, string? wgEasyUrl, string? wgEasyPassword) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxUnconfirmedAge = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(wgEasyUrl) || string.IsNullOrEmpty(wgEasyPassword))
            return; // Same tolerance as the endpoint that populates this table — nothing to sweep.

        using var wg = new HttpClient { BaseAddress = new Uri(wgEasyUrl) };
        wg.DefaultRequestHeaders.Add("Authorization", wgEasyPassword);

        using var timer = new PeriodicTimer(SweepInterval);
        do
        {
            try
            {
                await SweepOnceAsync(wg, stoppingToken);
            }
            catch
            {
                // Best-effort — never let one bad sweep (wg-easy briefly unreachable, etc.) take the
                // whole relay process down; the next tick tries again.
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepOnceAsync(HttpClient wg, CancellationToken ct)
    {
        var stale = db.GetStalePendingWireGuardPeers(DateTimeOffset.UtcNow - MaxUnconfirmedAge);
        foreach (var peer in stale)
        {
            if (db.AnyDeviceCreatedBetween(peer.CreatedAtUtc, peer.CreatedAtUtc + MaxUnconfirmedAge))
            {
                // A device showed up in the onboarding window — treat as confirmed, nothing to revoke.
                db.RemovePendingWireGuardPeer(peer.WgClientId);
                continue;
            }

            try
            {
                var response = await wg.DeleteAsync($"api/wireguard/client/{peer.WgClientId}", ct);
                // 404 means it's already gone (e.g. an admin deleted it by hand) — either way, nothing
                // left to revoke, so the pending record is cleared. Any OTHER failure leaves the
                // record in place for the next sweep to retry, rather than silently losing track of it.
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    db.RemovePendingWireGuardPeer(peer.WgClientId);
            }
            catch
            {
                // Best-effort — retried next sweep.
            }
        }
    }
}
