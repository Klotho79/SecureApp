using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using SecureApp.Relay;

var builder = WebApplication.CreateBuilder(args);

var adminSecret = builder.Configuration["SECUREAPP_RELAY_ADMIN_SECRET"]
    ?? throw new InvalidOperationException("SECUREAPP_RELAY_ADMIN_SECRET must be set (environment variable or configuration) — the relay refuses to start without it, rather than silently allow unauthenticated admin access.");

var port = builder.Configuration["SECUREAPP_RELAY_PORT"] ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Same directory RelayDatabase resolves SECUREAPP_RELAY_DB_PATH into (the mounted /data volume in
// production) — reused here rather than introducing a second env var, since the marker just needs
// to land somewhere that survives a container restart and is visible to a host-side watcher.
var deployMarkerPath = Path.Combine(
    Path.GetDirectoryName(builder.Configuration["SECUREAPP_RELAY_DB_PATH"]) is { Length: > 0 } dbDir ? dbDir : AppContext.BaseDirectory,
    "deploy-requested");

builder.Services.AddSingleton<RelayDatabase>();
builder.Services.AddSingleton<ConnectionRegistry>();

var app = builder.Build();

app.Services.GetRequiredService<RelayDatabase>().Initialize();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok());

app.MapPost("/admin/invites", (HttpRequest request, CreateInviteRequest body, RelayDatabase db) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    var code = db.CreateInvite(body.DisplayNameHint, TimeSpan.FromMinutes(body.ValidForMinutes), out var expiresAtUtc);
    return Results.Ok(new CreateInviteResponse(code, expiresAtUtc));
});

app.MapPost("/admin/devices", (HttpRequest request, CreateDeviceRequest body, RelayDatabase db) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    var (deviceId, secret) = db.CreateDevice(body.DisplayName);
    return Results.Ok(new DeviceCredentialResponse(deviceId, secret));
});

// The relay only ever REQUESTS a redeploy — it never runs `docker compose` on itself. Doing that
// from inside the very container being rebuilt would need the host's Docker socket mounted in
// (root-equivalent host access from inside a container — the kind of privilege this app's whole
// threat model tries to avoid granting anywhere). Instead this just drops a marker file into the
// already-mounted /data volume; a host-side systemd watcher (see relay/ops/) does the actual
// `docker compose build && up -d`, with real Docker access but no HTTP surface of its own.
app.MapPost("/admin/deploy", (HttpRequest request) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    File.WriteAllText(deployMarkerPath, DateTimeOffset.UtcNow.ToString("O"));
    return Results.Accepted(value: new { queued = true, message = "Redeploy requested — the host watcher picks this up within a few seconds." });
});

app.MapPost("/register", (RegisterRequest body, RelayDatabase db) =>
{
    if (!db.TryConsumeInvite(body.InviteCode))
        return Results.BadRequest("Invite code is invalid, already used, or expired.");

    var (deviceId, secret) = db.CreateDevice(body.DisplayName);
    return Results.Ok(new DeviceCredentialResponse(deviceId, secret));
});

app.MapPost("/library/files", async (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out var deviceId))
        return Results.Unauthorized();

    var folderPath = request.Query["folderPath"].ToString();
    var fileName = request.Query["fileName"].ToString();
    var tagsRaw = request.Query["tags"].ToString();
    var expectedHash = request.Query["contentHash"].ToString();

    if (string.IsNullOrWhiteSpace(fileName))
        return Results.BadRequest("fileName is required.");

    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    var contentBytes = buffer.ToArray();

    if (contentBytes.Length == 0)
        return Results.BadRequest("Request body (ciphertext) was empty.");

    var actualHash = Convert.ToHexStringLower(SHA256.HashData(contentBytes));
    if (!string.IsNullOrEmpty(expectedHash) && !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("contentHash did not match the uploaded bytes.");

    var tags = tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var record = db.InsertLibraryFileMetadata(folderPath, fileName, tags, contentBytes.LongLength, actualHash, deviceId);

    await File.WriteAllBytesAsync(db.GetLibraryFilePath(record.Id), contentBytes);

    return Results.Ok(ToLibraryFileDto(record));
});

app.MapGet("/library/files", (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    var query = request.Query["query"].ToString();
    var folderPath = request.Query["folderPath"].ToString();
    var tag = request.Query["tag"].ToString();

    var results = db.SearchLibraryFiles(
        string.IsNullOrEmpty(query) ? null : query,
        string.IsNullOrEmpty(folderPath) ? null : folderPath,
        string.IsNullOrEmpty(tag) ? null : tag);

    return Results.Ok(results.Select(ToLibraryFileDto).ToList());
});

app.MapGet("/library/files/{id:guid}", async (Guid id, HttpRequest request, HttpResponse response, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    var record = db.GetLibraryFile(id);
    var path = db.GetLibraryFilePath(id);
    if (record is null || !File.Exists(path))
        return Results.NotFound();

    // Carried as headers (URL-encoded — header values must be ASCII-safe) so the client can
    // import the file under its original name in one round trip, no separate metadata fetch.
    response.Headers["X-File-Name"] = Uri.EscapeDataString(record.FileName);
    response.Headers["X-Folder-Path"] = Uri.EscapeDataString(record.FolderPath);

    var bytes = await File.ReadAllBytesAsync(path);
    return Results.Bytes(bytes, "application/octet-stream");
});

