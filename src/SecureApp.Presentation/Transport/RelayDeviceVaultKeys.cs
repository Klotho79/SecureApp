namespace SecureApp.Presentation.Transport;

/// <summary>Shared vault key names for this device's relay identity — used by both <see cref="WebSocketMessageTransport"/> (WS auth) and <c>HttpSharedLibraryService</c> (HTTP auth), so the two can never drift apart.</summary>
internal static class RelayDeviceVaultKeys
{
    public const string DeviceSecret = "transport:relay-device-secret";

    // No AdminSecret key here (2026-09-07, removed) — the relay's SECUREAPP_RELAY_ADMIN_SECRET is
    // deliberately never persisted on any device at all now, not even in secure storage. See
    // IRelayAdminService's own remarks for why: OS-backed vault storage only protects against
    // someone without this device's own login, not against anything already running under it.
}
