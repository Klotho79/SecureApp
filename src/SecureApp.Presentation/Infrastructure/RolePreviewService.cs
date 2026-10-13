using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Admin-only, in-memory "náhled jako role" debug aid (2026-10-13, user's own ask: "v nastaveni
/// admina dej rolmenu zobrazeni co vidi jednotlive role" — the user explicitly chose a LIVE preview
/// over a static written description of what each role can see). Deliberately NOT persisted (resets
/// to "no preview" on every app start — nobody should be able to leave a device stuck simulating a
/// lower role) and deliberately NEVER consulted by anything identity-sensitive: message sender-role
/// stamping (<c>SecureApp.Data.Messaging.MessagingService</c>), <c>CurrentUserService</c>'s own
/// persisted role, or SettingsViewModel's own Account-card role picker/Save (which must always act
/// on the device's REAL role, never a preview) — only by the various ViewModels' own UI-gating
/// ("can I see/use this button/card") properties, via <see cref="Resolve"/>. A singleton so the one
/// active override is shared by every ViewModel instance across the whole app.
/// </summary>
public sealed class RolePreviewService
{
    public Role? PreviewRole { get; private set; }

    public event EventHandler? PreviewRoleChanged;

    public void SetPreviewRole(Role? role)
    {
        if (PreviewRole == role) return;
        PreviewRole = role;
        PreviewRoleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What a UI-gating check should treat the current role as — the active preview if one is set, otherwise the real role passed in.</summary>
    public Role Resolve(Role actualRole) => PreviewRole ?? actualRole;
}
