using System.Net.WebSockets;
using System.Text.Json;
using WireFrame = SecureApp.Domain.ValueObjects.WireFrame;

namespace SecureApp.Relay;

/// <summary>WebSocket message framing for <see cref="WireFrame"/> — one WebSocket message per JSON frame, accumulating multi-part receives.</summary>
public static class RelayProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public static async Task<WireFrame?> ReceiveFrameAsync(WebSocket socket, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var segment = new byte[8192];

        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(segment, ct);
            }
            catch (WebSocketException)
            {
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            buffer.Write(segment, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        if (buffer.Length == 0)
            return null;

        buffer.Position = 0;
        try
        {
            return await JsonSerializer.DeserializeAsync<WireFrame>(buffer, JsonOptions, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Task SendFrameAsync(WebSocket socket, WireFrame frame, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
        return socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public static string Serialize(WireFrame frame) => JsonSerializer.Serialize(frame, JsonOptions);
}
