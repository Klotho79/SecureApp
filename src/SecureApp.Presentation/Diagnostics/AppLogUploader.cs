using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Diagnostics;

/// <summary>
/// Ships this device's own AppLog (errors.log / metrics.log) to the relay (2026-09-26, user's ask:
/// the admin must be able to read every device's log without the member sending anything). Called
/// from the connection supervisor's sweep; remembers per log how many bytes were already sent and
/// only uploads new complete lines, following AppLog's rotation into the ".1" file.
/// </summary>
public static class AppLogUploader
{
    private const int FirstRunTailBytes = 64 * 1024;
    private const int MaxLinesPerRequest = 1500;
    private const int MaxRequestsPerRun = 5;
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task UploadAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (!await Gate.WaitAsync(0, ct)) return;
        try
        {
            using var scope = services.CreateScope();
            var configuration = await scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>().GetAsync(ct);
            if (configuration is not { AssignedDeviceId: { } deviceId, EndpointUri: { } wsEndpoint }) return;
            var secret = await scope.ServiceProvider.GetRequiredService<ISecureVaultKeyStore>().RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct);
            if (secret is null) return;

            var scheme = wsEndpoint.Scheme switch { "ws" => "http", "wss" => "https", _ => wsEndpoint.Scheme };
            var uri = new Uri(new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri, "diagnostics/applog");

            foreach (var kind in new[] { "errors", "metrics" })
                await UploadKindAsync(kind, uri, deviceId, Encoding.UTF8.GetString(secret), ct);
        }
        catch (Exception ex)
        {
            // Not AppLog.Error: a failing upload would write a line, which would then need uploading…
            System.Diagnostics.Debug.WriteLine("APPLOG-UPLOAD failed: " + ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task UploadKindAsync(string kind, Uri uri, Guid deviceId, string secret, CancellationToken ct)
    {
        var prefs = Microsoft.Maui.Storage.Preferences.Default;
        var offsetKey = $"applog_uploaded_bytes_{kind}";
        var path = AppLog.PathOf(kind);
        var length = File.Exists(path) ? new FileInfo(path).Length : 0;

        long offset;
        var pending = new StringBuilder();
        var startsMidLine = false;
        if (!prefs.ContainsKey(offsetKey))
        {
            offset = Math.Max(0, length - FirstRunTailBytes);
            startsMidLine = offset > 0;
        }
        else
        {
            offset = prefs.Get(offsetKey, 0L);
            if (length < offset)
            {
                // Rotated since the last upload: finish the old file (now ".1") first, then the new one from the start.
                var rotated = path + ".1";
                if (File.Exists(rotated) && new FileInfo(rotated).Length > offset)
                    pending.Append(ReadCompleteLines(rotated, offset, out _));
                offset = 0;
            }
        }

        pending.Append(ReadCompleteLines(path, offset, out var consumed));
        var lines = pending.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        if (startsMidLine && lines.Count > 0)
            lines.RemoveAt(0); // first run started mid-file — the first "line" is a fragment
        if (lines.Count > MaxLinesPerRequest * MaxRequestsPerRun)
            lines = lines.TakeLast(MaxLinesPerRequest * MaxRequestsPerRun).ToList(); // a huge backlog: the newest part matters

        for (var i = 0; i < lines.Count; i += MaxLinesPerRequest)
        {
            var batch = lines.Skip(i).Take(MaxLinesPerRequest).ToList();
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(new { Kind = kind, Lines = batch }, options: HttpJsonOptions)
            };
            request.Headers.Add("X-Device-Id", deviceId.ToString());
            request.Headers.Add("X-Device-Secret", secret);
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return; // keep the offset — retried next sweep
        }

        prefs.Set(offsetKey, offset + consumed);
    }

    /// <summary>Everything from <paramref name="offset"/> up to and including the last newline; <paramref name="consumed"/> = bytes read.</summary>
    private static string ReadCompleteLines(string path, long offset, out long consumed)
    {
        consumed = 0;
        if (!File.Exists(path)) return string.Empty;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (offset >= stream.Length) return string.Empty;
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[stream.Length - offset];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
        if (lastNewline < 0) return string.Empty;
        consumed = lastNewline + 1;
        return Encoding.UTF8.GetString(buffer, 0, (int)consumed);
    }
}
