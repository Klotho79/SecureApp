namespace SecureApp.Presentation.Transport;

/// <summary>
/// This app is built for one specific community with one specific relay — there is no reason to
/// make every device type the address in by hand. <c>SettingsViewModel</c> pre-fills the relay
/// address field with this the first time a device opens Settings (before anything has been saved
/// to <c>ITransportSettingsRepository</c> yet); it stays freely editable afterward, this is only a
/// default. Update this if the Pi's LAN address ever changes — mirrors the address hard-coded into
/// <c>relay/SecureApp.Relay/docker-compose.yml</c>'s port binding, keep the two in sync.
/// </summary>
internal static class RelayDefaults
{
    public const string DefaultEndpoint = "ws://192.168.50.8:8080";
}
