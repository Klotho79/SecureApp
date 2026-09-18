namespace SecureApp.Presentation.Transport;

/// <summary>
/// Thrown when the relay explicitly rejects this device's credentials
/// (<c>authResult.Success=false</c>) — distinct from a network/timeout failure,
/// which is a transient condition worth retrying as-is. This means the device
/// registration was deleted or the secret was rotated; credentials must be cleared
/// and re-registration started automatically.
/// </summary>
public sealed class RelayUnauthorizedException : Exception
{
    public RelayUnauthorizedException() : base("Relay server odmítl přihlašovací údaje zařízení.") { }
}
