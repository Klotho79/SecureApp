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
    /// <summary>
    /// Last successfully-fetched directory, kept process-wide (2026-09-11) so a screen can render
    /// with good names IMMEDIATELY on open — before its own background <see cref="BuildAsync"/> round
    /// trip returns — and only rebuild if the fresh copy actually differs. Before this, a group chat
    /// re-decrypted and rebuilt its whole message list a moment after opening (once with local
    /// fallback names, then again after the directory fetch), a visible re-render flicker on every
    /// open. Empty until the first successful fetch this session.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LastKnown { get; private set; } = new Dictionary<string, string>();

    /// <summary>
    /// Jméno Příjmení per peer (2026-10-09), same public-key keying as <see cref="LastKnown"/> and
    /// refreshed by the same <see cref="BuildAsync"/> call — only peers that actually published a
    /// <c>DirectoryMember.FormalName</c> are present. Kept as a second dictionary rather than changing
    /// <see cref="LastKnown"/>'s value type, so every existing name-only caller stays untouched; the
    /// chat list's press-and-hold reveal is the only reader.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LastKnownFormalNames { get; private set; } = new Dictionary<string, string>();

    /// <summary>Fetches the whole directory once and keys it by public key (base64) for repeated lookups — call once per screen load, not per row. Updates <see cref="LastKnown"/> and <see cref="LastKnownFormalNames"/> on success.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> BuildAsync(IContactDirectoryService contactDirectoryService, CancellationToken ct = default)
    {
        try
        {
            var members = await contactDirectoryService.ListMembersAsync(ct);
            var byKey = members.GroupBy(m => Convert.ToBase64String(m.PublicKey)).ToList();
            var result = byKey.ToDictionary(g => g.Key, g => g.First().DisplayName);
            LastKnownFormalNames = byKey
                .Where(g => !string.IsNullOrWhiteSpace(g.First().FormalName))
                .ToDictionary(g => g.Key, g => g.First().FormalName!);
            LastKnown = result;
            return result;
        }
        catch
        {
            // Best-effort — relay unreachable, not connected, whatever. Callers fall back to
            // whatever locally-cached name they already have; nothing worse than before this existed.
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Value-equality of two directory snapshots — lets a caller skip an expensive rebuild when a background refresh returned the same names it already displayed.</summary>
    public static bool AreEquivalent(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        foreach (var (key, value) in a)
            if (!b.TryGetValue(key, out var other) || other != value)
                return false;
        return true;
    }

    /// <summary>Looks up one peer's current name in a directory built by <see cref="BuildAsync"/>, falling back to the caller's own locally-cached copy if the peer isn't in it (not yet published, relay unreachable when the directory was fetched, etc.).</summary>
    public static string Resolve(IReadOnlyDictionary<string, string> directory, byte[] publicKey, string fallback)
        => directory.TryGetValue(Convert.ToBase64String(publicKey), out var currentName) && !string.IsNullOrWhiteSpace(currentName)
            ? currentName
            : fallback;

    /// <summary>A peer's Jméno Příjmení from <see cref="LastKnownFormalNames"/>, or null if they never published one (older build, or Jméno/Příjmení not filled in).</summary>
    public static string? ResolveFormalName(byte[] publicKey)
        => LastKnownFormalNames.TryGetValue(Convert.ToBase64String(publicKey), out var formalName) ? formalName : null;

    /// <summary>
    /// Whether a peer is currently ACTIVE on the relay (2.1, 2026-09-17) — the same "nedostupný"
    /// signal for both the 1:1 chat list and a group's member list: the relay's own
    /// <c>RelayDatabase.GetDirectoryMembers</c> already excludes anyone who hasn't republished within
    /// its <c>DirectoryActiveWindow</c> (2 days), so a peer simply absent from <paramref name="directory"/>
    /// has gone stale/dead — the exact ghost-identity shape that caused the original incident. Reuses
    /// the SAME directory fetch every caller already does for name resolution (<see cref="BuildAsync"/>),
    /// no second network round trip.
    /// </summary>
    public static bool IsActive(IReadOnlyDictionary<string, string> directory, byte[] publicKey)
        => directory.ContainsKey(Convert.ToBase64String(publicKey));
}
