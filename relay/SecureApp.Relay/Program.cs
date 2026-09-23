using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using SecureApp.Relay;

var builder = WebApplication.CreateBuilder(args);

var adminSecret = builder.Configuration["SECUREAPP_RELAY_ADMIN_SECRET"]
    ?? throw new InvalidOperationException("SECUREAPP_RELAY_ADMIN_SECRET must be set (environment variable or configuration) — the relay refuses to start without it, rather than silently allow unauthenticated admin access.");

// New-member WireGuard onboarding (2026-09-23, user's own ask: a brand-new member has no network
// access at all yet, so SecureApp's own admin secret can gate the CALL to this endpoint, but the
// actual wg-easy credential stays Pi-internal — never sent to/stored on any phone. Both optional:
// this endpoint 404s if either is missing, rather than the whole relay refusing to start (unlike
// adminSecret above) — WireGuard onboarding is a nice-to-have on top of the relay's real job, not
// something every deployment of this relay necessarily has wg-easy for.
var wgEasyUrl = builder.Configuration["SECUREAPP_WGEASY_URL"];
var wgEasyPassword = builder.Configuration["SECUREAPP_WGEASY_PASSWORD"];

var port = builder.Configuration["SECUREAPP_RELAY_PORT"] ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Kestrel's own default request-body cap (~28.6 MB) would silently reject a larger shared-library
// upload (a multi-page scanned PDF can easily exceed that) with no code on either side actually
// being wrong — this only ever showed up as a mysterious failure on a big file. Raised, not
// removed: an explicit cap still protects the Pi's own (limited, non-redundant) disk from an
// unbounded upload, it's just sized for a realistic clinical document rather than Kestrel's
// generic default. 200 MB comfortably covers even a large scanned/multi-page PDF.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 200 * 1024 * 1024);

// Same directory RelayDatabase resolves SECUREAPP_RELAY_DB_PATH into (the mounted /data volume in
// production) — reused here rather than introducing a second env var, since the marker just needs
// to land somewhere that survives a container restart and is visible to a host-side watcher.
// Public download page (2026-09-10) — deliberately the ONE unauthenticated, no-device-credential
// surface on this relay besides /health: a brand-new person has neither a device secret nor an
// admin secret yet, so anything gating this would be a chicken-and-egg problem. Bounded exposure
// the same way /health already is — this relay is never reachable outside the LAN/VPN in the first
// place (see docker-compose.yml's own remarks), so "public" here means "anyone already on the
// network", not the actual public internet. Files are read live from disk on every request (not
// baked into the container image), so publishing a new build is just overwriting the file on the
// Pi — no relay rebuild/redeploy needed for that alone.
var downloadsDir = builder.Configuration["SECUREAPP_RELAY_DOWNLOADS_DIR"]
    ?? Path.Combine(Path.GetDirectoryName(builder.Configuration["SECUREAPP_RELAY_DB_PATH"]) is { Length: > 0 } downloadsBaseDir ? downloadsBaseDir : AppContext.BaseDirectory, "downloads");
Directory.CreateDirectory(downloadsDir);

var deployMarkerPath = Path.Combine(
    Path.GetDirectoryName(builder.Configuration["SECUREAPP_RELAY_DB_PATH"]) is { Length: > 0 } dbDir ? dbDir : AppContext.BaseDirectory,
    "deploy-requested");

builder.Services.AddSingleton<RelayDatabase>();
builder.Services.AddSingleton<ConnectionRegistry>();

var app = builder.Build();

app.Services.GetRequiredService<RelayDatabase>().Initialize();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok());

app.MapGet("/download", () =>
{
    var androidPath = Path.Combine(downloadsDir, "secureapp-android.apk");
    var androidAvailable = File.Exists(androidPath);
    var androidSize = androidAvailable ? $"{new FileInfo(androidPath).Length / 1024.0 / 1024.0:F0} MB" : null;

    return Results.Content(DownloadPageHtml(androidAvailable, androidSize), "text/html; charset=utf-8");
});

app.MapGet("/download/android", async () =>
{
    var path = Path.Combine(downloadsDir, "secureapp-android.apk");
    if (!File.Exists(path))
        return Results.NotFound("Android verze zatím není nahraná.");

    var bytes = await File.ReadAllBytesAsync(path);
    return Results.File(bytes, "application/vnd.android.package-archive", "SecureApp.apk");
});

