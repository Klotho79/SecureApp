namespace SecureApp.Presentation.Workplace;

/// <summary>Vault key names for the Opicentrum portal's own third-party login — same pattern as <c>RelayDeviceVaultKeys</c>, a separate credential from anything SecureApp itself issues.</summary>
internal static class OpicentrumVaultKeys
{
    public const string Username = "opicentrum:username";
    public const string Password = "opicentrum:password";
}
