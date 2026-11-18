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
    Viewer = 2,

    /// <summary>
    /// 2026-11-18, user: "role redaktor, která je jako viewer, ale může měnit a schvalovat dokumenty; při schvalování
    /// ho musí umístit do struktury". Everything a <see cref="Viewer"/> may do, plus writing/editing teaching (CMS)
    /// documents and approving them (never their own) — see <see cref="RbacAction.AuthorCmsDocument"/> and
    /// <c>LibraryReviewPolicy</c>. Stored as 3 on the relay, like the other roles' numbers.
    /// </summary>
    Redaktor = 3
}
