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
/// </summary>
public sealed class ConnectionRegistry
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    private sealed record Connection(WebSocket Socket, SemaphoreSlim SendLock);

    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();

    public void Add(Guid deviceId, WebSocket socket) => _connections[deviceId] = new Connection(socket, new SemaphoreSlim(1, 1));

    public Guid[] ConnectedDeviceIds() => _connections.Keys.ToArray();

    public void Remove(Guid deviceId, WebSocket socket)
    {
        if (_connections.TryGetValue(deviceId, out var connection) && ReferenceEquals(connection.Socket, socket))
            _connections.TryRemove(new KeyValuePair<Guid, Connection>(deviceId, connection));
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
