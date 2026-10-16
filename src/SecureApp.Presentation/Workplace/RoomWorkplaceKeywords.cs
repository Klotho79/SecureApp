using System.Security.Cryptography;
using System.Text;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Shared "is this an operating-room-type workplace" keyword filter, plus a stable per-name Guid
/// for it (2026-10-14, bugfix). A real Opicentrum <c>WorkplaceName</c> (free text on
/// <see cref="Domain.Entities.WorkAssignment"/>) is NEVER tied to the admin-curated shared
/// Workplace catalog's own Id — see <see cref="OpicentrumSyncService.WriteBackAsync"/>'s own remarks,
/// "WorkplaceId intentionally cleared (null)" — so code that filtered on <c>WorkplaceId is not null</c>
/// to find "my own qualifying sál" could never match anything; this is the fix. <see cref="DeterministicId"/>
/// is a pure function of the normalized name, so every device (and the relay's room_status table,
/// keyed by this Guid) agrees on the same identity for "the same" room with no lookup or network call.
///
/// Keyword history: narrowed to "sál" only on 2026-10-15 because "ostatní"/"ODS"/"oční" were also
/// matching some ambulance/ARO/ARIM assignment names. Restored on 2026-10-16 per the user's explicit
/// re-confirmation ("Ano pridej ostatni ods a ocni") after being told about that risk — if the old
/// false-positive (non-sál workplaces showing up on the "Vše" board / personal widget) resurfaces,
/// get concrete real Opicentrum workplace-name examples from the user before narrowing again, rather
/// than guessing a new keyword list.
/// </summary>
public static class RoomWorkplaceKeywords
{
    public static readonly string[] Keywords = ["sál", "ostatní", "ODS", "oční"];

    public static bool IsQualifying(string workplaceName) =>
        Keywords.Any(keyword => workplaceName.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    public static Guid DeterministicId(string workplaceName)
    {
        var normalized = workplaceName.Trim().ToUpperInvariant();
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(normalized));
        return new Guid(hash);
    }
}