// Self-update (2026-09-23) — the in-app update check reads this; unauthenticated, same reasoning
// as /download itself (a device that's already this far along already has the app, but checking
// "is there something newer" shouldn't need a device credential either — it's the same "anyone
// already on the network" exposure /health and /download already accept).
app.MapGet("/download/android/version", () =>
{
    var versionPath = Path.Combine(downloadsDir, "secureapp-android.version.json");
    if (!File.Exists(versionPath))
        return Results.NotFound();

    return Results.Content(File.ReadAllText(versionPath), "application/json");
});

// Lets a future release be published with one authenticated POST from the dev machine instead of
// manually scp-ing the APK onto the Pi — same admin-secret gate as /admin/deploy right above.
app.MapPost("/admin/upload/android", async (HttpRequest request) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files["apk"];
    if (file is null || file.Length == 0)
        return Results.BadRequest("Missing 'apk' file.");
    if (!int.TryParse(form["versionCode"].ToString(), out var versionCode))
        return Results.BadRequest("Missing/invalid 'versionCode'.");

    var versionName = form["versionName"].ToString();
    if (string.IsNullOrEmpty(versionName))
        versionName = versionCode.ToString();

    var apkPath = Path.Combine(downloadsDir, "secureapp-android.apk");
    await using (var stream = File.Create(apkPath))
        await file.CopyToAsync(stream);

    var versionPath = Path.Combine(downloadsDir, "secureapp-android.version.json");
    var manifest = new UpdateManifest(versionCode, versionName, DateTimeOffset.UtcNow);
    await File.WriteAllTextAsync(versionPath, System.Text.Json.JsonSerializer.Serialize(manifest));

    return Results.Ok(new { uploaded = true, sizeBytes = file.Length, versionCode, versionName });
});

app.MapPost("/admin/invites", (HttpRequest request, CreateInviteRequest body, RelayDatabase db) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    var code = db.CreateInvite(body.DisplayNameHint, TimeSpan.FromMinutes(body.ValidForMinutes), out var expiresAtUtc);
    return Results.Ok(new CreateInviteResponse(code, expiresAtUtc));
});

