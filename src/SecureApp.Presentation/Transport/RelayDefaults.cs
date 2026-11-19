namespace SecureApp.Presentation.Transport;

/// <summary>
/// This app is built for one specific community with one specific relay — there is no reason to
/// make every device type the address in by hand. <c>SettingsViewModel</c> pre-fills the relay
/// address field with this the first time a device opens Settings (before anything has been saved
/// to <c>ITransportSettingsRepository</c> yet); it stays freely editable afterward, this is only a
/// default.
/// </summary>
internal static class RelayDefaults
{
    /// <summary>
    /// 2026-11-19: switched from the home-LAN address (<c>ws://192.168.50.8:8080</c>) to the public
    /// TLS endpoint, now that WAN 443 is confirmed port-forwarded and reachable from outside the
    /// LAN/VPN (verified from a phone on mobile data). A brand-new device (e.g. a work PC with no
    /// admin rights to install the WireGuard client) can now register on first launch with no manual
    /// endpoint edit and no tunnel at all. Existing devices already on the LAN address are unaffected
    /// — this only changes what a device with no saved config yet starts from; the tunnel is still
    /// used wherever it's already set up (<c>App.MigrateToPublicEndpointAsync</c>/
    /// <c>FallBackToLegacyEndpointAsync</c> handle switching between the two for already-registered
    /// devices).
    /// </summary>
    public const string DefaultEndpoint = PublicEndpoint;

    /// <summary>
    /// The relay's public TLS address (static IP, self-signed cert pinned in <see cref="RelayTls"/>). A device that can
    /// reach it moves its stored endpoint here (<c>App.MigrateToPublicEndpointAsync</c>) and drops the embedded tunnel —
    /// Android allows one VPN, and a member's company VPN (FortiClient) must not lose to ours.
    /// </summary>
    public const string PublicEndpoint = "wss://212.111.15.246";

    /// <summary>True for a TLS endpoint — such a device needs no embedded WireGuard tunnel.</summary>
    public static bool IsPublicEndpoint(Uri? endpoint) =>
        endpoint is not null && (endpoint.Scheme == Uri.UriSchemeWss || endpoint.Scheme == Uri.UriSchemeHttps);
}
