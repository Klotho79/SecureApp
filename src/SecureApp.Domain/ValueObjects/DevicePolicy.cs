using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// The admin-assigned policy for one device (2026-09-24) — the relay is the source of truth for
/// this, replacing the old "every device picks its own role locally" model. <see cref="Role"/> is
/// null when no admin has managed this device yet, in which case the device keeps its own local
/// role rather than being silently demoted. <see cref="HiddenTabs"/> holds AppShell preference keys
/// (e.g. <c>tab_chaty_visible</c>) the device must not show — an admin hides features that aren't
/// ready for a given member. <see cref="IsDocumentReviewer"/> (2026-10-01) is a separate, orthogonal
/// capability for the Document Library's content-approval workflow — deliberately NOT a <see cref="Role"/>
/// value, since the user scoped review capability to documents only; a device can be Modifier AND
/// Reviewer at once. <see cref="IsPiccTeam"/> (2026-10-12) is the same shape of orthogonal capability
/// for the PICC roster (see <c>IPiccRosterService</c>'s own remarks) — gates who may upload a new
/// roster and who may see the nurses' names in it; the on-call doctor's name is NOT gated by this.
/// Both appended as the LAST parameters (default false) so existing positional constructor calls
/// elsewhere in the codebase keep compiling unchanged.
/// </summary>
public sealed record DevicePolicy(Role? Role, IReadOnlyList<string> HiddenTabs, bool IsDocumentReviewer = false, bool IsPiccTeam = false)
{
    public static DevicePolicy Unmanaged { get; } = new(null, Array.Empty<string>(), false, false);
}

/// <summary>
/// One member as the admin's management screen sees them (2026-09-24). <see cref="IsDocumentReviewer"/>
/// (2026-10-01)/<see cref="IsPiccTeam"/> (2026-10-12) appended last — see <see cref="DevicePolicy"/>'s
/// own remarks. <see cref="AppVersion"/> (2026-10-01) is whatever the device last reported on
/// <c>/directory/publish</c> — null for an older client that predates this field, or one that has
/// never reconnected since. <see cref="HasSharedLibraryKey"/> (2026-10-12, user's own ask — surfacing
/// per-device key status in Admin so a "stuck" member is visible instead of only discoverable by
/// them reporting broken Nástěnka/library content) is the SAME self-reported flag, piggybacked on the
/// same publish call for the same reason: the relay can't know a device's local vault state any other
/// way, and a device already reports things about itself this way (<see cref="AppVersion"/>/Platform).
/// <see cref="ArimContactId"/>/<see cref="ArimContactName"/>/<see cref="ArimContactEmail"/> (2026-10-17) —
/// the same "Soukromé kontakty ARIM" link <see cref="RegisteredDevice"/> already carries, added here
/// too so the admin's "Členové" and "Zařízení" screens can be merged and grouped by the same person
/// instead of listing every device flat.
/// </summary>
public sealed record ManagedDevice(
    Guid DeviceId,
    string DisplayName,
    Role? Role,
    IReadOnlyList<string> HiddenTabs,
    DateTimeOffset? LastSeenUtc,
    bool IsDocumentReviewer = false,
    string? AppVersion = null,
    bool IsPiccTeam = false,
    bool HasSharedLibraryKey = false,
    Guid? ArimContactId = null,
    string? ArimContactName = null,
    string? ArimContactEmail = null);

/// <summary>
/// One notice-board post, decrypted for display (2026-09-24). <see cref="AuthorDisplayName"/> is
/// resolved by the relay against the live directory, so a rename applies retroactively.
/// <see cref="IsReadable"/> (2026-10-12) is false when <see cref="Text"/> is actually one of
/// <c>HttpCommunityBoardService</c>'s own placeholder strings (no/wrong shared library key on this
/// device yet) rather than real decrypted content — see <c>App.SyncBoardToNotificationsAsync</c>'s
/// own remarks on why that distinction matters: without it, every post this device can't yet read
/// gets silently marked "seen" with the placeholder as its permanent content, so the REAL text never
/// arrives even after the key shows up later. Appended last with a default so existing positional
/// constructor calls elsewhere keep compiling unchanged, same precedent as <see cref="DevicePolicy"/>.
/// </summary>
public sealed record BoardPost(
    Guid Id,
    Guid AuthorDeviceId,
    string AuthorDisplayName,
    string Text,
    DateTimeOffset CreatedAtUtc,
    bool IsReadable = true);
