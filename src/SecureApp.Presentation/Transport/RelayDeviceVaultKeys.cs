namespace SecureApp.Presentation.Transport;

/// <summary>Shared vault key names for this device's relay identity — used by both <see cref="WebSocketMessageTransport"/> (WS auth) and <c>HttpSharedLibraryService</c> (HTTP auth), so the two can never drift apart.</summary>
internal static class RelayDeviceVaultKeys
{
    public const string DeviceSecret = "transport:relay-device-secret";

    // Public-HTTPS security audit (2026-11-03) — a fresh one-time token stored alongside the device
    // secret, presented to /self-register the NEXT time this device needs to recover (stored secret
    // rejected). Replaces "reachable on the VPN/LAN" as /self-register's credential; see
    // RelayDatabase's device_recovery_tokens table remarks on the relay side. Survives
    // ClearCredentialsAsync on purpose (that call removes only DeviceSecret) — SelfRegisterAsync
    // needs to read it back AFTER credentials were cleared.
    public const string RecoveryToken = "transport:relay-recovery-token";

    // No AdminSecret key here (2026-09-07, removed) — the relay's SECUREAPP_RELAY_ADMIN_SECRET is
    // deliberately never persisted on any device at all now, not even in secure storage. See
    // IRelayAdminService's own remarks for why: OS-backed vault storage only protects against
    // someone without this device's own login, not against anything already running under it.
}
