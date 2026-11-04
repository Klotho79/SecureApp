using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace SecureApp.Relay;

/// <summary>
/// In-memory table of currently-connected devices. Deliberately not persisted — a disconnect (or
/// relay restart) simply means "not currently reachable live," which is exactly what
/// <c>RelayDatabase</c>'s outbox exists to cover.
///
/// Every send to a device goes through <see cref="TrySendAsync"/>, which serializes sends per socket:
/// <see cref="WebSocket.SendAsync(ReadOnlyMemory{byte}, WebSocketMessageType, bool, CancellationToken)"/>
/// must not be called concurrently on one socket, and two senders messaging the same recipient (or a
/// live delivery landing mid outbox-flush) would otherwise throw InvalidOperationException — which
/// used to tear down the SENDER's connection and lose the message.
///
/// Self-repair (2026-11-04, user: "relay resi proc a pokusi se opravit spojeni"): when a delivery to a device that is
/// registered as connected fails, the connection is dead in practice (half-open socket after a network change, a stuck
/// send). <see cref="RecoverStaleConnection"/> records why and drops that socket; the app's own reconnect then
/// authenticates again and <see cref="FlushOutboxAsync"/> delivers what was queued. Event-driven, not a timer, and
/// limited to one drop per device per <see cref="RecoverCooldown"/> so a device that keeps failing cannot cause a loop.
/// A device that is simply offline is not touched: its messages wait in the outbox for its next login.
/// </summary>
public sealed class ConnectionRegistry(ILogger<ConnectionRegistry> logger)
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecoverCooldown = TimeSpan.FromSeconds(60);

    private sealed record Connection(WebSocket Socket, SemaphoreSlim SendLock);

    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _flushLocks = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastRecovery = new();

    public void Add(Guid deviceId, WebSocket socket) => _connections[deviceId] = new Connection(socket, new SemaphoreSlim(1, 1));

    public Guid[] ConnectedDeviceIds() => _connections.Keys.ToArray();

    public bool IsConnected(Guid deviceId) => _connections.ContainsKey(deviceId);

    public void Remove(Guid deviceId, WebSocket socket)
    {
        if (_connections.TryGetValue(deviceId, out var connection) && ReferenceEquals(connection.Socket, socket))
            _connections.TryRemove(new KeyValuePair<Guid, Connection>(deviceId, connection));
    }

    /// <summary>
    /// Called when a frame for <paramref name="deviceId"/> could not be delivered although the device is registered as
    /// connected. Logs the socket's state as the reason and aborts the dead socket so the device reconnects (its receive
    /// loop ends, <see cref="Remove"/> runs, the app re-authenticates and the outbox is flushed). Returns whether it acted.
    /// </summary>
    public bool RecoverStaleConnection(Guid deviceId, string trigger)
    {
        if (!_connections.TryGetValue(deviceId, out var connection))
            return false; // Offline, not stale: nothing to repair, the outbox waits for the next login.

        var now = DateTimeOffset.UtcNow;
        if (_lastRecovery.TryGetValue(deviceId, out var last) && now - last < RecoverCooldown)
        {
            logger.LogWarning("Delivery to {DeviceId} failed again ({Trigger}) within {Cooldown}s of the last repair — leaving the connection alone",
                deviceId, trigger, (int)RecoverCooldown.TotalSeconds);
            return false;
        }

        _lastRecovery[deviceId] = now;
        logger.LogWarning("Delivery to {DeviceId} failed while registered as connected ({Trigger}, socket state {State}) — dropping the stale connection so the app reconnects and the outbox is flushed",
            deviceId, trigger, connection.Socket.State);
        try
        {
            connection.Socket.Abort();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or WebSocketException)
        {
            // Already gone — the receive loop's finally removes the entry.
        }

        return true;
    }

    /// <summary>
    /// Sends a device's queued frames, oldest first; a frame leaves the outbox only once it was actually sent, so a
    /// failure keeps the rest. One flush per device at a time: two concurrent flushes would send the same frame twice,
    /// which a client's ratchet treats as a broken session. If a send fails while the device is still registered as
    /// connected, the connection is repaired via <see cref="RecoverStaleConnection"/>.
    /// </summary>
    public async Task FlushOutboxAsync(Guid deviceId, RelayDatabase db, TimeSpan pacing)
    {
        var gate = _flushLocks.GetOrAdd(deviceId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var flushed = 0;
            foreach (var (frameId, frameJson) in db.PeekOutbox(deviceId))
            {
                if (flushed++ > 0)
                    await Task.Delay(pacing);
                if (!await TrySendAsync(deviceId, frameJson))
                {
                    RecoverStaleConnection(deviceId, "outbox flush");
                    break;
                }

                db.DeleteOutboxFrame(frameId);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Sends one already-serialized frame to a connected device. Returns false (never throws) when the
    /// device isn't connected, its socket isn't open, or the send fails/times out — the caller then
    /// falls back to the outbox, so nothing is silently dropped.
    /// </summary>
    public async Task<bool> TrySendAsync(Guid deviceId, string frameJson)
    {
        if (!_connections.TryGetValue(deviceId, out var connection) || connection.Socket.State != WebSocketState.Open)
            return false;

        try
        {
            using var timeout = new CancellationTokenSource(SendTimeout);
            await connection.SendLock.WaitAsync(timeout.Token);
            try
            {
                if (connection.Socket.State != WebSocketState.Open)
                    return false;
                await connection.Socket.SendAsync(Encoding.UTF8.GetBytes(frameJson), WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
                return true;
            }
            finally
            {
                connection.SendLock.Release();
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }
}
