using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// The PICC team's doctor/nurse roster (2026-10-12, user's own ask: "aktualni lekar by byl videt ve
/// wigetu jako posledni jako PICC, admin urci uzivatele, ktery bude videt i sestry picc teamu"). The
/// source of truth is a single xlsx a picc_team member uploads by hand whenever the roster changes —
/// the user explicitly chose this over a live Google Sheets sync ("Ok urceny uzivatel picc uzivatel
/// si to sam rucne nahraje"), so there is no scheduled fetch, no Google credential anywhere in this
/// app, and no dependency on the sheet's sharing settings staying put.
///
/// Access is split in two, enforced server-side (not just hidden in the UI — see Program.cs's
/// /picc/status and /picc/roster-pointer):
///   - Today's on-call DOCTOR's name is public to every device (exactly like the notice board) —
///     it's what the Android widget's last duty-list row shows, see <c>PiccRosterStore</c>.
///   - The full roster file (doctors AND nurses) is reachable only by picc_team/Admin devices, via
///     a pointer at the relay to an UNLISTED (<c>listed: false</c>) shared-library upload — a device
///     that was never granted picc_team can never learn that file's id.
///
/// The xlsx itself is parsed by <c>PiccRosterParser</c> (Presentation layer, since it needs
/// DocumentFormat.OpenXml) — this interface only moves bytes and the already-parsed snapshot.
/// </summary>
public interface IPiccRosterService
{
    /// <summary>Whether THIS device may upload a new roster / see the nurses' names — mirrors the server-side check, so the UI can hide the upload card, but the relay enforces it independently either way.</summary>
    Task<bool> CanManageRosterAsync(CancellationToken ct = default);

    /// <summary>
    /// Uploads a new roster xlsx: stores it unlisted in the shared library, points the relay at it
    /// (picc_team/Admin only — a 403 from the relay surfaces as an exception here if the caller lost
    /// the flag since <see cref="CanManageRosterAsync"/> was last checked), and publishes today's
    /// parsed doctor name as the new public status. Returns the parsed snapshot so the caller can
    /// show an immediate confirmation of what was found for today (and catch a format mismatch
    /// before anyone relies on it).
    /// </summary>
    Task<PiccRosterSnapshot> UploadRosterAsync(Stream xlsxContent, CancellationToken ct = default);

    /// <summary>Today's on-call doctor, for the widget — open to any device, decrypted with the shared library key. Null fields mean "nothing published yet" or "the key isn't set up on this device", never a guess.</summary>
    Task<PiccStatus?> GetCurrentStatusAsync(CancellationToken ct = default);

    /// <summary>The full roster (doctor + nurses) re-parsed from the relay's current pointer — picc_team/Admin only, throws if the relay refuses (see this interface's own remarks).</summary>
    Task<PiccRosterSnapshot?> GetFullRosterAsync(CancellationToken ct = default);
}