// New-member onboarding (2026-09-23) — creates a WireGuard peer via wg-easy's own API (session-less:
// wg-easy's own middleware also accepts the password directly as a plain Authorization header, see
// its Server.js, so no cookie/session juggling needed here) and returns the raw .conf text so the app
// can render its own QR from it (ZXing, already used for the SecureApp-download QR) — never proxying
// through wg-easy's own SVG QR endpoint, one fewer format to round-trip.
app.MapPost("/admin/wireguard/clients", async (HttpRequest request, CreateWireGuardClientRequest body) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();
    if (string.IsNullOrEmpty(wgEasyUrl) || string.IsNullOrEmpty(wgEasyPassword))
        return Results.NotFound("WireGuard onboarding not configured on this relay (SECUREAPP_WGEASY_URL/SECUREAPP_WGEASY_PASSWORD).");
    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("Missing 'name'.");

    using var wg = new HttpClient { BaseAddress = new Uri(wgEasyUrl) };
    wg.DefaultRequestHeaders.Add("Authorization", wgEasyPassword);

    var createResponse = await wg.PostAsJsonAsync("api/wireguard/client", new { name = body.Name });
    if (!createResponse.IsSuccessStatusCode)
        return Results.Problem($"wg-easy rejected client creation: {createResponse.StatusCode}", statusCode: 502);

    // camelCase JSON from wg-easy (its own JS field names, e.g. "createdAt") needs Web defaults —
    // same real bug this relay's own client code already hit once before (see MessagingService/
    // WebSocketMessageTransport's own remarks), not repeating it here.
    var clients = await wg.GetFromJsonAsync<List<WgEasyClient>>("api/wireguard/client", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    var newest = clients?.OrderByDescending(c => c.CreatedAt).FirstOrDefault();
    if (newest is null)
        return Results.Problem("wg-easy created the client but it couldn't be found afterward.", statusCode: 502);

    var configText = await wg.GetStringAsync($"api/wireguard/client/{newest.Id}/configuration");
    return Results.Ok(new WireGuardClientResponse(configText));
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

// No admin approval required — WireGuard is the trust boundary: only devices on the VPN/LAN
// can reach this endpoint at all, so "you're on the network" is the credential check.
// Used by the app's silent self-re-registration path when a device's stored credentials are
// rejected (registration was deleted). The device recovers on the next supervisor tick with no
// user-visible error or confirmation step.
app.MapPost("/self-register", (CreateDeviceRequest body, RelayDatabase db, ILogger<Program> logger) =>
{
    var displayName = string.IsNullOrWhiteSpace(body.DisplayName) ? "Unknown" : body.DisplayName;
    var (deviceId, secret) = db.CreateDevice(displayName);
    logger.LogInformation("Self-registered device {DeviceId} ({DisplayName})", deviceId, displayName);
    return Results.Ok(new DeviceCredentialResponse(deviceId, secret));
});

// --- Activation requests (2026-09-06) — see Contracts.cs's own remarks for why this exists
// alongside (not instead of, at the relay's own storage level) the invite-code endpoints above.

app.MapPost("/activation/request", (CreateActivationRequestRequest body, RelayDatabase db) =>
{
    if (string.IsNullOrWhiteSpace(body.DisplayName) || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.KeyFingerprint))
        return Results.BadRequest("DisplayName, Email, and KeyFingerprint are all required.");

    var requestId = db.CreateActivationRequest(body.DisplayName, body.Email, body.KeyFingerprint);

    // 2026-09-19 (user's own explicit call, after being shown the tradeoff: an open relay endpoint
    // is baked into the shared APK, so anyone who ever gets the install file could self-register)
    // — auto-approve every activation request immediately instead of waiting on an admin's manual
    // decision. The only remaining gate is who receives the install link at all. Reuses the exact
    // same device-creation path a manual /admin/activation-requests/{id}/approve already used, so
    // the client's existing PollActivationAsync loop needs no changes — it just sees "Approved" on
    // its very next poll instead of staying "Pending" indefinitely.
    db.ApproveActivationRequest(requestId);

    return Results.Ok(new CreateActivationRequestResponse(requestId));
});

// Deliberately unauthenticated (like /register) — the request id itself is an unguessable GUID the
// requesting device alone holds, same bearer-token-by-possession reasoning an invite code already
// relied on. Idempotent: polling again after Approved keeps returning the same credential rather
// than a one-shot reveal, so a device that misses the response once (killed mid-poll, network
// blip) doesn't get stuck needing the admin to approve a second time.
app.MapGet("/activation/status/{id:guid}", (Guid id, RelayDatabase db) =>
{
    var record = db.GetActivationRequest(id);
    if (record is null)
        return Results.NotFound();

    if (record.Status != "Approved" || record.AssignedDeviceId is not { } deviceId)
        return Results.Ok(new ActivationStatusResponse(record.Status, null, null));

    // Approved: the device row already exists (ApproveActivationRequest created it), but its
    // bearer secret was only ever returned once from that call, at approval time — RelayDatabase
    // doesn't store secrets in recoverable form (see its own remarks: salted + HMAC-hashed only).
    // So the secret returned here is cached at approval time onto the activation request row's own
    // (unauthenticated-but-unguessable) storage, not re-derived. See ApproveActivationRequest's
    // caller below for where that value actually comes from.
    var cachedSecret = db.GetActivationRequestSecret(id);
    return Results.Ok(new ActivationStatusResponse(record.Status, deviceId, cachedSecret));
});

app.MapGet("/admin/activation-requests", (HttpRequest request, RelayDatabase db) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    var pending = db.GetPendingActivationRequests();
    return Results.Ok(pending.Select(r => new ActivationRequestSummary(r.Id, r.DisplayName, r.Email, r.KeyFingerprint, r.CreatedAtUtc)).ToList());
});

app.MapPost("/admin/activation-requests/{id:guid}/approve", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    var approved = db.ApproveActivationRequest(id);
    if (approved is null)
        return Results.Conflict("This request no longer exists or was already decided.");

    return Results.Ok();
});

app.MapPost("/admin/activation-requests/{id:guid}/reject", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    if (!db.RejectActivationRequest(id))
        return Results.Conflict("This request no longer exists or was already decided.");

    return Results.Ok();
});

// --- Device management (2.1, 2026-09-17) — see RelayDatabase.GetAllDevicesWithStatus/DeregisterDevice's own remarks.

app.MapGet("/admin/devices", (HttpRequest request, RelayDatabase db) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    var devices = db.GetAllDevicesWithStatus();
    return Results.Ok(devices.Select(d => new RegisteredDeviceSummary(
        d.Id, d.DisplayName, d.CreatedAtUtc, d.DirectoryDisplayName, d.LastActiveAtUtc, d.PendingOutboxCount)).ToList());
});

