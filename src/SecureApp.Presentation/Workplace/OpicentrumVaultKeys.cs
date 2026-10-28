namespace SecureApp.Presentation.Workplace;

/// <summary>Vault key names for the Opicentrum portal's own third-party login — same pattern as <c>RelayDeviceVaultKeys</c>, a separate credential from anything SecureApp itself issues.</summary>
internal static class OpicentrumVaultKeys
{
    public const string Username = "opicentrum:username";
    public const string Password = "opicentrum:password";

    /// <summary>The nurses' read-only portal (opicentrum.cz/AROS) — a second, shared viewer account (2026-10-28).</summary>
    public const string NurseUsername = "opicentrum:nurse-username";
    public const string NursePassword = "opicentrum:nurse-password";
}
