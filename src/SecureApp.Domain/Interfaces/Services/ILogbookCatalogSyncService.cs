using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Syncs the Logbook's two admin/modifier-managed catalogs (checklist templates, procedure types)
/// across every device via the relay (2026-09-10) — the user's own direct ask, after noticing these
/// had shipped local-only (2026-09-09): "nové výkony nebo nové check listy se mají projevit u všech
/// uživatelů" (new procedure types or new checklists should show up for every user). Deliberately
/// scoped to just the two CATALOGS, not <see cref="LogbookProcedureEntry"/> (what a specific person
/// actually logged) or the statistics rollup built from it — those stay genuinely per-device/personal
/// data, same as every other local-only entity in this app; only the shared reference catalog syncs.
///
/// Plain HTTP against the relay, device-authenticated (X-Device-Id/X-Device-Secret) like
/// <c>IContactDirectoryService</c>/<c>ISharedLibraryService</c> — not admin-gated server-side (RBAC
/// in this app is enforced client-side, same as everywhere else; the relay itself trusts any already
/// admin-approved device). Deliberately unencrypted on the relay, unlike the shared file library:
/// checklist item text and procedure-type names are reference/protocol content (e.g. "CŽK", "před
/// RSI"), the same kind of non-patient-identifying data this app's pre-seeded checklists already ship
/// baked into the APK in plaintext — not clinical documents, which is what the shared library's own
/// encryption key exists to protect.
/// </summary>
public interface ILogbookCatalogSyncService
{
    /// <summary>Best-effort — publishing a just-created item is important enough that callers should surface a failure to the user (unlike e.g. <c>IContactDirectoryService.PublishSelfAsync</c>), but this method itself still shouldn't throw for a routine "not connected right now" case; check the return value.</summary>
    Task<bool> PublishChecklistAsync(LogbookChecklistTemplate template, CancellationToken ct = default);

    /// <summary>See <see cref="PublishChecklistAsync"/>'s own remarks.</summary>
    Task<bool> PublishProcedureTypeAsync(LogbookProcedureType type, CancellationToken ct = default);

    /// <summary>Every checklist currently on the relay, reconstructed with their ORIGINAL id (see <see cref="LogbookChecklistTemplate"/>'s own <c>Guid id</c> constructor) so a caller can diff against its own local copy and only add what's actually new. Throws on a genuine failure — callers are expected to catch/swallow for their own best-effort framing rather than this method hiding it.</summary>
    Task<IReadOnlyList<LogbookChecklistTemplate>> FetchChecklistsAsync(CancellationToken ct = default);

    /// <summary>See <see cref="FetchChecklistsAsync"/>'s own remarks.</summary>
    Task<IReadOnlyList<LogbookProcedureType>> FetchProcedureTypesAsync(CancellationToken ct = default);
}
