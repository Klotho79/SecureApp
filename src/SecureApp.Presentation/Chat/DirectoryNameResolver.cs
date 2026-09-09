using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Chat;

/// <summary>
/// Resolves a peer's CURRENT display name from the relay's own member directory (2026-09-09) —
/// <c>ChatSession.PeerDisplayName</c>/<c>GroupMember.DisplayName</c> are both captured once, at
/// pairing/invite time, and never revised afterward; renaming yourself in Settings only ever
/// affects a FUTURE pairing or group (re-)broadcast, never anything already established. Every
/// device in real testing paired while still literally "Local User", so nothing already-established
/// ever picked up a later rename — a real, repeatedly-reported complaint ("chci aby se misto lokal
/// user vypsalo kdo to poslal"). The directory itself is always current (every device republishes
/// its OWN name on every successful connect via <c>WebSocketMessageTransport.ConnectAsync</c>'s call
/// to <c>PublishSelfAsync</c>), so looking a peer's name up there at DISPLAY time, instead of
/// trusting whatever was cached at pairing time, is current by construction — no re-pairing dance,
/// no waiting for a fresh invite.
/// </summary>
public static class DirectoryNameResolver
{
    /// <summary>Fetches the whole directory once and keys it by public key (base64) for repeated lookups — call once per screen load, not per row.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> BuildAsync(IContactDirectoryService contactDirectoryService, CancellationToken ct = default)
    {
        try
        {
            var members = await contactDirectoryService.ListMembersAsync(ct);
            return members
                .GroupBy(m => Convert.ToBase64String(m.PublicKey))
                .ToDictionary(g => g.Key, g => g.First().DisplayName);
        }
        catch
        {
            // Best-effort — relay unreachable, not connected, whatever. Callers fall back to
            // whatever locally-cached name they already have; nothing worse than before this existed.
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Looks up one peer's current name in a directory built by <see cref="BuildAsync"/>, falling back to the caller's own locally-cached copy if the peer isn't in it (not yet published, relay unreachable when the directory was fetched, etc.).</summary>
    public static string Resolve(IReadOnlyDictionary<string, string> directory, byte[] publicKey, string fallback)
        => directory.TryGetValue(Convert.ToBase64String(publicKey), out var currentName) && !string.IsNullOrWhiteSpace(currentName)
            ? currentName
            : fallback;
}
