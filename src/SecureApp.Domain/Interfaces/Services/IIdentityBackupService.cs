namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Disaster recovery (2026-09-29, born from a real incident: a debug-signed build got sideloaded over
/// a release-signed install, and Android silently uninstalled+reinstalled instead of refusing — wiping
/// the local encrypted vault, including the chat identity key, with no way back). Lets the user export
/// their chat identity (the ML-KEM key pair peers recognize them by — see
/// <see cref="ValueObjects.IdentityKeyMaterial"/>'s own remarks), encrypted with a passphrase they
/// choose, to two durable places outside the app's own wipeable storage: a local file (survives an
/// uninstall) and the relay (survives losing the phone entirely, at the cost of trusting the relay
/// operator with an ENCRYPTED blob they cannot read without the passphrase).
///
/// Deliberately does NOT back up chat history — that's genuinely impossible to do without weakening
/// the app's E2EE property (message plaintext only ever exists on the two devices that sent/received
/// it), so this only prevents re-registering as a brand-new, unrecognized identity after data loss —
/// existing peers can still trust/resync with the restored identity, but old message content already
/// lost when the local database was wiped is not recovered by this.
/// </summary>
public interface IIdentityBackupService
{
    /// <summary>
    /// Exports this device's current chat identity, encrypts it with <paramref name="passphrase"/>,
    /// writes it to a local file and hands it to the OS share sheet (so the user picks where it
    /// survives to — Drive, Files app, email to self, ...), and best-effort uploads the same encrypted
    /// envelope to the relay keyed by a value derived from <paramref name="email"/>+<paramref name="passphrase"/>
    /// (the relay only ever sees that derived key and the ciphertext, never the email or passphrase
    /// themselves). Returns the local file path. The relay upload failing (offline, relay down) does
    /// NOT fail the whole call — the local file is the primary guarantee, relay is a bonus.
    /// </summary>
    Task<string> BackupAsync(string email, string passphrase, CancellationToken ct = default);

    /// <summary>Restores identity from a local backup file's raw bytes (as picked via a file picker). Throws if the passphrase is wrong or the file isn't a recognized backup.</summary>
    Task RestoreFromFileAsync(byte[] fileBytes, string passphrase, CancellationToken ct = default);

    /// <summary>Restores identity by fetching the backup from the relay via the same email+passphrase used at backup time. Throws if nothing matches or the passphrase is wrong.</summary>
    Task RestoreFromRelayAsync(Uri relayEndpoint, string email, string passphrase, CancellationToken ct = default);
}
