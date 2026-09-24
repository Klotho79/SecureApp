using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// The admin-assigned policy for one device (2026-09-24) — the relay is the source of truth for
/// this, replacing the old "every device picks its own role locally" model. <see cref="Role"/> is
/// null when no admin has managed this device yet, in which case the device keeps its own local
/// role rather than being silently demoted. <see cref="HiddenTabs"/> holds AppShell preference keys
/// (e.g. <c>tab_chaty_visible</c>) the device must not show — an admin hides features that aren't
/// ready for a given member.
/// </summary>
public sealed record DevicePolicy(Role? Role, IReadOnlyList<string> HiddenTabs)
{
    public static DevicePolicy Unmanaged { get; } = new(null, Array.Empty<string>());
}

/// <summary>One member as the admin's management screen sees them (2026-09-24).</summary>
public sealed record ManagedDevice(
    Guid DeviceId,
    string DisplayName,
    Role? Role,
    IReadOnlyList<string> HiddenTabs,
    DateTimeOffset? LastSeenUtc);

/// <summary>One notice-board post, decrypted for display (2026-09-24). <see cref="AuthorDisplayName"/> is resolved by the relay against the live directory, so a rename applies retroactively.</summary>
public sealed record BoardPost(
    Guid Id,
    Guid AuthorDeviceId,
    string AuthorDisplayName,
    string Text,
    DateTimeOffset CreatedAtUtc);
