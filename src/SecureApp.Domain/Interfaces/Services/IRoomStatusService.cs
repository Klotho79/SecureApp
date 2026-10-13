using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Relay-backed live sál status board (2026-10-13, user's own ask on Nástěnka). Any authenticated
/// device may call <see cref="SetStatusAsync"/> for ANY sál — the relay has no way to verify "this
/// device really is on that sál" since <see cref="Entities.WorkAssignment"/> (the rozpis) is local-only
/// and never uploaded. The own-sál-only restriction for a plain Viewer is therefore enforced
/// client-side only (the UI only offers the editable picker for the device's own today's workplace);
/// Admin/Modifier are allowed to edit any row by design (user's own ask: "modifera a admina pridavat
/// status jestli ma sal koncit..."). The list of possible status VALUES is a small curated set, settable
/// only by Admin/Modifier — the relay enforces that one server-side, same role gate already used for
/// the notice board (<c>/board</c>).
/// </summary>
public interface IRoomStatusService
{
    /// <summary>Every sál that currently has a status set, newest-updated info included.</summary>
    Task<IReadOnlyList<RoomStatus>> ListAsync(CancellationToken ct = default);

    Task<bool> SetStatusAsync(Guid workplaceId, string workplaceName, string statusText, CancellationToken ct = default);

    /// <summary>The curated list of status values a picker may offer, in display order.</summary>
    Task<IReadOnlyList<string>> GetStatusOptionsAsync(CancellationToken ct = default);

    /// <summary>Admin/Modifier only (relay-enforced) — replaces the whole options list at once.</summary>
    Task<bool> SetStatusOptionsAsync(IReadOnlyList<string> options, CancellationToken ct = default);
}
