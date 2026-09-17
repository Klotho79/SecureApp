namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One registered device as the relay's admin device-management listing reports it (2.1, 2026-09-17)
/// — see <c>IRelayAdminService.GetRegisteredDevicesAsync</c>'s own remarks. <see cref="DirectoryDisplayName"/>/
/// <see cref="LastActiveAtUtc"/> are null if the device has never published to the relay's member
/// directory, or its entry is old enough that the relay's own active-directory window would already
/// hide it from the member picker — that staleness, not <see cref="CreatedAtUtc"/>, is the actual
/// "is this a ghost identity" signal an admin needs. <see cref="PendingOutboxCount"/> surfaces exactly
/// the kind of stuck delivery queue that caused the original ghost-identity incident (120 frames
/// permanently queued for a device that never came back) instead of it only being discoverable by
/// SSHing into the relay and querying SQLite by hand.
/// </summary>
public sealed record RegisteredDevice(Guid Id, string DisplayName, DateTimeOffset CreatedAtUtc, string? DirectoryDisplayName, DateTimeOffset? LastActiveAtUtc, int PendingOutboxCount);