app.MapPost("/admin/devices/{id:guid}/deregister", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    if (!IsAdminAuthorized(request, adminSecret))
        return Results.Unauthorized();

    db.DeregisterDevice(id);
    return Results.Ok();
});

app.MapPost("/library/files", async (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out var deviceId))
        return Results.Unauthorized();

    var folderPath = request.Query["folderPath"].ToString();
    var fileName = request.Query["fileName"].ToString();
    var tagsRaw = request.Query["tags"].ToString();
    var expectedHash = request.Query["contentHash"].ToString();
    // listed=false marks a PRIVATE chat attachment: same encrypted storage, but hidden from the
    // library browser and reachable only by id from the E2EE message (2026-09-14). Default listed.
    var listed = !string.Equals(request.Query["listed"].ToString(), "false", StringComparison.OrdinalIgnoreCase);

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
    var record = db.InsertLibraryFileMetadata(folderPath, fileName, tags, contentBytes.LongLength, actualHash, deviceId, listed);

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

// Promote a private chat attachment to the community library (2026-09-14, 2.3 "move from local archive
// to global") — flips is_listed to 1 so it appears in the browser. Uploader or admin only, mirroring
// the delete endpoint's cooperative-role trust.
app.MapPost("/library/files/{id:guid}/publish", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    var isAdmin = IsAdminAuthorized(request, adminSecret);
    var isDeviceAuthed = TryGetDeviceAuth(request, db, out var callerDeviceId);
    if (!isAdmin && !isDeviceAuthed)
        return Results.Unauthorized();

    var folderPath = request.Query["folderPath"].ToString();
    if (!db.TryPublishLibraryFile(id, callerDeviceId, isAdmin, folderPath))
        return Results.NotFound();

    return Results.NoContent();
});

// --- Member directory (2026-09-06) — see Contracts.cs's own remarks. Device-authenticated
// (X-Device-Id/X-Device-Secret), same as /library/files — not admin-gated, since any already
// admin-approved device is exactly who this is meant to be visible to.

app.MapPost("/directory/publish", (HttpRequest request, PublishDirectoryEntryRequest body, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out var deviceId))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.DisplayName) || string.IsNullOrWhiteSpace(body.PublicKeyBase64))
        return Results.BadRequest("DisplayName and PublicKeyBase64 are both required.");

    byte[] publicKey;
    try
    {
        publicKey = Convert.FromBase64String(body.PublicKeyBase64);
    }
    catch (FormatException)
    {
        return Results.BadRequest("PublicKeyBase64 is not valid base64.");
    }

    db.UpsertDirectoryEntry(deviceId, body.DisplayName, publicKey);
    return Results.Ok();
});

app.MapGet("/directory/members", (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out var deviceId))
        return Results.Unauthorized();

    var members = db.GetDirectoryMembers(deviceId);
    return Results.Ok(members.Select(m => new DirectoryMemberSummary(m.DeviceId, m.DisplayName, Convert.ToBase64String(m.PublicKey))).ToList());
});

// --- Shared-library-key escrow (2026-09-11) — see the wrapped_library_keys table's own remarks.
// Device-authenticated, not admin-gated: the same "any already-approved device" trust as the
// directory and library. The relay only stores/serves opaque ML-KEM ciphertext it cannot read.

app.MapPost("/library/wrapped-keys", (HttpRequest request, PublishWrappedKeyRequest body, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    if (body.RecipientDeviceId == Guid.Empty || string.IsNullOrWhiteSpace(body.WrappedBlob))
        return Results.BadRequest("RecipientDeviceId and WrappedBlob are both required.");

    db.UpsertWrappedLibraryKey(body.RecipientDeviceId, body.WrappedBlob);
    return Results.Ok();
});

app.MapGet("/library/wrapped-key", (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out var deviceId))
        return Results.Unauthorized();

    var blob = db.GetWrappedLibraryKey(deviceId);
    return blob is null ? Results.NotFound() : Results.Ok(new WrappedKeyResponse(blob));
});

// --- Shared diagnostics log (2026-09-10) — see Contracts.cs's own remarks.

