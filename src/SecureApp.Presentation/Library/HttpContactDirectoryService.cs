using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Library;

/// <inheritdoc cref="IContactDirectoryService"/>
/// <remarks>Endpoint, device auth and sending come from <see cref="RelayServiceBase"/>.</remarks>
public sealed class HttpContactDirectoryService : RelayServiceBase, IContactDirectoryService
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IMessagingService _messagingService;
    private readonly ISharedLibraryService _sharedLibraryService;

    public HttpContactDirectoryService(
        ICurrentUserService currentUserService,
        IMessagingService messagingService,
        ISecureVaultKeyStore vault,
        ITransportSettingsRepository transportSettingsRepository,
        ISharedLibraryService sharedLibraryService)
        : base(vault, transportSettingsRepository)
    {
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
    }

    public async Task PublishSelfAsync(CancellationToken ct = default)
    {
        // 2026-09-09 — a real, repeatedly-reported bug: without this call, `_currentUserService.Current`
        // can still be its in-memory constructor default ("Local User") rather than whatever was
        // actually persisted, if nothing else has initialized it yet. `App.RunConnectionSupervisorLoopAsync`
        // now connects (and so publishes) almost immediately on every launch, racing ahead of Settings'
        // own page-level InitializeAsync call — so EVERY app restart re-published the wrong default
        // name, overwriting whatever the user had actually set the moment before. InitializeAsync is
        // idempotent (a no-op after the first real call), so calling it defensively here is always safe.
        await _currentUserService.InitializeAsync(ct);

        var publicKey = await _messagingService.GetLocalIdentityPublicKeyAsync(ct);

        // AppVersion (2026-10-01) — "1.29 (32)" display+build, so the admin's member screen can spot
        // an outdated device without having to ask ("jakou verzi appky mas?") every time something
        // like the shared-library-key auto-sync behaves differently on an old build.
        var appVersion = $"{Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString} ({Microsoft.Maui.ApplicationModel.AppInfo.Current.BuildString})";

        // FormalName (2026-10-08) — Jméno+Příjmení, null until the device has both set (see User's
        // own remarks). Platform — DeviceInfo.Current.Platform's string form (Android/iOS/WinUI/
        // MacCatalyst), for the admin device list's per-platform coverage view.
        var formalName = _currentUserService.Current.FormalName;

        // HasSharedLibraryKey (2026-10-12, user's own ask after a real bogus-notification report —
        // see HttpCommunityBoardService's own remarks): self-reported the same way AppVersion/Platform
        // already are, so the admin's "Správa členů" screen can show which members are stuck without
        // the key instead of only finding out when something downstream (Nástěnka, library files)
        // breaks for them.
        var hasSharedLibraryKey = await _sharedLibraryService.HasSharedKeyAsync(ct);

        await SendOrThrowAsync(HttpMethod.Post, "directory/publish", new
        {
            DisplayName = _currentUserService.Current.DisplayName,
            PublicKeyBase64 = Convert.ToBase64String(publicKey),
            AppVersion = appVersion,
            FormalName = string.IsNullOrWhiteSpace(formalName) ? null : formalName,
            Platform = Microsoft.Maui.Devices.DeviceInfo.Current.Platform.ToString(),
            HasSharedLibraryKey = hasSharedLibraryKey,
        }, ct);
    }

    public async Task<IReadOnlyList<DirectoryMember>> ListMembersAsync(CancellationToken ct = default)
    {
        var dtos = await GetJsonAsync<List<DirectoryMemberDto>>("directory/members", ct) ?? [];
        return dtos.Select(d => new DirectoryMember(d.DeviceId, d.DisplayName, Convert.FromBase64String(d.PublicKeyBase64), d.FormalName)).ToList();
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetLastSeenAsync(CancellationToken ct = default)
    {
        // An older relay without the route answers 404 — treated as "no data".
        var dtos = await GetJsonAsync<List<DirectoryLastSeenDto>>("directory/last-seen", ct, notFoundAsDefault: true) ?? [];
        return dtos.GroupBy(d => d.PublicKeyBase64).ToDictionary(g => g.Key, g => g.Max(d => d.LastActiveAtUtc));
    }

    public Task LinkArimContactAsync(Guid arimContactId, CancellationToken ct = default)
        => SendOrThrowAsync(HttpMethod.Post, "directory/link-arim-contact", new { ArimContactId = arimContactId }, ct);

    private sealed record DirectoryLastSeenDto(string PublicKeyBase64, DateTimeOffset LastActiveAtUtc);

    private sealed record DirectoryMemberDto(Guid DeviceId, string DisplayName, string PublicKeyBase64, string? FormalName = null);
}
