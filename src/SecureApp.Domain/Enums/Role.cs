namespace SecureApp.Domain.Enums;

/// <summary>
/// Global, app-wide role for the local device's current user. Provisional model: this
/// app has no multi-user/login concept yet, so every document and folder is governed by
/// the same single role — per-folder or per-document role assignment was deliberately
/// deferred (not decided against), see DEVELOPMENT_PLAN.md's Milestone 3 note.
/// </summary>
public enum Role
{
    Admin = 0,
    Modifier = 1,
    Viewer = 2
}