app.MapPost("/diagnostics/logs", (HttpRequest request, ReportDiagnosticLogRequest body, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out var deviceId))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.Level) || string.IsNullOrWhiteSpace(body.Message))
        return Results.BadRequest("Level and Message are both required.");

    db.InsertDiagnosticLog(deviceId, body.Level, body.Message, body.Context, body.ExceptionDetails);
    return Results.Ok();
});

app.MapGet("/diagnostics/logs", (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    var limitRaw = request.Query["limit"].ToString();
    var limit = int.TryParse(limitRaw, out var parsed) ? Math.Clamp(parsed, 1, 500) : 100;

    var entries = db.GetRecentDiagnosticLogs(limit);
    return Results.Ok(entries.Select(e => new DiagnosticLogEntryDto(e.Id, e.DeviceDisplayName, e.Level, e.Message, e.Context, e.ExceptionDetails, e.CreatedAtUtc)).ToList());
});

// --- Logbook catalog sync (2026-09-10) — see Contracts.cs's own remarks.

app.MapPost("/logbook/checklists", (HttpRequest request, LogbookChecklistDto body, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("Name is required.");

    db.UpsertLogbookChecklist(body.Id, body.Name, System.Text.Json.JsonSerializer.Serialize(body.Items), body.CreatedAtUtc);
    return Results.Ok();
});

app.MapGet("/logbook/checklists", (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    var entries = db.GetLogbookChecklists();
    return Results.Ok(entries.Select(e => new LogbookChecklistDto(e.Id, e.Name, System.Text.Json.JsonSerializer.Deserialize<List<string>>(e.ItemsJson) ?? [], e.CreatedAtUtc)).ToList());
});

app.MapPost("/logbook/procedure-types", (HttpRequest request, LogbookProcedureTypeDto body, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Abbreviation) || string.IsNullOrWhiteSpace(body.Category))
        return Results.BadRequest("Name, Abbreviation, and Category are all required.");

    db.UpsertLogbookProcedureType(body.Id, body.Name, body.Abbreviation, body.Category, body.CreatedAtUtc);
    return Results.Ok();
});

app.MapGet("/logbook/procedure-types", (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    var entries = db.GetLogbookProcedureTypes();
    return Results.Ok(entries.Select(e => new LogbookProcedureTypeDto(e.Id, e.Name, e.Abbreviation, e.Category, e.CreatedAtUtc)).ToList());
});

// Deletion (2026-09-10) — device-authenticated same as everything else here, not restricted to
// the uploader/creator: the app's own RoleAccessPolicy (Modifier/Admin for either catalog) is what
// actually gates who gets to press the button, same trust model this whole sync feature already
// has (the relay trusts any already-activated device, RBAC is enforced client-side throughout).
app.MapDelete("/logbook/checklists/{id:guid}", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    db.DeleteLogbookChecklist(id);
    return Results.NoContent();
});

app.MapDelete("/logbook/procedure-types/{id:guid}", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    db.DeleteLogbookProcedureType(id);
    return Results.NoContent();
});

// --- Shared company phone/extension directory (2026-09-20) — see Contracts.cs's own remarks.

app.MapPost("/contacts", (HttpRequest request, SharedContactDto body, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.DisplayName))
        return Results.BadRequest("DisplayName is required.");

    db.UpsertSharedContact(body.Id, body.DisplayName, body.Phone, body.Note, body.SortOrder, body.CreatedAtUtc);
    return Results.Ok();
});

app.MapGet("/contacts", (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    var entries = db.GetSharedContacts();
    return Results.Ok(entries.Select(e => new SharedContactDto(e.Id, e.DisplayName, e.Phone, e.Note, e.SortOrder, e.CreatedAtUtc)).ToList());
});

app.MapDelete("/contacts/{id:guid}", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    db.DeleteSharedContact(id);
    return Results.NoContent();
});

// --- Shared company workplace catalog (2026-09-20, NOTIFICATION_HUB_SPEC.md Phase 5) — mirrors
// the /contacts endpoints right above exactly; see Contracts.cs's own remarks.

app.MapPost("/workplaces", (HttpRequest request, WorkplaceDto body, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("Name is required.");

    db.UpsertWorkplace(body.Id, body.Name, body.Description, body.CreatedAtUtc);
    return Results.Ok();
});

app.MapGet("/workplaces", (HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    var entries = db.GetWorkplaces();
    return Results.Ok(entries.Select(e => new WorkplaceDto(e.Id, e.Name, e.Description, e.CreatedAtUtc)).ToList());
});

