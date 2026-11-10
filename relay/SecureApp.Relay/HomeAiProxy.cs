namespace SecureApp.Relay;

/// <summary>
/// Streams the admin's phone to the LM Studio on their home PC (2026-11-10, "Domácí AI"): the phone is rarely on the home
/// Wi-Fi or in the WireGuard tunnel (it reaches the relay over the public HTTPS address), so it cannot talk to the PC
/// directly. The PC keeps an outbound SSH tunnel to this Pi, a small forwarder on the Pi offers it at
/// <c>SECUREAPP_HOMEAI_UPSTREAM</c>, and this proxy hands the phone's requests to it over the relay's own pinned HTTPS.
///
/// Admin-role devices only (device_policy role 0), only four LM Studio paths, nothing stored and nothing logged — the
/// prompt and the answer are just copied through. The relay therefore sees the plaintext of these private chats while it
/// passes them (the Pi is the user's own machine at home; nothing leaves it).
/// </summary>
public static class HomeAiProxy
{
    private static readonly HttpClient Upstream = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly HashSet<string> AllowedPaths = new(StringComparer.Ordinal)
    {
        "api/v1/models",
        "api/v1/models/load",
        "api/v1/models/unload",
        "v1/chat/completions",
    };

    public static void MapHomeAiProxy(this WebApplication app, string upstreamBaseUrl)
    {
        var upstream = upstreamBaseUrl.TrimEnd('/');
        app.MapMethods("/homeai/{**path}", ["GET", "POST"], async (HttpContext context, string? path, RelayDatabase db) =>
        {
            var request = context.Request;
            var response = context.Response;

            var deviceIdHeader = request.Headers["X-Device-Id"].ToString();
            var secretHeader = request.Headers["X-Device-Secret"].ToString();
            if (!Guid.TryParse(deviceIdHeader, out var deviceId) || string.IsNullOrEmpty(secretHeader) || !db.TryAuthenticate(deviceId, secretHeader))
            {
                response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (db.GetDevicePolicy(deviceId)?.Role != 0)
            {
                await WriteErrorAsync(response, StatusCodes.Status403Forbidden, "Domácí AI je dostupná jen Adminovi.");
                return;
            }

            if (path is null || !AllowedPaths.Contains(path))
            {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            using var message = new HttpRequestMessage(new HttpMethod(request.Method), $"{upstream}/{path}");
            if (request.ContentLength is > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
            {
                message.Content = new StreamContent(request.Body);
                if (request.ContentType is { Length: > 0 } contentType)
                    message.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                if (request.ContentLength is { } length)
                    message.Content.Headers.ContentLength = length;
            }

            try
            {
                using var answer = await Upstream.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                response.StatusCode = (int)answer.StatusCode;
                response.ContentType = answer.Content.Headers.ContentType?.ToString() ?? "application/json";
                response.Headers.CacheControl = "no-store";
                response.Headers["X-Accel-Buffering"] = "no";

                // Copied piece by piece and flushed at once, so a streamed chat answer reaches the phone word by word.
                await using var body = await answer.Content.ReadAsStreamAsync(context.RequestAborted);
                var buffer = new byte[8192];
                int read;
                while ((read = await body.ReadAsync(buffer, context.RequestAborted)) > 0)
                {
                    await response.Body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                    await response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (HttpRequestException)
            {
                if (!response.HasStarted)
                    await WriteErrorAsync(response, StatusCodes.Status503ServiceUnavailable, "Domácí PC neodpovídá. Zkontrolujte, že je zapnuté a přihlášené a že běží LM Studio.");
            }
            catch (OperationCanceledException)
            {
                // The phone went away (Stop pressed) — nothing to answer.
            }
        });
    }

    private static async Task WriteErrorAsync(HttpResponse response, int status, string message)
    {
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        await response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { error = message }));
    }
}
