using System.Net.Http.Json;
using System.Security.Cryptography;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Updates;

/// <inheritdoc cref="IUpdateService"/>
/// <remarks>
/// Plain HTTP against the relay's own per-platform <c>/download/{platform}/version</c>/
/// <c>/download/{platform}</c> routes (see <c>SecureApp.Relay.Program</c>'s own remarks —
/// deliberately the one unauthenticated surface on the relay besides /health, same "anyone already
/// on the network" exposure). Reuses whatever relay endpoint the device already has configured for
/// chat (<see cref="ITransportSettingsRepository"/>), converting its <c>ws://</c>/<c>wss://</c>
/// scheme to <c>http://</c>/<c>https://</c> — falls back to <see cref="RelayDefaults.DefaultEndpoint"/>
/// when nothing's configured yet, same fallback <c>SettingsViewModel</c>'s own endpoint field already
/// pre-fills.
///
/// 2026-10-12, real bug fixed: this used to hardcode "download/android" everywhere, so a Windows
/// device's own update check actually compared itself against the ANDROID release and, even when it
/// found a "newer" version, had no matching installer to hand off to (see INativeAppInstaller's
/// Windows implementation for the other half of that fix) — effectively "no update possible on PC"
/// from the user's own report. <see cref="PlatformName"/> below is the one thing that changed; every
/// relay route and local filename now derives from it instead of a literal "android".
/// </remarks>
public sealed class UpdateService : IUpdateService
{
    private readonly ITransportSettingsRepository _transportSettingsRepository;

    public UpdateService(ITransportSettingsRepository transportSettingsRepository)
    {
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
    }

    /// <summary>The relay's own per-platform route segment — "windows" or "android" (every other MAUI target this app ships to update is Android-based, so it's the fallback rather than iOS/MacCatalyst, which this app doesn't ship update binaries for at all).</summary>
    private static string PlatformName => OperatingSystem.IsWindows() ? "windows" : "android";

    private static string LocalFileName => OperatingSystem.IsWindows() ? "secureapp-update.zip" : "secureapp-update.apk";

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = await ResolveHttpBaseAsync(ct) };
            var manifest = await http.GetFromJsonAsync<UpdateManifestDto>($"download/{PlatformName}/version", ct);
            if (manifest is null)
                return new UpdateCheckResult(false, null, null, "Na relay serveru zatím není nahraná žádná verze appky.");

            var currentVersionCode = int.TryParse(Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString, out var v) ? v : 0;
            var isNewer = manifest.VersionCode > currentVersionCode;
            return new UpdateCheckResult(isNewer, manifest.VersionCode, manifest.VersionName, null);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, null, null, $"Kontrola aktualizace se nezdařila: {ex.Message}");
        }
    }

    public async Task<string> DownloadUpdateAsync(IProgress<double>? onProgress = null, CancellationToken ct = default)
    {
        // Windows delta path (2026-10-14, user's own ask: don't re-download the whole ~170MB portable
        // build for a small change) — Android has no equivalent (see this type's own remarks on why a
        // sideloaded APK can't do this without a custom bsdiff-style binary patcher); Windows can,
        // because the portable build is just a folder of files the relay can diff by path+hash. Falls
        // straight through to the unconditional full download below on ANY failure (old relay with no
        // manifest yet, network hiccup, local files that don't match what the manifest expects) — the
        // full path is always correct, just occasionally not the smallest one.
        if (OperatingSystem.IsWindows())
        {
            var delta = await TryDownloadWindowsDeltaAsync(onProgress, ct);
            if (delta is not null)
                return delta;
        }

        using var http = new HttpClient { BaseAddress = await ResolveHttpBaseAsync(ct) };
        using var response = await http.GetAsync($"download/{PlatformName}", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var destinationPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, LocalFileName);

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var destination = File.Create(destinationPath);

        var buffer = new byte[81920];
        long readSoFar = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            readSoFar += bytesRead;
            if (totalBytes is > 0)
                onProgress?.Report((double)readSoFar / totalBytes.Value);
        }

        return destinationPath;
    }

    /// <summary>
    /// Fetches the relay's path→sha256 manifest for the hosted Windows build (see
    /// <c>SecureApp.Relay.Program</c>'s own <c>/download/windows/manifest</c>/<c>/download/windows/delta</c>
    /// remarks), hashes this install's own files against it, and downloads only what actually
    /// differs. Returns null on anything that doesn't cleanly work out — the caller falls back to a
    /// full download rather than ever treating this as a hard error.
    /// </summary>
    private async Task<string?> TryDownloadWindowsDeltaAsync(IProgress<double>? onProgress, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = await ResolveHttpBaseAsync(ct) };
            var manifest = await http.GetFromJsonAsync<Dictionary<string, string>>("download/windows/manifest", ct);
            if (manifest is null || manifest.Count == 0)
                return null;

            var baseDir = AppContext.BaseDirectory;
            var needed = new List<string>();
            foreach (var (relativePath, expectedHash) in manifest)
            {
                var localPath = Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(localPath) || !string.Equals(ComputeFileSha256(localPath), expectedHash, StringComparison.OrdinalIgnoreCase))
                    needed.Add(relativePath);
            }

            // Every file the manifest lists already matches what's on disk — nothing to transfer at
            // all. In practice this means the version bump was metadata-only (shouldn't normally
            // happen, since the main exe/dll's own version is baked into their bytes), but there's
            // nothing unsafe about it: just hand back "no update needed" rather than fabricate a path.
            if (needed.Count == 0)
                return null;

            using var response = await http.PostAsJsonAsync("download/windows/delta", new { paths = needed }, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var totalBytes = response.Content.Headers.ContentLength;
            var destinationPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, "secureapp-update-delta.zip");
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = File.Create(destinationPath);

            var buffer = new byte[81920];
            long readSoFar = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                readSoFar += bytesRead;
                if (totalBytes is > 0)
                    onProgress?.Report((double)readSoFar / totalBytes.Value);
            }

            return destinationPath;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public async Task<string> GetDownloadUrlAsync(CancellationToken ct = default)
    {
        var baseUri = await ResolveHttpBaseAsync(ct);
        return new Uri(baseUri, $"download/{PlatformName}").ToString();
    }

    private async Task<Uri> ResolveHttpBaseAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri ?? new Uri(RelayDefaults.DefaultEndpoint);
        var scheme = wsEndpoint.Scheme == "wss" ? "https" : "http";
        var builder = new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port, Path = "/" };
        return builder.Uri;
    }

    private sealed record UpdateManifestDto(int VersionCode, string VersionName, DateTimeOffset ReleasedAtUtc);
}
