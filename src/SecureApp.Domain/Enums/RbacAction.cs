namespace SecureApp.Domain.Enums;

/// <summary>Structural or content-mutating actions gated by <see cref="Policies.RoleAccessPolicy"/>. Browsing/opening documents is never gated.</summary>
public enum RbacAction
{
    CreateFolder,
    RenameFolder,
    DeleteFolder,
    MoveFolder,
    ImportDocument,
    RenameDocument,
    DeleteDocument,
    MoveDocument,
    CreateChatSession,
    SendMessage,
    UploadLibraryFile,
    DeleteLibraryFile,

    /// <summary>Adding/removing a member on an existing group chat — the *founder* of a group can always do this regardless of role (see <c>GroupChat.FounderPublicKey</c>'s own remarks); this action gates it for everyone else, i.e. "admin or povereny uzivatel" (an authorized user) in the user's own framing — Modifier already represents exactly that tier in this app's 3-role model, so it's granted the same as every other RbacAction rather than inventing a 4th tier.</summary>
    InviteGroupMember,
    RemoveGroupMember,

    /// <summary>Creating/editing the Logbook's checklists (name + item list) — Modifier-equivalent-to-Admin, same as most actions here, per the user's own follow-up ("modifer muže přidávat výkony do check listu"). Not Viewer, same reasoning as every other RbacAction.</summary>
    ManageLogbookChecklists,

    /// <summary>Creating/editing the Logbook's procedure-TYPE catalog specifically (the master list a <see cref="Entities.LogbookProcedureEntry"/> logs against, which is what feeds <see cref="ViewLogbookStatistics"/>) — deliberately Admin-only, not also Modifier like <see cref="ManageLogbookChecklists"/> above (the user's own explicit split: "položky zadá admin"). The first real use of the Admin/Modifier distinction <c>RoleAccessPolicy</c>'s own remarks flagged as "reserved for future admin-only features".</summary>
    ManageLogbookProcedureCatalog,

    /// <summary>Recording a Logbook procedure entry — picking an EXISTING type from the catalog and logging it. Allowed for every role, Viewer included (2026-09-10 correction of an earlier miscommunication: "každý uživatel má právo zadávat výkony" — everyone may log, only <see cref="ManageLogbookProcedureCatalog"/> stays gated).</summary>
    RecordLogbookProcedure,

    /// <summary>Viewing the Logbook's statistics rollup — Modifier-equivalent-to-Admin, not Viewer (the user's own explicit "statistiku muže videt admin a modifer").</summary>
    ViewLogbookStatistics
}
