using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace SecureApp.Relay;

/// <summary>
/// In-memory table of currently-connected devices. Deliberately not persisted — a disconnect (or
/// relay restart) simply means "not currently reachable live," which is exactly what
/// <c>RelayDatabase</c>'s outbox exists to cover.
/// </summary>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _connections = new();

    public void Add(Guid deviceId, WebSocket socket) => _connections[deviceId] = socket;

    public void Remove(Guid deviceId, WebSocket socket) =>
        _connections.TryRemove(new KeyValuePair<Guid, WebSocket>(deviceId, socket));

    public bool TryGet(Guid deviceId, out WebSocket socket)
    {
        var found = _connections.TryGetValue(deviceId, out var value);
        socket = value!;
        return found && socket.State == WebSocketState.Open;
    }
}