app.MapDelete("/workplaces/{id:guid}", (Guid id, HttpRequest request, RelayDatabase db) =>
{
    if (!TryGetDeviceAuth(request, db, out _))
        return Results.Unauthorized();

    db.DeleteWorkplace(id);
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
            else if (frame is { Type: "pairing", RecipientDeviceId: { } pairingRecipientId, PairingInviteBlob: not null })
            {
                // Same live-or-outbox routing as a "send" frame above — deliberately duplicated
                // rather than generalized, since the two carry different payload shapes (Envelope
                // vs. an opaque pairing blob) and stay independently readable this way.
                var pairingDeliverFrame = new RelayFrame { Type = "pairing-deliver", SenderDeviceId = deviceId, PairingInviteBlob = frame.PairingInviteBlob };

                if (registry.TryGet(pairingRecipientId, out var pairingRecipientSocket))
                {
                    try
                    {
                        await RelayProtocol.SendFrameAsync(pairingRecipientSocket, pairingDeliverFrame, ct);
                    }
                    catch (WebSocketException)
                    {
                        db.EnqueueOutbox(pairingRecipientId, RelayProtocol.Serialize(pairingDeliverFrame));
                    }
                }
                else
                {
                    db.EnqueueOutbox(pairingRecipientId, RelayProtocol.Serialize(pairingDeliverFrame));
                }
            }
            else if (frame is { Type: "group-invite", RecipientDeviceId: { } groupInviteRecipientId, GroupInviteBlob: not null })
            {
                // Same live-or-outbox routing again — see the "pairing" branch above.
                var groupInviteDeliverFrame = new RelayFrame { Type = "group-invite-deliver", SenderDeviceId = deviceId, GroupInviteBlob = frame.GroupInviteBlob };

                if (registry.TryGet(groupInviteRecipientId, out var groupInviteRecipientSocket))
                {
                    try
                    {
                        await RelayProtocol.SendFrameAsync(groupInviteRecipientSocket, groupInviteDeliverFrame, ct);
                    }
                    catch (WebSocketException)
                    {
                        db.EnqueueOutbox(groupInviteRecipientId, RelayProtocol.Serialize(groupInviteDeliverFrame));
                    }
                }
                else
                {
                    db.EnqueueOutbox(groupInviteRecipientId, RelayProtocol.Serialize(groupInviteDeliverFrame));
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

/// <summary>Plain, dependency-free HTML — no static-file middleware/Razor set up in this minimal-API project, and this is one small page, so an inline string is the simplest honest option.</summary>
static string DownloadPageHtml(bool androidAvailable, string? androidSize)
{
    var androidSection = androidAvailable
        ? $"""<a class="btn" href="/download/android">Stáhnout pro Android ({androidSize})</a>"""
        : """<p class="muted">Android verze zatím není nahraná — zkuste to prosím později.</p>""";

    return $$"""
        <!doctype html>
        <html lang="cs">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>SecureApp — stažení</title>
        <style>
            body { font-family: system-ui, sans-serif; max-width: 560px; margin: 40px auto; padding: 0 20px; line-height: 1.5; color: #1a1a1a; }
            h1 { font-size: 22px; }
            .btn { display: inline-block; background: #14688A; color: #fff; text-decoration: none; padding: 12px 20px; border-radius: 8px; font-weight: 600; margin: 12px 0; }
            .muted { color: #666; font-size: 14px; }
            ol { padding-left: 20px; }
            li { margin-bottom: 8px; }
        </style>
        </head>
        <body>
        <h1>SecureApp</h1>
        <p>Aplikace pro dokumenty, chat a Logbook oddělení ARIM.</p>
        {{androidSection}}
        <h2>Jak nainstalovat (Android)</h2>
        <ol>
            <li>Stáhněte soubor tlačítkem výše.</li>
            <li>Otevřete stažený soubor — telefon se zeptá na povolení instalace z tohoto zdroje (prohlížeč/Soubory), povolte to.</li>
            <li>Po nainstalování otevřete appku → Nastavení → Relay.</li>
            <li>Zadejte jméno a e-mail a stiskněte <strong>Aktivovat</strong> — registrace proběhne rovnou, bez čekání.</li>
        </ol>
        <p class="muted">Tato stránka je dostupná jen v domácí síti / přes VPN, ne z veřejného internetu.</p>
        </body>
        </html>
        """;
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
