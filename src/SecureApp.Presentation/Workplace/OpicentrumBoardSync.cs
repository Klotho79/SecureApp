using System.Collections.Concurrent;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Keeps this device's local copy of the relay-cached Opicentrum boards current without anyone opening
/// the page (2026-10-24, user's ask: "relay posilat aktivne pokud zjisti zmeny... kreslit se budou data
/// z apky"). The relay sends a "board-changed" frame to the other Admin/Modifier devices whenever a
/// snapshot is replaced; <see cref="OnBoardChanged"/> then downloads it in the background into the local
/// cache. While the WebSocket has stayed connected since the local copy was last confirmed against the
/// relay, nothing can have changed without a push — so opening the board skips the relay round trip
/// entirely (<see cref="IsCurrent"/>) and just draws what is stored locally.
///
/// "Confirmed" is tracked with an epoch that moves on every connection-state change and every push: a
/// confirmation only counts if the epoch is the same as when its fetch STARTED, so a push or a
/// reconnect that happened mid-fetch can never leave a stale copy marked as current.
/// </summary>
public static class OpicentrumBoardSync
{
    public const string Roster = "roster";
    public const string Volno = "volno";

    private static long _epoch;
    private static volatile bool _connected;
    private static readonly ConcurrentDictionary<string, long> VerifiedEpoch = new();

    /// <summary>Raised (on a thread-pool thread) after a pushed roster snapshot was saved locally — a page that is open redraws from it.</summary>
    public static event Action<OpicentrumRosterBoardSnapshot>? RosterPushed;

    /// <summary>Raised (on a thread-pool thread) after a pushed volno snapshot was saved locally.</summary>
    public static event Action<OpicentrumVolnoSnapshot>? VolnoPushed;

    /// <summary>Wire to <c>IMessageTransport.ConnectionStateChanged</c>: any change may have cost pushes, so earlier confirmations stop counting.</summary>
    public static void OnConnectionStateChanged(TransportConnectionState state)
    {
        _connected = state == TransportConnectionState.Connected;
        Interlocked.Increment(ref _epoch);
    }

    /// <summary>The epoch to capture BEFORE starting a relay fetch and hand to <see cref="MarkVerified"/> afterwards.</summary>
    public static long CurrentEpoch => Volatile.Read(ref _epoch);

    public static void MarkVerified(string board, long epochAtFetchStart) => VerifiedEpoch[board] = epochAtFetchStart;

    /// <summary>True when the local copy was confirmed against the relay during the current uninterrupted connection and no push has arrived since.</summary>
    public static bool IsCurrent(string board) =>
        _connected && VerifiedEpoch.TryGetValue(board, out var verified) && verified == Volatile.Read(ref _epoch);

    /// <summary>Wire to <c>IMessageTransport.BoardChanged</c>.</summary>
    public static void OnBoardChanged(string board)
    {
        Interlocked.Increment(ref _epoch); // an in-flight earlier fetch may predate this change
        _ = Task.Run(async () =>
        {
            try
            {
                var services = IPlatformApplication.Current?.Services;
                if (services is null) return;
                var epoch = CurrentEpoch;

                if (board == Roster && services.GetService<IOpicentrumRosterBoardService>() is { } roster)
                {
                    var fromRelay = await roster.GetCachedAsync();
                    if (fromRelay is null) return;
                    var local = OpicentrumRosterBoardLocalCache.Load();
                    if (local is null || fromRelay.UpdatedAtUtc > local.UpdatedAtUtc)
                    {
                        OpicentrumRosterBoardLocalCache.Save(fromRelay);
                        RosterPushed?.Invoke(fromRelay);
                    }
                    MarkVerified(Roster, epoch);
                }
                else if (board == Volno && services.GetService<IOpicentrumVolnoBoardService>() is { } volno)
                {
                    var fromRelay = await volno.GetCachedAsync();
                    if (fromRelay is null) return;
                    var local = OpicentrumVolnoBoardLocalCache.Load();
                    if (local is null || fromRelay.UpdatedAtUtc > local.UpdatedAtUtc)
                    {
                        OpicentrumVolnoBoardLocalCache.Save(fromRelay);
                        VolnoPushed?.Invoke(fromRelay);
                    }
                    MarkVerified(Volno, epoch);
                }
                AppLog.Event("opicentrum.board-push", ("board", board));
            }
            catch (Exception ex)
            {
                AppLog.Error(nameof(OpicentrumBoardSync), $"applying a pushed {board} board failed", ex);
            }
        });
    }
}
