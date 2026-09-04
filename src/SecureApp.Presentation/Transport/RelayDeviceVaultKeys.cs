namespace SecureApp.Presentation.Transport;

/// <summary>Shared vault key names for this device's relay identity — used by both <see cref="WebSocketMessageTransport"/> (WS auth) and <c>HttpSharedLibraryService</c> (HTTP auth), so the two can never drift apart.</summary>
internal static class RelayDeviceVaultKeys
{
    public const string DeviceSecret = "transport:relay-device-secret";

    /// <summary>The relay's <c>SECUREAPP_RELAY_ADMIN_SECRET</c>, typed in once on an admin's device via Settings so invite codes can be minted in-app instead of SSH+curl on the Pi. Only ever present on devices whose local <see cref="SecureApp.Domain.Enums.Role"/> is Admin — see <see cref="HttpRelayAdminService"/>.</summary>
    public const string AdminSecret = "transport:relay-admin-secret";
}
