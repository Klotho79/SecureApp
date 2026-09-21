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

    /// <summary>Creating/editing the Logbook's procedure-TYPE catalog specifically (the master list a <see cref="Entities.LogbookProcedureEntry"/> logs against, which is what feeds <see cref="ViewLogbookStatistics"/>) — Modifier-equivalent-to-Admin, same as <see cref="ManageLogbookChecklists"/> above. Was briefly Admin-only (2026-09-09, "položky zadá admin") — walked back the same day by the user's own follow-up: "modifer a admin mohou přidávat typy výkonů i check listy".</summary>
    ManageLogbookProcedureCatalog,

    /// <summary>Recording a Logbook procedure entry — picking an EXISTING type from the catalog and logging it. Allowed for every role, Viewer included (2026-09-10 correction of an earlier miscommunication: "každý uživatel má právo zadávat výkony" — everyone may log; adding a new type to that catalog stays gated behind <see cref="ManageLogbookProcedureCatalog"/>).</summary>
    RecordLogbookProcedure,

    /// <summary>Viewing the Logbook's statistics rollup — allowed for every role, Viewer included (2026-09-10 correction: "statistiku by měl vidět i viewer"; briefly Modifier/Admin-only before this).</summary>
    ViewLogbookStatistics,

    /// <summary>
    /// Changing a <see cref="Entities.WorkAssignment"/>'s actual classification (Type/Workplace/time)
    /// or deleting it outright — 2026-09-21, the user's own explicit rule: "viewer nemuze menit
    /// pracovni zarazeni, muze psat poznamku". Not Viewer, same default-deny as every other
    /// unlisted RbacAction — no special case needed here, unlike RecordLogbookProcedure/
    /// ViewLogbookStatistics above. Writing/editing the day's Note is deliberately NOT gated by this
    /// action at all (see <c>AddAssignmentViewModel</c>'s own remarks) — every role, Viewer included,
    /// may always annotate a day.
    /// </summary>
    EditWorkAssignment
}