app.MapDelete("/library/files/{id:guid}", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    var isAdmin = IsAdminAuthorized(request, adminSecret);
    var isDeviceAuthed = TryGetDeviceAuth(request, db, out var callerDeviceId);
    if (!isAdmin && !isDeviceAuthed)
        return Results.Unauthorized();

    if (!db.TryDeleteLibraryFile(id, callerDeviceId, isAdmin))
        return Results.NotFound();

    var path = db.GetLibraryFilePath(id);
    if (File.Exists(path))
        File.Delete(path);

    return Results.NoContent();
});

app.Map("/ws", async (HttpContext context, RelayDatabase db, ConnectionRegistry registry) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var ct = context.RequestAborted;

    var authFrame = await RelayProtocol.ReceiveFrameAsync(socket, ct);
    if (authFrame is not { Type: "auth", DeviceId: { } deviceId, Secret: { } secret } || !db.TryAuthenticate(deviceId, secret))
    {
        if (socket.State == WebSocketState.Open)
        {
            await RelayProtocol.SendFrameAsync(socket, new RelayFrame { Type = "authResult", Success = false }, ct);
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Authentication failed", CancellationToken.None);
        }
        return;
    }

    await RelayProtocol.SendFrameAsync(socket, new RelayFrame { Type = "authResult", Success = true }, ct);
    registry.Add(deviceId, socket);

    try
    {
        // Flush anything queued while this device was offline, oldest first.
        foreach (var queuedFrameJson in db.DequeueOutbox(deviceId))
        {
            if (socket.State != WebSocketState.Open)
                break;
            await RelayProtocol.SendRawAsync(socket, queuedFrameJson, ct);
        }

        while (socket.State == WebSocketState.Open)
        {
            var frame = await RelayProtocol.ReceiveFrameAsync(socket, ct);
            if (frame is null)
                break;

            if (frame is { Type: "send", RecipientDeviceId: { } recipientId, Envelope: not null })
            {
                var deliverFrame = new RelayFrame { Type = "deliver", SenderDeviceId = deviceId, Envelope = frame.Envelope };

                if (registry.TryGet(recipientId, out var recipientSocket))
                {
                    // Best-effort live delivery: if the send itself fails (e.g. the recipient
                    // dropped between TryGet and here), fall back to the outbox rather than
                    // losing the message — no ACK protocol in this pass, see RelayFrame's remarks.
                    try
                    {
                        await RelayProtocol.SendFrameAsync(recipientSocket, deliverFrame, ct);
                    }
                    catch (WebSocketException)
                    {
                        db.EnqueueOutbox(recipientId, RelayProtocol.Serialize(deliverFrame));
                    }
                }
                else
                {
                    db.EnqueueOutbox(recipientId, RelayProtocol.Serialize(deliverFrame));
                }
            }
        }
    }
    finally
    {
        registry.Remove(deviceId, socket);

        // Complete the close handshake gracefully — without this, disposing the raw socket
        // abruptly (whatever ended the loop above: client-initiated close, error, or otherwise)
        // makes ClientWebSocket.CloseAsync on the far end throw "remote party closed the
        // connection without completing the close handshake" instead of returning cleanly.
        try
        {
            if (socket.State == WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            else if (socket.State == WebSocketState.Open)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
        catch (WebSocketException)
        {
            // Best-effort — the peer may already be gone (network drop, abrupt process exit).
        }
    }
});

app.Run();

static bool IsAdminAuthorized(HttpRequest request, string adminSecret)
{
    var provided = request.Headers["X-Admin-Secret"].ToString();
    if (string.IsNullOrEmpty(provided))
        return false;

    var providedBytes = Encoding.UTF8.GetBytes(provided);
    var expectedBytes = Encoding.UTF8.GetBytes(adminSecret);
    return providedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}

/// <summary>Device-bearer auth for the HTTP (non-WebSocket) endpoints — the WS channel authenticates via its own first-frame handshake instead; this reuses the same underlying <see cref="RelayDatabase.TryAuthenticate"/> check.</summary>
static bool TryGetDeviceAuth(HttpRequest request, RelayDatabase db, out Guid deviceId)
{
    deviceId = Guid.Empty;
    var deviceIdHeader = request.Headers["X-Device-Id"].ToString();
    var secretHeader = request.Headers["X-Device-Secret"].ToString();
    if (string.IsNullOrEmpty(deviceIdHeader) || string.IsNullOrEmpty(secretHeader))
        return false;
    if (!Guid.TryParse(deviceIdHeader, out deviceId))
        return false;

    return db.TryAuthenticate(deviceId, secretHeader);
}

static object ToLibraryFileDto(LibraryFileRecord record) => new
{
    record.Id,
    record.FolderPath,
    record.FileName,
    record.Tags,
    record.SizeBytes,
    record.UploadedByDeviceId,
    record.UploadedAtUtc
};
