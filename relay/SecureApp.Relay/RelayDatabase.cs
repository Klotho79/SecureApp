using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SecureApp.Relay;

/// <summary>Metadata row for one file in the shared library — see <c>library_files</c>'s schema in <see cref="RelayDatabase.Initialize"/>.</summary>
public sealed record LibraryFileRecord(
    Guid Id,
    string FolderPath,
    string FileName,
    IReadOnlyList<string> Tags,
    long SizeBytes,
    Guid UploadedByDeviceId,
    DateTimeOffset UploadedAtUtc);

/// <summary>One reviewable Document Library entry — see <c>library_documents</c>'s schema in <see cref="RelayDatabase.Initialize"/>.</summary>
public sealed record LibraryDocumentRecord(
    Guid Id, string Title, string FolderPath, string Status,
    Guid? CurrentVersionId, Guid? CurrentLibraryFileId,
    Guid CreatedByDeviceId, Guid? SubmittedByDeviceId, DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

/// <summary>One content revision of a <see cref="LibraryDocumentRecord"/> — see <c>library_document_versions</c>'s schema.</summary>
public sealed record LibraryDocumentVersionRecord(Guid Id, Guid LibraryDocumentId, int VersionNumber, Guid LibraryFileId, Guid AuthorDeviceId, DateTimeOffset CreatedAtUtc, string? ChangeNote);

/// <summary>One reviewer decision — see <c>library_document_reviews</c>'s schema.</summary>
public sealed record LibraryDocumentReviewRecord(Guid Id, Guid LibraryDocumentId, Guid VersionId, Guid ReviewerDeviceId, string Decision, string? Comment, DateTimeOffset DecidedAtUtc);

/// <summary>One pending/decided activation request — see <c>Contracts.cs</c>'s own remarks for the flow this replaces.</summary>
public sealed record ActivationRequestRecord(
    Guid Id,
    string DisplayName,
    string Email,
    string KeyFingerprint,
    string Status,
    DateTimeOffset CreatedAtUtc,
    Guid? AssignedDeviceId);

/// <summary>One registered device for the admin's device-management list (2.1, 2026-09-17) — see <see cref="RelayDatabase.GetAllDevicesWithStatus"/>'s own remarks.</summary>
public sealed record RegisteredDeviceRecord(
    Guid Id,
    string DisplayName,
    DateTimeOffset CreatedAtUtc,
    string? DirectoryDisplayName,
    DateTimeOffset? LastActiveAtUtc,
    int PendingOutboxCount);

/// <summary>
/// Plain SQLite storage (not SQLCipher) for the relay's own bookkeeping — devices, invites, and
/// the store-and-forward outbox. Deliberate simplicity/security tradeoff, stated explicitly: the
/// relay never sees plaintext chat content (outbox rows hold already-ratchet-encrypted
/// <c>MessageEnvelope</c> JSON), so the only genuinely sensitive data here is device secrets —
/// and those are salted + HMAC-hashed, never stored raw.
/// </summary>
public sealed class RelayDatabase
{
    private readonly string _connectionString;

    /// <summary>Where shared-library ciphertext blobs live on disk — metadata is in SQLite (<c>library_files</c>), content is not, so a large file never bloats the SQLite file/WAL.</summary>
    public string LibraryFilesDirectory { get; }

    public RelayDatabase(IConfiguration configuration)
    {
        var dbPath = configuration["SECUREAPP_RELAY_DB_PATH"] ?? Path.Combine(AppContext.BaseDirectory, "relay.db3");
        _connectionString = $"Data Source={dbPath}";

        LibraryFilesDirectory = configuration["SECUREAPP_RELAY_LIBRARY_DIR"] ?? Path.Combine(AppContext.BaseDirectory, "library-files");
        Directory.CreateDirectory(LibraryFilesDirectory);
    }

    public string GetLibraryFilePath(Guid id) => Path.Combine(LibraryFilesDirectory, id.ToString("N") + ".bin");

    public void Initialize()
    {
        using var connection = OpenConnection();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS devices (
                id                TEXT PRIMARY KEY NOT NULL,
                secret_salt       BLOB NOT NULL,
                secret_hash       BLOB NOT NULL,
                display_name      TEXT NOT NULL,
                created_at_utc    TEXT NOT NULL
            )
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS invites (
                code                TEXT PRIMARY KEY NOT NULL,
                display_name_hint   TEXT NULL,
                expires_at_utc      TEXT NOT NULL,
                consumed_at_utc     TEXT NULL
            )
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS outbox (
                id                    TEXT PRIMARY KEY NOT NULL,
                recipient_device_id   TEXT NOT NULL,
                frame_json            TEXT NOT NULL,
                created_at_utc        TEXT NOT NULL
            )
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS ix_outbox_recipient ON outbox(recipient_device_id)");

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS library_files (
                id                       TEXT PRIMARY KEY NOT NULL,
                folder_path              TEXT NOT NULL,
                file_name                TEXT NOT NULL,
                tags_json                TEXT NOT NULL,
                size_bytes               INTEGER NOT NULL,
                content_hash             TEXT NOT NULL,
                uploaded_by_device_id    TEXT NOT NULL,
                uploaded_at_utc          TEXT NOT NULL
            )
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS ix_library_files_folder ON library_files(folder_path)");
        // 2026-09-14: is_listed distinguishes a real community-library file (1) from a PRIVATE chat
        // attachment (0) — same encrypted storage, but private ones are hidden from the library browser
        // and reachable only by the id carried in the E2EE chat message. Guarded ALTER (SQLite has no
        // ADD COLUMN IF NOT EXISTS); existing rows default to listed.
        if (!ColumnExists(connection, "library_files", "is_listed"))
            Execute(connection, "ALTER TABLE library_files ADD COLUMN is_listed INTEGER NOT NULL DEFAULT 1");

        // device_secret holds the new device's PLAINTEXT bearer secret once approved — unlike
        // `devices.secret_hash` (salted + HMAC-hashed, never recoverable), this one genuinely needs
        // to be readable back out, since it's the only channel that ever hands the secret to the
        // still-unregistered device (see GetActivationRequestSecret's remarks). Equivalent exposure
        // to what /register already does today (returns a plaintext secret over an unauthenticated
        // call, gated only by possessing a valid one-time invite code) — here the request's own
        // unguessable GUID plays that same role instead of a typed-in code.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS activation_requests (
                id                    TEXT PRIMARY KEY NOT NULL,
                display_name          TEXT NOT NULL,
                email                 TEXT NOT NULL,
                key_fingerprint       TEXT NOT NULL,
                status                TEXT NOT NULL,
                created_at_utc        TEXT NOT NULL,
                decided_at_utc        TEXT NULL,
                assigned_device_id    TEXT NULL,
                device_secret         TEXT NULL
            )
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS ix_activation_requests_status ON activation_requests(status)");

        // Member directory (2026-09-06) — one row per device that has opted in by calling
        // /directory/publish (every device does this automatically on connect once the client-side
        // feature ships; an old device that never reconnects since just never appears here, rather
        // than needing a data migration). Trust-model note, stated explicitly because it's a real
        // shift: this only makes sense because every device already went through admin-approved
        // activation above — the relay is deliberately treating "this device is registered" as
        // "safe to hand its public key to any other registered device", not just "safe to route
        // ciphertext to" like the plain `devices` table already implied. A small, admin-curated
        // community's reasonable tradeoff, not a universal one.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS directory_entries (
                device_id         TEXT PRIMARY KEY NOT NULL,
                display_name      TEXT NOT NULL,
                public_key        BLOB NOT NULL,
                updated_at_utc    TEXT NOT NULL
            )
            """);

        // Admin-assigned per-device policy (2026-09-24). Until now a device's Role was chosen by
        // whoever held the device — ICurrentUserService defaults to Admin and Settings let anyone
        // switch freely — so "admin" meant nothing across the community. Tab visibility had the same
        // shape: a per-device Preferences toggle each user set for themselves only. Both now have a
        // single source of truth here, which a device fetches for ITSELF on connect (GET /me/policy)
        // and an admin sets for anyone (POST /admin/users/{id}/policy).
        //
        // A device with NO row here keeps whatever it decides locally — deliberately NOT a silent
        // demotion to Viewer, which would lock every already-running install (including the admin's
        // own phone) out of its admin UI the moment this deploys. The admin opts each member in by
        // assigning them once; absence means "not managed yet", not "untrusted".
        //
        // hidden_tabs is a JSON array of AppShell's own preference keys (tab_chaty_visible etc.), so
        // the client needs no key mapping and a tab added later needs no schema change here.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS device_policy (
                device_id         TEXT PRIMARY KEY NOT NULL,
                role              INTEGER NULL,
                hidden_tabs       TEXT NOT NULL,
                updated_at_utc    TEXT NOT NULL
            )
            """);
        // document_reviewer (2026-10-01) — a separate, orthogonal capability for the Document Library
        // content-approval workflow, deliberately NOT folded into `role`: the user scoped review
        // capability to documents only, and a device can be Modifier-and-Reviewer at once. Same
        // guarded-ALTER pattern as library_files.is_listed (SQLite has no ADD COLUMN IF NOT EXISTS).
        if (!ColumnExists(connection, "device_policy", "document_reviewer"))
            Execute(connection, "ALTER TABLE device_policy ADD COLUMN document_reviewer INTEGER NOT NULL DEFAULT 0");

        // app_version (2026-10-01) — reported by the client on every /directory/publish call (already
        // fired on nearly every app launch, see HttpContactDirectoryService.PublishSelfAsync's own
        // remarks), so the admin's member screen can see at a glance who is on an outdated build
        // without having to ask each member to read it off their own Settings screen. Nullable: an
        // older client that predates this field simply never sends it, same tolerance as every other
        // guarded-ALTER column in this file.
        if (!ColumnExists(connection, "directory_entries", "app_version"))
            Execute(connection, "ALTER TABLE directory_entries ADD COLUMN app_version TEXT NULL");

        // Document Library content-approval workflow (2026-10-01) — layered ALONGSIDE library_files,
        // never migrating it: an existing library_files row with no matching row here simply has no
        // review history, still found the normal way via GET /library/files. Each version's actual
        // encrypted content IS an ordinary library_files row (library_file_id below) — staging a
        // version reuses the existing upload endpoint verbatim, same call a private chat attachment
        // already uses (listed=false), so no new crypto/storage code exists anywhere in this feature.
        // Status: Draft -> PendingReview -> Published | Rejected (Rejected loops back to Draft via a
        // new version + resubmit). Approve IS publish — one step, matching TryPublishLibraryFile's own
        // single-step shape rather than adding a separate release step nothing else here has.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS library_documents (
                id                        TEXT PRIMARY KEY NOT NULL,
                title                     TEXT NOT NULL,
                folder_path               TEXT NOT NULL,
                status                    TEXT NOT NULL,
                current_version_id        TEXT NULL,
                current_library_file_id   TEXT NULL,
                created_by_device_id      TEXT NOT NULL,
                submitted_by_device_id    TEXT NULL,
                submitted_at_utc          TEXT NULL,
                created_at_utc            TEXT NOT NULL,
                updated_at_utc            TEXT NOT NULL
            )
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS ix_library_documents_status ON library_documents(status)");

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS library_document_versions (
                id                      TEXT PRIMARY KEY NOT NULL,
                library_document_id     TEXT NOT NULL,
                version_number          INTEGER NOT NULL,
                library_file_id         TEXT NOT NULL,
                author_device_id        TEXT NOT NULL,
                created_at_utc          TEXT NOT NULL,
                change_note             TEXT NULL
            )
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS ix_library_document_versions_doc ON library_document_versions(library_document_id)");

        // Append-only — the direct audit-trail twin of document_downloads, but for approval decisions
        // rather than downloads. Admin-only search via GET /admin/library-documents/audit.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS library_document_reviews (
                id                      TEXT PRIMARY KEY NOT NULL,
                library_document_id     TEXT NOT NULL,
                version_id              TEXT NOT NULL,
                reviewer_device_id      TEXT NOT NULL,
                decision                TEXT NOT NULL,
                comment                 TEXT NULL,
                decided_at_utc          TEXT NOT NULL
            )
            """);

        // Notice board (2026-09-24) — Admin/Modifier post a message that every member sees on the
        // Nastenka tab. content_blob is OPAQUE to the relay: the client encrypts it with the shared
        // community library key (which every member already holds via the wrapped-key escrow above),
        // so the board gets the same "relay stores ciphertext it cannot read" treatment as library
        // files, rather than becoming the one plaintext channel in an otherwise encrypted app.
        //
        // The author's display name is deliberately NOT stored here — it is resolved against the
        // CURRENT directory at read time, same as GetRecentDiagnosticLogs already does, so a rename
        // applies retroactively instead of freezing whatever the name was when posting.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS board_posts (
                id                 TEXT PRIMARY KEY NOT NULL,
                author_device_id   TEXT NOT NULL,
                content_blob       TEXT NOT NULL,
                created_at_utc     TEXT NOT NULL
            )
            """);

        // Shared-library-key escrow (2026-09-11) — the robust, fully-automatic way a device gets the
        // community's shared library key with zero user action, replacing the fragile peer-to-peer-
        // over-the-chat-ratchet delivery that kept failing on broken sessions / both-devices-online
        // timing. A device that HAS the key wraps it (ML-KEM encapsulation to each other member's
        // published directory public key + AES-GCM) and stores the per-recipient ciphertext here; a
        // device that LACKS it fetches its own wrapped blob whenever it comes online and unwraps with
        // its own private identity key. The relay only ever holds ML-KEM ciphertext it cannot read —
        // same trust boundary as library_files (ciphertext-only) — so escrowing here does NOT let the
        // relay decrypt the library. One row per recipient device; any key-holder may (re)write it.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS wrapped_library_keys (
                recipient_device_id TEXT PRIMARY KEY NOT NULL,
                wrapped_blob        TEXT NOT NULL,
                updated_at_utc      TEXT NOT NULL
            )
            """);

        // Shared diagnostics log (2026-09-10) — the user's own ask, straight after finishing the
        // S23+ WireGuard tunnel: a place any device can report an error to, and any device (or an
        // operator with SSH into the relay) can read from, instead of debugging always needing
        // physical access to whichever device hit the problem. Plain SQLite like everything else
        // here — device_id is stored raw (not resolved to a display name at write time) so a
        // rename after the fact still shows up correctly when read back, same reasoning as
        // directory_entries/DirectoryNameResolver already established for chat.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS diagnostic_logs (
                id                    TEXT PRIMARY KEY NOT NULL,
                device_id             TEXT NOT NULL,
                level                 TEXT NOT NULL,
                message               TEXT NOT NULL,
                context               TEXT NULL,
                exception_details     TEXT NULL,
                created_at_utc        TEXT NOT NULL
            )
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS ix_diagnostic_logs_created ON diagnostic_logs(created_at_utc)");

        // Per-device app log (2026-09-26, user's ask: every device's own errors/metrics log lands here
        // automatically, readable by the admin only — members never have to send anything). Raw
        // lines exactly as the app's AppLog wrote them; capped per device+kind, see AppendAppLogLines.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS device_app_logs (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                device_id         TEXT NOT NULL,
                kind              TEXT NOT NULL,
                line              TEXT NOT NULL,
                received_at_utc   TEXT NOT NULL
            )
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS ix_device_app_logs_device ON device_app_logs(device_id, kind, id)");

        // Logbook catalog sync (2026-09-10) — see ILogbookCatalogSyncService's own remarks for why
        // this is plaintext (reference/protocol content, not patient data) and device-authenticated
        // rather than admin-gated. Upsert-by-id (see UpsertLogbookChecklist/UpsertLogbookProcedureType)
        // rather than insert-only, so a future edit-and-republish doesn't need a separate code path —
        // nothing calls that yet, but the shape costs nothing extra today.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS logbook_checklists (
                id                TEXT PRIMARY KEY NOT NULL,
                name              TEXT NOT NULL,
                items_json        TEXT NOT NULL,
                created_at_utc    TEXT NOT NULL,
                updated_at_utc    TEXT NOT NULL
            )
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS logbook_procedure_types (
                id                TEXT PRIMARY KEY NOT NULL,
                name              TEXT NOT NULL,
                abbreviation      TEXT NOT NULL DEFAULT '',
                category          TEXT NOT NULL,
                created_at_utc    TEXT NOT NULL,
                updated_at_utc    TEXT NOT NULL
            )
            """);
        // Shared company phone/extension directory (2026-09-20) — same device-authenticated,
        // not-admin-gated, plaintext-on-purpose shape as the two logbook_* tables right above (see
        // SharedContact's own remarks): this is reference/contact data, not a clinical document.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS shared_contacts (
                id                TEXT PRIMARY KEY NOT NULL,
                display_name      TEXT NOT NULL,
                phone             TEXT NULL,
                note              TEXT NULL,
                sort_order        INTEGER NOT NULL,
                created_at_utc    TEXT NOT NULL,
                updated_at_utc    TEXT NOT NULL
            )
            """);
        // Identity backup (2026-09-29, disaster recovery — see IIdentityBackupService's own remarks).
        // lookup_key is SHA-256(email+passphrase), computed CLIENT-SIDE only — this relay never sees
        // the email or passphrase themselves, just this derived key and an envelope it cannot decrypt
        // (AES-256-GCM, keyed by a PBKDF2 derivation of that same passphrase, also never sent here).
        // Deliberately unauthenticated, same accepted tradeoff as activation_requests below: a device
        // that just lost its local data has no device credential left to authenticate with.
        // Document download audit log (2026-09-30, user's own ask: "bude log kdo co kdy stahl podle
        // dokumentu vyhledatelny") — one row per "Stáhnout" tap in DocumentViewerPage. Device-authed
        // to WRITE (any already-activated device, same trust model as /contacts), admin-secret to
        // READ/search (see /admin/document-downloads) — a download log is exactly the kind of thing
        // only an admin/the institution should be able to browse, not every other device.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS document_downloads (
                id                      TEXT PRIMARY KEY NOT NULL,
                device_id               TEXT NOT NULL,
                display_name            TEXT NOT NULL,
                document_title          TEXT NOT NULL,
                source_library_file_id  TEXT NULL,
                downloaded_at_utc       TEXT NOT NULL
            )
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS identity_backups (
                lookup_key        TEXT PRIMARY KEY NOT NULL,
                envelope_json     TEXT NOT NULL,
                updated_at_utc    TEXT NOT NULL
            )
            """);
        // Shared company workplace catalog (2026-09-20, NOTIFICATION_HUB_SPEC.md Phase 5) — same
        // reference-catalog shape as shared_contacts right above; the personal day-by-day schedule
        // that references these by id/name snapshot stays local per-device (see WorkAssignment's
        // own remarks), only the list of possible workplace NAMES is shared/company-wide.
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS workplaces (
                id                TEXT PRIMARY KEY NOT NULL,
                name              TEXT NOT NULL,
                description       TEXT NULL,
                created_at_utc    TEXT NOT NULL,
                updated_at_utc    TEXT NOT NULL
            )
            """);
        // Additive column for a relay data directory that already has this table from before
        // 2026-09-10 — SQLite has no "ADD COLUMN IF NOT EXISTS", so this is guarded by checking
        // pragma_table_info first; a fresh CREATE TABLE above already includes it, so this is a
        // no-op there.
        var hasAbbreviationColumn = false;
        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('logbook_procedure_types') WHERE name = 'abbreviation'";
            hasAbbreviationColumn = Convert.ToInt64(checkCommand.ExecuteScalar()) > 0;
        }
        if (!hasAbbreviationColumn)
            Execute(connection, "ALTER TABLE logbook_procedure_types ADD COLUMN abbreviation TEXT NOT NULL DEFAULT ''");
    }

    public string CreateInvite(string? displayNameHint, TimeSpan validFor, out DateTimeOffset expiresAtUtc)
    {
        var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        expiresAtUtc = DateTimeOffset.UtcNow.Add(validFor);

        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO invites (code, display_name_hint, expires_at_utc, consumed_at_utc) VALUES (@code, @hint, @expires, NULL)",
            ("@code", code), ("@hint", (object?)displayNameHint ?? DBNull.Value), ("@expires", Format(expiresAtUtc)));

        return code;
    }

    /// <summary>Validates and atomically consumes an invite code. Returns the stored display-name hint (may be null) if valid, or null if the code doesn't exist, is already used, or has expired.</summary>
    public bool TryConsumeInvite(string code)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE invites
            SET consumed_at_utc = @now
            WHERE code = @code AND consumed_at_utc IS NULL AND expires_at_utc > @now
            """;
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@now", Format(DateTimeOffset.UtcNow));
        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Every registered device, joined against its directory entry (if any — gives staleness, the
    /// same signal <c>DirectoryActiveWindow</c> already uses to hide a dead identity from the member
    /// picker) and its pending outbox queue depth (2.1, 2026-09-17) — lets an admin actually SEE which
    /// devices are stale/dead instead of only discovering it mid-incident. This is the exact shape of
    /// the original ghost-identity incident: a wiped/reinstalled device left 120 frames permanently
    /// stuck in <c>outbox</c> for a recipient that would never come back, discoverable at the time only
    /// by SSHing into the Pi and querying SQLite directly.
    /// </summary>
    public IReadOnlyList<RegisteredDeviceRecord> GetAllDevicesWithStatus()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id, d.display_name, d.created_at_utc,
                   dir.display_name AS directory_display_name, dir.updated_at_utc AS last_active_at_utc,
                   (SELECT COUNT(*) FROM outbox o WHERE o.recipient_device_id = d.id) AS pending_outbox_count
            FROM devices d
            LEFT JOIN directory_entries dir ON dir.device_id = d.id
            ORDER BY d.created_at_utc DESC
            """;

        var results = new List<RegisteredDeviceRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new RegisteredDeviceRecord(
                Guid.Parse((string)reader["id"]),
                (string)reader["display_name"],
                DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture),
                reader["directory_display_name"] is DBNull ? null : (string)reader["directory_display_name"],
                reader["last_active_at_utc"] is DBNull ? null : DateTimeOffset.Parse((string)reader["last_active_at_utc"], CultureInfo.InvariantCulture),
                Convert.ToInt32((long)reader["pending_outbox_count"])));
        }
        return results;
    }

    /// <summary>
    /// Deregisters a device entirely (2.1, 2026-09-17): deletes its <c>devices</c> row (so it can no
    /// longer authenticate), its <c>directory_entries</c> row (so it silently drops out of every other
    /// device's member picker/directory — no separate "who's this affect" step needed, the directory
    /// is already always-rebuilt-live per device), and purges every <c>outbox</c> row still queued for
    /// it as recipient (exactly the stuck-forever queue the original incident left behind). Idempotent
    /// — deregistering an already-gone or never-existing id is a harmless no-op, not an error, so a
    /// double-tap or a stale admin list never throws.
    /// </summary>
    public void DeregisterDevice(Guid deviceId)
    {
        using var connection = OpenConnection();
        Execute(connection, "DELETE FROM outbox WHERE recipient_device_id = @id", ("@id", deviceId.ToString()));
        Execute(connection, "DELETE FROM directory_entries WHERE device_id = @id", ("@id", deviceId.ToString()));
        Execute(connection, "DELETE FROM devices WHERE id = @id", ("@id", deviceId.ToString()));
    }

    public (Guid DeviceId, string Secret) CreateDevice(string displayName)
    {
        var deviceId = Guid.NewGuid();
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(secretBytes);
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashSecret(secret, salt);

        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO devices (id, secret_salt, secret_hash, display_name, created_at_utc) VALUES (@id, @salt, @hash, @name, @created)",
            ("@id", deviceId.ToString()), ("@salt", salt), ("@hash", hash), ("@name", displayName), ("@created", Format(DateTimeOffset.UtcNow)));

        return (deviceId, secret);
    }

    public bool TryAuthenticate(Guid deviceId, string secret)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT secret_salt, secret_hash FROM devices WHERE id = @id";
        command.Parameters.AddWithValue("@id", deviceId.ToString());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return false;

        var salt = (byte[])reader["secret_salt"];
        var storedHash = (byte[])reader["secret_hash"];
        var computedHash = HashSecret(secret, salt);

        return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
    }

    public void EnqueueOutbox(Guid recipientDeviceId, string frameJson)
    {
        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO outbox (id, recipient_device_id, frame_json, created_at_utc) VALUES (@id, @recipient, @json, @created)",
            ("@id", Guid.NewGuid().ToString()), ("@recipient", recipientDeviceId.ToString()), ("@json", frameJson), ("@created", Format(DateTimeOffset.UtcNow)));
    }

    /// <summary>Returns (and deletes) every queued frame for a device, oldest first — called once right after it authenticates.</summary>
    public IReadOnlyList<string> DequeueOutbox(Guid recipientDeviceId)
    {
        using var connection = OpenConnection();

        var frames = new List<string>();
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT id, frame_json FROM outbox WHERE recipient_device_id = @recipient ORDER BY created_at_utc";
            selectCommand.Parameters.AddWithValue("@recipient", recipientDeviceId.ToString());

            var ids = new List<string>();
            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                ids.Add((string)reader["id"]);
                frames.Add((string)reader["frame_json"]);
            }

            foreach (var id in ids)
                Execute(connection, "DELETE FROM outbox WHERE id = @id", ("@id", id));
        }

        return frames;
    }

    public LibraryFileRecord InsertLibraryFileMetadata(string folderPath, string fileName, IReadOnlyList<string> tags, long sizeBytes, string contentHash, Guid uploadedByDeviceId, bool listed = true)
    {
        var id = Guid.NewGuid();
        var uploadedAtUtc = DateTimeOffset.UtcNow;
        var tagsJson = JsonSerializer.Serialize(tags);

        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO library_files (id, folder_path, file_name, tags_json, size_bytes, content_hash, uploaded_by_device_id, uploaded_at_utc, is_listed) VALUES (@id, @folder, @name, @tags, @size, @hash, @uploader, @created, @listed)",
            ("@id", id.ToString()), ("@folder", folderPath), ("@name", fileName), ("@tags", tagsJson),
            ("@size", sizeBytes), ("@hash", contentHash), ("@uploader", uploadedByDeviceId.ToString()), ("@created", Format(uploadedAtUtc)), ("@listed", listed ? 1 : 0));

        return new LibraryFileRecord(id, folderPath, fileName, tags, sizeBytes, uploadedByDeviceId, uploadedAtUtc);
    }

    /// <summary>Metadata-only search — every filter is optional (an omitted one is not applied); <paramref name="query"/> matches against the file name.</summary>
    public IReadOnlyList<LibraryFileRecord> SearchLibraryFiles(string? query, string? folderPath, string? tag)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        // Only real community-library files are browsable — private chat attachments (is_listed = 0)
        // are reachable only by id via GetLibraryFile, never listed here (2026-09-14).
        var whereParts = new List<string> { "is_listed = 1" };
        if (!string.IsNullOrWhiteSpace(query))
        {
            whereParts.Add("file_name LIKE @query");
            command.Parameters.AddWithValue("@query", $"%{query}%");
        }
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            whereParts.Add("folder_path LIKE @folder");
            command.Parameters.AddWithValue("@folder", $"%{folderPath}%");
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            // Tags are stored as a JSON string array; matching the quoted form avoids a
            // substring of one tag accidentally matching a different, longer tag.
            whereParts.Add("tags_json LIKE @tag");
            command.Parameters.AddWithValue("@tag", $"%\"{tag}\"%");
        }

        command.CommandText = "SELECT id, folder_path, file_name, tags_json, size_bytes, uploaded_by_device_id, uploaded_at_utc FROM library_files"
            + (whereParts.Count > 0 ? " WHERE " + string.Join(" AND ", whereParts) : "")
            + " ORDER BY uploaded_at_utc DESC";

        var results = new List<LibraryFileRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(ReadLibraryFileRecord(reader));

        return results;
    }

    public LibraryFileRecord? GetLibraryFile(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, folder_path, file_name, tags_json, size_bytes, uploaded_by_device_id, uploaded_at_utc FROM library_files WHERE id = @id";
        command.Parameters.AddWithValue("@id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadLibraryFileRecord(reader) : null;
    }

    /// <summary>
    /// Promotes a private chat attachment to the community library (2026-09-14, 2.3 "move from local
    /// archive to global") — flips is_listed to 1 so it becomes browsable, optionally moving it into a
    /// folder. Returns false (no-op) unless the caller is the original uploader or
    /// <paramref name="isAdminOverride"/> is set (the same cooperative-role trust as delete). Already-
    /// listed files are unaffected (idempotent).
    /// </summary>
    public bool TryPublishLibraryFile(Guid id, Guid callerDeviceId, bool isAdminOverride, string? folderPath)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var setFolder = string.IsNullOrWhiteSpace(folderPath) ? "" : ", folder_path = @folder";
        command.CommandText = isAdminOverride
            ? $"UPDATE library_files SET is_listed = 1{setFolder} WHERE id = @id"
            : $"UPDATE library_files SET is_listed = 1{setFolder} WHERE id = @id AND uploaded_by_device_id = @caller";
        command.Parameters.AddWithValue("@id", id.ToString());
        if (!isAdminOverride)
            command.Parameters.AddWithValue("@caller", callerDeviceId.ToString());
        if (!string.IsNullOrWhiteSpace(folderPath))
            command.Parameters.AddWithValue("@folder", folderPath);

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>Deletes metadata only — the caller is responsible for also removing the on-disk blob (see <see cref="GetLibraryFilePath"/>). Deletes nothing (returns false) unless the caller is the original uploader or <paramref name="isAdminOverride"/> is set.</summary>
    public bool TryDeleteLibraryFile(Guid id, Guid callerDeviceId, bool isAdminOverride)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = isAdminOverride
            ? "DELETE FROM library_files WHERE id = @id"
            : "DELETE FROM library_files WHERE id = @id AND uploaded_by_device_id = @caller";
        command.Parameters.AddWithValue("@id", id.ToString());
        if (!isAdminOverride)
            command.Parameters.AddWithValue("@caller", callerDeviceId.ToString());

        return command.ExecuteNonQuery() > 0;
    }

    // --- Document Library content-approval workflow (2026-10-01) — see library_documents/
    // library_document_versions/library_document_reviews's own schema remarks above. "The latest
    // version" (whatever is currently Draft/PendingReview) is always the version row with the highest
    // version_number for a document — there is deliberately no separate "pending version" column,
    // since only one version is ever in flight at a time (ReviewAsync only acts once a document is
    // PendingReview, and SubmitLibraryDocumentForReview requires Draft first).

    public LibraryDocumentRecord CreateLibraryDocument(string title, string folderPath, Guid libraryFileId, Guid createdByDeviceId, string? changeNote)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        Execute(connection, transaction,
            """
            INSERT INTO library_documents (id, title, folder_path, status, current_version_id, current_library_file_id, created_by_device_id, submitted_by_device_id, submitted_at_utc, created_at_utc, updated_at_utc)
            VALUES (@id, @title, @folder, 'Draft', NULL, NULL, @creator, NULL, NULL, @now, @now)
            """,
            ("@id", id.ToString()), ("@title", title), ("@folder", folderPath), ("@creator", createdByDeviceId.ToString()), ("@now", Format(now)));

        InsertNextVersion(connection, transaction, id, libraryFileId, createdByDeviceId, changeNote, now);

        transaction.Commit();
        return new LibraryDocumentRecord(id, title, folderPath, "Draft", null, null, createdByDeviceId, null, null, now, now);
    }

    /// <summary>Stages a new version (e.g. revising a Rejected document) and resets status to Draft. Returns null if the document doesn't exist.</summary>
    public LibraryDocumentRecord? AddLibraryDocumentVersion(Guid documentId, Guid libraryFileId, Guid authorDeviceId, string? changeNote)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        if (GetLibraryDocument(connection, transaction, documentId) is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        InsertNextVersion(connection, transaction, documentId, libraryFileId, authorDeviceId, changeNote, now);
        Execute(connection, transaction,
            "UPDATE library_documents SET status = 'Draft', submitted_by_device_id = NULL, submitted_at_utc = NULL, updated_at_utc = @now WHERE id = @id",
            ("@id", documentId.ToString()), ("@now", Format(now)));

        var result = GetLibraryDocument(connection, transaction, documentId);
        transaction.Commit();
        return result;
    }

    private static void InsertNextVersion(SqliteConnection connection, SqliteTransaction transaction, Guid documentId, Guid libraryFileId, Guid authorDeviceId, string? changeNote, DateTimeOffset now)
    {
        using var maxCommand = connection.CreateCommand();
        maxCommand.Transaction = transaction;
        maxCommand.CommandText = "SELECT COALESCE(MAX(version_number), 0) FROM library_document_versions WHERE library_document_id = @id";
        maxCommand.Parameters.AddWithValue("@id", documentId.ToString());
        var nextVersion = Convert.ToInt32(maxCommand.ExecuteScalar()) + 1;

        Execute(connection, transaction,
            """
            INSERT INTO library_document_versions (id, library_document_id, version_number, library_file_id, author_device_id, created_at_utc, change_note)
            VALUES (@id, @docId, @versionNumber, @fileId, @author, @now, @note)
            """,
            ("@id", Guid.NewGuid().ToString()), ("@docId", documentId.ToString()), ("@versionNumber", nextVersion),
            ("@fileId", libraryFileId.ToString()), ("@author", authorDeviceId.ToString()), ("@now", Format(now)), ("@note", (object?)changeNote ?? DBNull.Value));
    }

    /// <summary>Moves the document's current (Draft) version into the reviewer queue. Returns null if the document doesn't exist or isn't currently Draft.</summary>
    public LibraryDocumentRecord? SubmitLibraryDocumentForReview(Guid documentId, Guid submittedByDeviceId)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = GetLibraryDocument(connection, transaction, documentId);
        if (existing is null || existing.Status != "Draft")
            return null;

        var now = DateTimeOffset.UtcNow;
        Execute(connection, transaction,
            "UPDATE library_documents SET status = 'PendingReview', submitted_by_device_id = @submitter, submitted_at_utc = @now, updated_at_utc = @now WHERE id = @id",
            ("@id", documentId.ToString()), ("@submitter", submittedByDeviceId.ToString()), ("@now", Format(now)));

        var result = GetLibraryDocument(connection, transaction, documentId);
        transaction.Commit();
        return result;
    }

    /// <summary>
    /// Approves (publishes) the document's current pending version: lists the new version's
    /// library_files row, unlists the PREVIOUS current one (if any — a first-ever version has none),
    /// and advances current_version_id/current_library_file_id. Records the decision in
    /// library_document_reviews. Returns null if the document doesn't exist or isn't PendingReview.
    /// </summary>
    public LibraryDocumentRecord? ApproveLibraryDocumentVersion(Guid documentId, Guid reviewerDeviceId, string? comment)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = GetLibraryDocument(connection, transaction, documentId);
        if (existing is null || existing.Status != "PendingReview")
            return null;

        var latestVersion = GetLatestVersion(connection, transaction, documentId)
            ?? throw new InvalidOperationException($"library_documents row {documentId} has no versions — data integrity bug.");

        var now = DateTimeOffset.UtcNow;
        if (existing.CurrentLibraryFileId is { } previousFileId)
            Execute(connection, transaction, "UPDATE library_files SET is_listed = 0 WHERE id = @id", ("@id", previousFileId.ToString()));
        Execute(connection, transaction, "UPDATE library_files SET is_listed = 1 WHERE id = @id", ("@id", latestVersion.LibraryFileId.ToString()));

        Execute(connection, transaction,
            "UPDATE library_documents SET status = 'Published', current_version_id = @versionId, current_library_file_id = @fileId, updated_at_utc = @now WHERE id = @id",
            ("@id", documentId.ToString()), ("@versionId", latestVersion.Id.ToString()), ("@fileId", latestVersion.LibraryFileId.ToString()), ("@now", Format(now)));

        InsertReview(connection, transaction, documentId, latestVersion.Id, reviewerDeviceId, "Approved", comment, now);

        var result = GetLibraryDocument(connection, transaction, documentId);
        transaction.Commit();
        return result;
    }

    /// <summary>Rejects the document's current pending version (not terminal — the author stages a new version and resubmits). Returns null if the document doesn't exist or isn't PendingReview.</summary>
    public LibraryDocumentRecord? RejectLibraryDocumentVersion(Guid documentId, Guid reviewerDeviceId, string comment)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = GetLibraryDocument(connection, transaction, documentId);
        if (existing is null || existing.Status != "PendingReview")
            return null;

        var latestVersion = GetLatestVersion(connection, transaction, documentId)
            ?? throw new InvalidOperationException($"library_documents row {documentId} has no versions — data integrity bug.");

        var now = DateTimeOffset.UtcNow;
        Execute(connection, transaction,
            "UPDATE library_documents SET status = 'Rejected', updated_at_utc = @now WHERE id = @id",
            ("@id", documentId.ToString()), ("@now", Format(now)));

        InsertReview(connection, transaction, documentId, latestVersion.Id, reviewerDeviceId, "Rejected", comment, now);

        var result = GetLibraryDocument(connection, transaction, documentId);
        transaction.Commit();
        return result;
    }

    private static void InsertReview(SqliteConnection connection, SqliteTransaction transaction, Guid documentId, Guid versionId, Guid reviewerDeviceId, string decision, string? comment, DateTimeOffset now)
        => Execute(connection, transaction,
            "INSERT INTO library_document_reviews (id, library_document_id, version_id, reviewer_device_id, decision, comment, decided_at_utc) VALUES (@id, @docId, @versionId, @reviewer, @decision, @comment, @now)",
            ("@id", Guid.NewGuid().ToString()), ("@docId", documentId.ToString()), ("@versionId", versionId.ToString()),
            ("@reviewer", reviewerDeviceId.ToString()), ("@decision", decision), ("@comment", (object?)comment ?? DBNull.Value), ("@now", Format(now)));

    public LibraryDocumentRecord? GetLibraryDocument(Guid id)
    {
        using var connection = OpenConnection();
        return GetLibraryDocument(connection, null, id);
    }

    private static LibraryDocumentRecord? GetLibraryDocument(SqliteConnection connection, SqliteTransaction? transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, title, folder_path, status, current_version_id, current_library_file_id, created_by_device_id, submitted_by_device_id, submitted_at_utc, created_at_utc, updated_at_utc FROM library_documents WHERE id = @id";
        command.Parameters.AddWithValue("@id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadLibraryDocument(reader) : null;
    }

    private static LibraryDocumentVersionRecord? GetLatestVersion(SqliteConnection connection, SqliteTransaction transaction, Guid documentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, library_document_id, version_number, library_file_id, author_device_id, created_at_utc, change_note FROM library_document_versions WHERE library_document_id = @id ORDER BY version_number DESC LIMIT 1";
        command.Parameters.AddWithValue("@id", documentId.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadVersion(reader) : null;
    }

    /// <summary>Every document this device created or submitted — the "Moje koncepty" list, newest first.</summary>
    public IReadOnlyList<LibraryDocumentRecord> SearchLibraryDocumentsByOwner(Guid deviceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, folder_path, status, current_version_id, current_library_file_id, created_by_device_id, submitted_by_device_id, submitted_at_utc, created_at_utc, updated_at_utc
            FROM library_documents WHERE created_by_device_id = @id OR submitted_by_device_id = @id ORDER BY updated_at_utc DESC
            """;
        command.Parameters.AddWithValue("@id", deviceId.ToString());

        var results = new List<LibraryDocumentRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadLibraryDocument(reader));
        return results;
    }

    /// <summary>Every document currently awaiting review, oldest-submitted first (fairness: first in, first reviewed).</summary>
    public IReadOnlyList<LibraryDocumentRecord> SearchPendingLibraryDocuments()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, folder_path, status, current_version_id, current_library_file_id, created_by_device_id, submitted_by_device_id, submitted_at_utc, created_at_utc, updated_at_utc
            FROM library_documents WHERE status = 'PendingReview' ORDER BY submitted_at_utc ASC
            """;

        var results = new List<LibraryDocumentRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadLibraryDocument(reader));
        return results;
    }

    public IReadOnlyList<LibraryDocumentVersionRecord> GetLibraryDocumentVersions(Guid documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, library_document_id, version_number, library_file_id, author_device_id, created_at_utc, change_note FROM library_document_versions WHERE library_document_id = @id ORDER BY version_number ASC";
        command.Parameters.AddWithValue("@id", documentId.ToString());

        var results = new List<LibraryDocumentVersionRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadVersion(reader));
        return results;
    }

    public IReadOnlyList<LibraryDocumentReviewRecord> GetLibraryDocumentReviews(Guid documentId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, library_document_id, version_id, reviewer_device_id, decision, comment, decided_at_utc FROM library_document_reviews WHERE library_document_id = @id ORDER BY decided_at_utc ASC";
        command.Parameters.AddWithValue("@id", documentId.ToString());

        var results = new List<LibraryDocumentReviewRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadReview(reader));
        return results;
    }

    /// <summary>Admin-only audit search (2026-10-01) — <paramref name="query"/> matches the OWNING document's title as a case-insensitive substring; null/empty returns every logged decision, newest first. The approval-workflow twin of <see cref="SearchDocumentDownloads"/>.</summary>
    public IReadOnlyList<LibraryDocumentReviewRecord> SearchLibraryDocumentReviews(string? query)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(query)
            ? "SELECT r.id, r.library_document_id, r.version_id, r.reviewer_device_id, r.decision, r.comment, r.decided_at_utc FROM library_document_reviews r ORDER BY r.decided_at_utc DESC"
            : """
              SELECT r.id, r.library_document_id, r.version_id, r.reviewer_device_id, r.decision, r.comment, r.decided_at_utc
              FROM library_document_reviews r JOIN library_documents d ON d.id = r.library_document_id
              WHERE d.title LIKE @query ORDER BY r.decided_at_utc DESC
              """;
        if (!string.IsNullOrWhiteSpace(query))
            command.Parameters.AddWithValue("@query", $"%{query}%");

        var results = new List<LibraryDocumentReviewRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadReview(reader));
        return results;
    }

    private static LibraryDocumentRecord ReadLibraryDocument(SqliteDataReader reader) => new(
        Guid.Parse((string)reader["id"]),
        (string)reader["title"],
        (string)reader["folder_path"],
        (string)reader["status"],
        reader["current_version_id"] is DBNull ? null : Guid.Parse((string)reader["current_version_id"]),
        reader["current_library_file_id"] is DBNull ? null : Guid.Parse((string)reader["current_library_file_id"]),
        Guid.Parse((string)reader["created_by_device_id"]),
        reader["submitted_by_device_id"] is DBNull ? null : Guid.Parse((string)reader["submitted_by_device_id"]),
        reader["submitted_at_utc"] is DBNull ? null : DateTimeOffset.Parse((string)reader["submitted_at_utc"], CultureInfo.InvariantCulture),
        DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture),
        DateTimeOffset.Parse((string)reader["updated_at_utc"], CultureInfo.InvariantCulture));

    private static LibraryDocumentVersionRecord ReadVersion(SqliteDataReader reader) => new(
        Guid.Parse((string)reader["id"]),
        Guid.Parse((string)reader["library_document_id"]),
        Convert.ToInt32(reader["version_number"]),
        Guid.Parse((string)reader["library_file_id"]),
        Guid.Parse((string)reader["author_device_id"]),
        DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture),
        reader["change_note"] is DBNull ? null : (string)reader["change_note"]);

    private static LibraryDocumentReviewRecord ReadReview(SqliteDataReader reader) => new(
        Guid.Parse((string)reader["id"]),
        Guid.Parse((string)reader["library_document_id"]),
        Guid.Parse((string)reader["version_id"]),
        Guid.Parse((string)reader["reviewer_device_id"]),
        (string)reader["decision"],
        reader["comment"] is DBNull ? null : (string)reader["comment"],
        DateTimeOffset.Parse((string)reader["decided_at_utc"], CultureInfo.InvariantCulture));

    public Guid CreateActivationRequest(string displayName, string email, string keyFingerprint)
    {
        var id = Guid.NewGuid();
        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO activation_requests (id, display_name, email, key_fingerprint, status, created_at_utc, decided_at_utc, assigned_device_id) VALUES (@id, @name, @email, @fp, 'Pending', @created, NULL, NULL)",
            ("@id", id.ToString()), ("@name", displayName), ("@email", email), ("@fp", keyFingerprint), ("@created", Format(DateTimeOffset.UtcNow)));
        return id;
    }

    public ActivationRequestRecord? GetActivationRequest(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, display_name, email, key_fingerprint, status, created_at_utc, assigned_device_id FROM activation_requests WHERE id = @id";
        command.Parameters.AddWithValue("@id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadActivationRequestRecord(reader) : null;
    }

    public IReadOnlyList<ActivationRequestRecord> GetPendingActivationRequests()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, display_name, email, key_fingerprint, status, created_at_utc, assigned_device_id FROM activation_requests WHERE status = 'Pending' ORDER BY created_at_utc";

        var results = new List<ActivationRequestRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(ReadActivationRequestRecord(reader));
        return results;
    }

    /// <summary>Atomically approves a still-pending request: mints a new device credential (same underlying <see cref="CreateDevice"/> as the old invite-code path) and records it against the request. Returns null if the request doesn't exist or was already decided (approved/rejected) — e.g. a double-tap on "Potvrdit", or someone else already acted on it.</summary>
    public (Guid DeviceId, string Secret)? ApproveActivationRequest(Guid id)
    {
        using var connection = OpenConnection();
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "SELECT display_name FROM activation_requests WHERE id = @id AND status = 'Pending'";
        checkCommand.Parameters.AddWithValue("@id", id.ToString());
        using var reader = checkCommand.ExecuteReader();
        if (!reader.Read())
            return null;
        var displayName = (string)reader["display_name"];
        reader.Close();

        var (deviceId, secret) = CreateDevice(displayName);

        Execute(connection,
            "UPDATE activation_requests SET status = 'Approved', decided_at_utc = @now, assigned_device_id = @device, device_secret = @secret WHERE id = @id",
            ("@now", Format(DateTimeOffset.UtcNow)), ("@device", deviceId.ToString()), ("@id", id.ToString()), ("@secret", secret));

        return (deviceId, secret);
    }

    /// <summary>Reads back the plaintext secret an approved activation request is holding for its still-polling device — see the `device_secret` column's own remarks in <see cref="Initialize"/> for why this one genuinely needs to be recoverable, unlike a device's own stored secret hash. Returns null for a request that isn't Approved (or doesn't exist) — never partially exposes a secret before a human has actually decided.</summary>
    public string? GetActivationRequestSecret(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT device_secret FROM activation_requests WHERE id = @id AND status = 'Approved'";
        command.Parameters.AddWithValue("@id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() && reader["device_secret"] is string secret ? secret : null;
    }

    /// <summary>Returns false if the request doesn't exist or was already decided — same "someone/something else got there first" reasoning as <see cref="ApproveActivationRequest"/>.</summary>
    public bool RejectActivationRequest(Guid id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE activation_requests SET status = 'Rejected', decided_at_utc = @now WHERE id = @id AND status = 'Pending'";
        command.Parameters.AddWithValue("@now", Format(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("@id", id.ToString());
        return command.ExecuteNonQuery() > 0;
    }

    private static ActivationRequestRecord ReadActivationRequestRecord(SqliteDataReader reader) => new(
        Guid.Parse((string)reader["id"]),
        (string)reader["display_name"],
        (string)reader["email"],
        (string)reader["key_fingerprint"],
        (string)reader["status"],
        DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture),
        reader["assigned_device_id"] is DBNull ? null : Guid.Parse((string)reader["assigned_device_id"]));

    public void UpsertDirectoryEntry(Guid deviceId, string displayName, byte[] publicKey, string? appVersion)
    {
        using var connection = OpenConnection();
        Execute(connection,
            """
            INSERT INTO directory_entries (device_id, display_name, public_key, updated_at_utc, app_version) VALUES (@id, @name, @key, @now, @version)
            ON CONFLICT(device_id) DO UPDATE SET display_name = @name, public_key = @key, updated_at_utc = @now, app_version = @version
            """,
            ("@id", deviceId.ToString()), ("@name", displayName), ("@key", publicKey), ("@now", Format(DateTimeOffset.UtcNow)), ("@version", (object?)appVersion ?? DBNull.Value));
    }

    // --- Admin-assigned device policy (2026-09-24) — see the device_policy table's own remarks.

    /// <summary>This device's admin-assigned policy, or null when no admin has managed it yet (in which case the client keeps deciding locally, rather than being silently demoted).</summary>
    public (int? Role, IReadOnlyList<string> HiddenTabs, bool DocumentReviewer, DateTimeOffset UpdatedAtUtc)? GetDevicePolicy(Guid deviceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT role, hidden_tabs, document_reviewer, updated_at_utc FROM device_policy WHERE device_id = @id";
        command.Parameters.AddWithValue("@id", deviceId.ToString());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var role = reader["role"] is DBNull ? (int?)null : Convert.ToInt32(reader["role"]);
        return (role, DeserializeTabs((string)reader["hidden_tabs"]), Convert.ToInt32(reader["document_reviewer"]) != 0, DateTimeOffset.Parse((string)reader["updated_at_utc"]));
    }

    /// <summary>Creates or replaces one device's policy. A null <paramref name="role"/> means "leave the role to the device itself" while still applying any tab restrictions. A null <paramref name="documentReviewer"/> leaves that capability unchanged (defaults to false for a brand-new row).</summary>
    public void SetDevicePolicy(Guid deviceId, int? role, IReadOnlyList<string> hiddenTabs, bool? documentReviewer = null)
    {
        using var connection = OpenConnection();
        var existingReviewer = documentReviewer ?? GetDevicePolicy(deviceId)?.DocumentReviewer ?? false;
        Execute(connection,
            """
            INSERT INTO device_policy (device_id, role, hidden_tabs, document_reviewer, updated_at_utc) VALUES (@id, @role, @tabs, @reviewer, @now)
            ON CONFLICT(device_id) DO UPDATE SET role = @role, hidden_tabs = @tabs, document_reviewer = @reviewer, updated_at_utc = @now
            """,
            ("@id", deviceId.ToString()),
            ("@role", role is null ? DBNull.Value : role.Value),
            ("@tabs", System.Text.Json.JsonSerializer.Serialize(hiddenTabs)),
            ("@reviewer", existingReviewer ? 1 : 0),
            ("@now", Format(DateTimeOffset.UtcNow)));
    }

    /// <summary>Every registered device with whatever policy it currently has, for the admin's own management screen. Names come from the live directory (a device that has never published shows its registration-time name instead).</summary>
    public IReadOnlyList<(Guid DeviceId, string DisplayName, int? Role, IReadOnlyList<string> HiddenTabs, DateTimeOffset? LastSeenUtc, bool DocumentReviewer, string? AppVersion)> GetManagedDevices()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id,
                   COALESCE(e.display_name, d.display_name) AS display_name,
                   p.role              AS role,
                   p.hidden_tabs       AS hidden_tabs,
                   p.document_reviewer AS document_reviewer,
                   e.updated_at_utc    AS last_seen,
                   e.app_version       AS app_version
            FROM devices d
            LEFT JOIN directory_entries e ON e.device_id = d.id
            LEFT JOIN device_policy    p ON p.device_id = d.id
            ORDER BY display_name COLLATE NOCASE
            """;

        var results = new List<(Guid, string, int?, IReadOnlyList<string>, DateTimeOffset?, bool, string?)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                Guid.Parse((string)reader["id"]),
                (string)reader["display_name"],
                reader["role"] is DBNull ? null : Convert.ToInt32(reader["role"]),
                reader["hidden_tabs"] is DBNull ? Array.Empty<string>() : DeserializeTabs((string)reader["hidden_tabs"]),
                reader["last_seen"] is DBNull ? null : DateTimeOffset.Parse((string)reader["last_seen"]),
                reader["document_reviewer"] is not DBNull && Convert.ToInt32(reader["document_reviewer"]) != 0,
                reader["app_version"] is DBNull ? null : (string)reader["app_version"]));
        }

        return results;
    }

    /// <summary>Tolerates a malformed/legacy value rather than throwing — a broken policy row must never be able to stop a device from starting up.</summary>
    private static IReadOnlyList<string> DeserializeTabs(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<string>();
        }
    }

    // --- Notice board (2026-09-24) — see the board_posts table's own remarks.

    public Guid InsertBoardPost(Guid authorDeviceId, string contentBlob)
    {
        var id = Guid.NewGuid();
        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO board_posts (id, author_device_id, content_blob, created_at_utc) VALUES (@id, @author, @blob, @now)",
            ("@id", id.ToString()), ("@author", authorDeviceId.ToString()), ("@blob", contentBlob), ("@now", Format(DateTimeOffset.UtcNow)));
        return id;
    }

    /// <summary>Newest first. The author's name is resolved against the CURRENT directory, so a later rename applies retroactively.</summary>
    public IReadOnlyList<(Guid Id, Guid AuthorDeviceId, string AuthorDisplayName, string ContentBlob, DateTimeOffset CreatedAtUtc)> GetBoardPosts(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.id, b.author_device_id, b.content_blob, b.created_at_utc,
                   COALESCE(e.display_name, d.display_name, 'Neznámý') AS author_name
            FROM board_posts b
            LEFT JOIN directory_entries e ON e.device_id = b.author_device_id
            LEFT JOIN devices           d ON d.id        = b.author_device_id
            ORDER BY b.created_at_utc DESC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<(Guid, Guid, string, string, DateTimeOffset)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                Guid.Parse((string)reader["id"]),
                Guid.Parse((string)reader["author_device_id"]),
                (string)reader["author_name"],
                (string)reader["content_blob"],
                DateTimeOffset.Parse((string)reader["created_at_utc"])));
        }

        return results;
    }

    /// <summary>Deletes one post. <paramref name="requesterDeviceId"/> null means the caller authenticated as the relay admin and may delete anyone's; otherwise only the author's own post is removed.</summary>
    public bool DeleteBoardPost(Guid id, Guid? requesterDeviceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        if (requesterDeviceId is null)
        {
            command.CommandText = "DELETE FROM board_posts WHERE id = @id";
            command.Parameters.AddWithValue("@id", id.ToString());
        }
        else
        {
            command.CommandText = "DELETE FROM board_posts WHERE id = @id AND author_device_id = @author";
            command.Parameters.AddWithValue("@id", id.ToString());
            command.Parameters.AddWithValue("@author", requesterDeviceId.Value.ToString());
        }

        return command.ExecuteNonQuery() > 0;
    }

    /// <summary>How long since a device last refreshed its directory entry before it's treated as INACTIVE and hidden from the member listing (2026-09-14, the user's ask: "server hlásí jen aktivní uživatele"). A live device republishes on every relay (re)connect — the client forces a reconnect every ~5 min — so an active device is always fresh; only a wiped/abandoned identity (like the ghost founder that caused the black-hole incident) ever goes stale. Generous enough (2 days) that a device merely offline over a weekend reappears the moment it reconnects and republishes.</summary>
    private static readonly TimeSpan DirectoryActiveWindow = TimeSpan.FromDays(2);

    /// <summary>Every ACTIVE published member except the caller — a device never needs to "start a chat" with its own identity, and inactive/dead identities are filtered out (see <see cref="DirectoryActiveWindow"/>) so they never clutter the picker or get re-paired to.</summary>
    public IReadOnlyList<(Guid DeviceId, string DisplayName, byte[] PublicKey)> GetDirectoryMembers(Guid excludingDeviceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        // updated_at_utc is stored in ISO-8601 "O" format at UTC (+00:00), so lexicographic string
        // comparison is chronological — a plain >= cutoff filter selects only recently-seen devices.
        command.CommandText = "SELECT device_id, display_name, public_key FROM directory_entries WHERE device_id != @excluded AND updated_at_utc >= @cutoff ORDER BY display_name COLLATE NOCASE";
        command.Parameters.AddWithValue("@excluded", excludingDeviceId.ToString());
        command.Parameters.AddWithValue("@cutoff", Format(DateTimeOffset.UtcNow - DirectoryActiveWindow));

        var results = new List<(Guid, string, byte[])>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add((Guid.Parse((string)reader["device_id"]), (string)reader["display_name"], (byte[])reader["public_key"]));
        return results;
    }

    /// <summary>Stores (or replaces) the shared library key wrapped for one recipient device — see the wrapped_library_keys table's own remarks. The blob is opaque ciphertext to the relay.</summary>
    public void UpsertWrappedLibraryKey(Guid recipientDeviceId, string wrappedBlob)
    {
        using var connection = OpenConnection();
        Execute(connection,
            """
            INSERT INTO wrapped_library_keys (recipient_device_id, wrapped_blob, updated_at_utc) VALUES (@id, @blob, @now)
            ON CONFLICT(recipient_device_id) DO UPDATE SET wrapped_blob = @blob, updated_at_utc = @now
            """,
            ("@id", recipientDeviceId.ToString()), ("@blob", wrappedBlob), ("@now", Format(DateTimeOffset.UtcNow)));
    }

    /// <summary>Returns the wrapped shared library key stored for a device, or null if none has been escrowed for it yet.</summary>
    public string? GetWrappedLibraryKey(Guid recipientDeviceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT wrapped_blob FROM wrapped_library_keys WHERE recipient_device_id = @id";
        command.Parameters.AddWithValue("@id", recipientDeviceId.ToString());
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Inserts one diagnostic log entry and, in the same call, prunes the table so it can't grow
    /// unbounded on the Pi's limited disk: anything older than <see cref="DiagnosticLogRetention"/>
    /// is deleted, and if the table still has more than <see cref="DiagnosticLogMaxRows"/> rows
    /// after that (a genuine burst, not just old age), the oldest excess rows go too. A write-time
    /// cost rather than a separate scheduled job — simple, and this table is never write-heavy
    /// enough for that to matter.
    /// </summary>
    public void InsertDiagnosticLog(Guid deviceId, string level, string message, string? context, string? exceptionDetails)
    {
        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO diagnostic_logs (id, device_id, level, message, context, exception_details, created_at_utc) VALUES (@id, @device, @level, @message, @context, @ex, @created)",
            ("@id", Guid.NewGuid().ToString()), ("@device", deviceId.ToString()), ("@level", level), ("@message", message),
            ("@context", (object?)context ?? DBNull.Value), ("@ex", (object?)exceptionDetails ?? DBNull.Value), ("@created", Format(DateTimeOffset.UtcNow)));

        var cutoff = Format(DateTimeOffset.UtcNow - DiagnosticLogRetention);
        Execute(connection, "DELETE FROM diagnostic_logs WHERE created_at_utc < @cutoff", ("@cutoff", cutoff));
        Execute(connection,
            "DELETE FROM diagnostic_logs WHERE id IN (SELECT id FROM diagnostic_logs ORDER BY created_at_utc DESC LIMIT -1 OFFSET @max)",
            ("@max", DiagnosticLogMaxRows));
    }

    private const int AppLogMaxLinesPerDeviceKind = 5000;

    /// <summary>Appends a batch of one device's AppLog lines ("errors" or "metrics"), then trims that device+kind to its newest <see cref="AppLogMaxLinesPerDeviceKind"/> lines.</summary>
    public void AppendAppLogLines(Guid deviceId, string kind, IReadOnlyList<string> lines)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = Format(DateTimeOffset.UtcNow);
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO device_app_logs (device_id, kind, line, received_at_utc) VALUES (@device, @kind, @line, @at)";
            var pDevice = insert.Parameters.AddWithValue("@device", deviceId.ToString());
            var pKind = insert.Parameters.AddWithValue("@kind", kind);
            var pLine = insert.Parameters.AddWithValue("@line", "");
            var pAt = insert.Parameters.AddWithValue("@at", now);
            foreach (var line in lines)
            {
                pLine.Value = line;
                insert.ExecuteNonQuery();
            }
        }
        using (var trim = connection.CreateCommand())
        {
            trim.Transaction = transaction;
            trim.CommandText = """
                DELETE FROM device_app_logs WHERE device_id = @device AND kind = @kind AND id NOT IN (
                    SELECT id FROM device_app_logs WHERE device_id = @device AND kind = @kind ORDER BY id DESC LIMIT @max)
                """;
            trim.Parameters.AddWithValue("@device", deviceId.ToString());
            trim.Parameters.AddWithValue("@kind", kind);
            trim.Parameters.AddWithValue("@max", AppLogMaxLinesPerDeviceKind);
            trim.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>The newest <paramref name="limit"/> lines of one device's log, returned oldest-first (reading order).</summary>
    public IReadOnlyList<string> GetAppLogLines(Guid deviceId, string kind, int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT line FROM device_app_logs WHERE device_id = @device AND kind = @kind ORDER BY id DESC LIMIT @limit";
        command.Parameters.AddWithValue("@device", deviceId.ToString());
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@limit", limit);
        var lines = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) lines.Add(reader.GetString(0));
        lines.Reverse();
        return lines;
    }

    private static readonly TimeSpan DiagnosticLogRetention = TimeSpan.FromDays(30);
    private const int DiagnosticLogMaxRows = 5000;

    /// <summary>
    /// Most recent entries, newest first, joined against the CURRENT <c>directory_entries</c> for
    /// display name — resolved at read time rather than stored at write time, same
    /// "don't trust a stale cached name" reasoning <c>DirectoryNameResolver</c> already established
    /// for chat: a device renamed after reporting an error should still show its current name.
    /// Falls back to a short id-based placeholder for a device that's never published to the
    /// directory (reported before its first successful connect, or an old wiped-and-replaced
    /// identity — see DEVELOPMENT_PLAN.md's own notes on what a device wipe does to its identity).
    /// </summary>
    public IReadOnlyList<(Guid Id, string DeviceDisplayName, string Level, string Message, string? Context, string? ExceptionDetails, DateTimeOffset CreatedAtUtc)> GetRecentDiagnosticLogs(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.id, l.device_id, l.level, l.message, l.context, l.exception_details, l.created_at_utc, d.display_name
            FROM diagnostic_logs l
            LEFT JOIN directory_entries d ON d.device_id = l.device_id
            ORDER BY l.created_at_utc DESC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<(Guid, string, string, string, string?, string?, DateTimeOffset)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var deviceId = (string)reader["device_id"];
            var displayName = reader["display_name"] is string name ? name : $"Zařízení {deviceId[..8]}";
            results.Add((
                Guid.Parse((string)reader["id"]),
                displayName,
                (string)reader["level"],
                (string)reader["message"],
                reader["context"] is string ctx ? ctx : null,
                reader["exception_details"] is string ex ? ex : null,
                DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture)));
        }
        return results;
    }

    public void UpsertLogbookChecklist(Guid id, string name, string itemsJson, DateTimeOffset createdAtUtc)
    {
        using var connection = OpenConnection();
        Execute(connection,
            """
            INSERT INTO logbook_checklists (id, name, items_json, created_at_utc, updated_at_utc) VALUES (@id, @name, @items, @created, @now)
            ON CONFLICT(id) DO UPDATE SET name = @name, items_json = @items, updated_at_utc = @now
            """,
            ("@id", id.ToString()), ("@name", name), ("@items", itemsJson), ("@created", Format(createdAtUtc)), ("@now", Format(DateTimeOffset.UtcNow)));
    }

    public IReadOnlyList<(Guid Id, string Name, string ItemsJson, DateTimeOffset CreatedAtUtc)> GetLogbookChecklists()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, items_json, created_at_utc FROM logbook_checklists ORDER BY name";

        var results = new List<(Guid, string, string, DateTimeOffset)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add((Guid.Parse((string)reader["id"]), (string)reader["name"], (string)reader["items_json"], DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture)));
        return results;
    }

    public void UpsertLogbookProcedureType(Guid id, string name, string abbreviation, string category, DateTimeOffset createdAtUtc)
    {
        using var connection = OpenConnection();
        Execute(connection,
            """
            INSERT INTO logbook_procedure_types (id, name, abbreviation, category, created_at_utc, updated_at_utc) VALUES (@id, @name, @abbr, @category, @created, @now)
            ON CONFLICT(id) DO UPDATE SET name = @name, abbreviation = @abbr, category = @category, updated_at_utc = @now
            """,
            ("@id", id.ToString()), ("@name", name), ("@abbr", abbreviation), ("@category", category), ("@created", Format(createdAtUtc)), ("@now", Format(DateTimeOffset.UtcNow)));
    }

    public IReadOnlyList<(Guid Id, string Name, string Abbreviation, string Category, DateTimeOffset CreatedAtUtc)> GetLogbookProcedureTypes()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, abbreviation, category, created_at_utc FROM logbook_procedure_types ORDER BY name";

        var results = new List<(Guid, string, string, string, DateTimeOffset)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add((Guid.Parse((string)reader["id"]), (string)reader["name"], (string)reader["abbreviation"], (string)reader["category"], DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture)));
        return results;
    }

    /// <summary>Removes a checklist from the shared relay catalog (2026-09-10) — see <c>ILogbookCatalogSyncService.DeleteChecklistAsync</c>'s own remarks for why relay-side deletion (not just local) is what actually makes a delete stick.</summary>
    public void DeleteLogbookChecklist(Guid id)
    {
        using var connection = OpenConnection();
        Execute(connection, "DELETE FROM logbook_checklists WHERE id = @id", ("@id", id.ToString()));
    }

    /// <summary>See <see cref="DeleteLogbookChecklist"/>'s own remarks.</summary>
    public void DeleteLogbookProcedureType(Guid id)
    {
        using var connection = OpenConnection();
        Execute(connection, "DELETE FROM logbook_procedure_types WHERE id = @id", ("@id", id.ToString()));
    }

    public void UpsertSharedContact(Guid id, string displayName, string? phone, string? note, int sortOrder, DateTimeOffset createdAtUtc)
    {
        using var connection = OpenConnection();
        Execute(connection,
            """
            INSERT INTO shared_contacts (id, display_name, phone, note, sort_order, created_at_utc, updated_at_utc) VALUES (@id, @name, @phone, @note, @sort, @created, @now)
            ON CONFLICT(id) DO UPDATE SET display_name = @name, phone = @phone, note = @note, sort_order = @sort, updated_at_utc = @now
            """,
            ("@id", id.ToString()), ("@name", displayName), ("@phone", (object?)phone ?? DBNull.Value), ("@note", (object?)note ?? DBNull.Value),
            ("@sort", sortOrder), ("@created", Format(createdAtUtc)), ("@now", Format(DateTimeOffset.UtcNow)));
    }

    public IReadOnlyList<(Guid Id, string DisplayName, string? Phone, string? Note, int SortOrder, DateTimeOffset CreatedAtUtc)> GetSharedContacts()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, display_name, phone, note, sort_order, created_at_utc FROM shared_contacts ORDER BY sort_order";

        var results = new List<(Guid, string, string?, string?, int, DateTimeOffset)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                Guid.Parse((string)reader["id"]),
                (string)reader["display_name"],
                reader["phone"] is DBNull ? null : (string)reader["phone"],
                reader["note"] is DBNull ? null : (string)reader["note"],
                Convert.ToInt32(reader["sort_order"]),
                DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture)));
        }
        return results;
    }

    public void DeleteSharedContact(Guid id)
    {
        using var connection = OpenConnection();
        Execute(connection, "DELETE FROM shared_contacts WHERE id = @id", ("@id", id.ToString()));
    }

    /// <summary>One row per "Stáhnout" tap in DocumentViewerPage (2026-09-30) — see document_downloads' own remarks.</summary>
    public void LogDocumentDownload(Guid id, Guid deviceId, string displayName, string documentTitle, Guid? sourceLibraryFileId, DateTimeOffset downloadedAtUtc)
    {
        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO document_downloads (id, device_id, display_name, document_title, source_library_file_id, downloaded_at_utc) VALUES (@id, @deviceId, @name, @title, @libId, @at)",
            ("@id", id.ToString()), ("@deviceId", deviceId.ToString()), ("@name", displayName), ("@title", documentTitle),
            ("@libId", (object?)sourceLibraryFileId?.ToString() ?? DBNull.Value), ("@at", Format(downloadedAtUtc)));
    }

    /// <summary>Admin-only search (2026-09-30) — <paramref name="query"/> matches DocumentTitle as a case-insensitive substring (SQLite LIKE is ASCII-case-insensitive by default; good enough for this, same posture as the shared contact/workplace lists elsewhere in this file). Null/empty query returns everything, newest first.</summary>
    public IReadOnlyList<(Guid Id, Guid DeviceId, string DisplayName, string DocumentTitle, Guid? SourceLibraryFileId, DateTimeOffset DownloadedAtUtc)> SearchDocumentDownloads(string? query)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(query)
            ? "SELECT id, device_id, display_name, document_title, source_library_file_id, downloaded_at_utc FROM document_downloads ORDER BY downloaded_at_utc DESC"
            : "SELECT id, device_id, display_name, document_title, source_library_file_id, downloaded_at_utc FROM document_downloads WHERE document_title LIKE @query ORDER BY downloaded_at_utc DESC";
        if (!string.IsNullOrWhiteSpace(query))
            command.Parameters.AddWithValue("@query", $"%{query}%");

        var results = new List<(Guid, Guid, string, string, Guid?, DateTimeOffset)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                Guid.Parse((string)reader["id"]),
                Guid.Parse((string)reader["device_id"]),
                (string)reader["display_name"],
                (string)reader["document_title"],
                reader["source_library_file_id"] is DBNull ? null : Guid.Parse((string)reader["source_library_file_id"]),
                DateTimeOffset.Parse((string)reader["downloaded_at_utc"], CultureInfo.InvariantCulture)));
        }
        return results;
    }

    /// <summary>Upsert — a re-backup with the same email+passphrase (e.g. after rotating identity on purpose) simply overwrites the previous one.</summary>
    public void UpsertIdentityBackup(string lookupKey, string envelopeJson)
    {
        using var connection = OpenConnection();
        Execute(connection,
            """
            INSERT INTO identity_backups (lookup_key, envelope_json, updated_at_utc) VALUES (@key, @envelope, @now)
            ON CONFLICT(lookup_key) DO UPDATE SET envelope_json = @envelope, updated_at_utc = @now
            """,
            ("@key", lookupKey), ("@envelope", envelopeJson), ("@now", Format(DateTimeOffset.UtcNow)));
    }

    /// <summary>Admin-only rename (2026-09-30) — only display_name + updated_at_utc change, everything else about the row (phone, note, sort_order) is untouched. A no-op if the id doesn't exist, same "silently do nothing on a stale id" convention as the other admin write endpoints here.</summary>
    public void RenameSharedContact(Guid id, string displayName)
    {
        using var connection = OpenConnection();
        Execute(connection, "UPDATE shared_contacts SET display_name = @name, updated_at_utc = @now WHERE id = @id",
            ("@name", displayName), ("@now", Format(DateTimeOffset.UtcNow)), ("@id", id.ToString()));
    }

    public string? GetIdentityBackup(string lookupKey)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope_json FROM identity_backups WHERE lookup_key = @key";
        command.Parameters.AddWithValue("@key", lookupKey);
        return command.ExecuteScalar() as string;
    }

    public void UpsertWorkplace(Guid id, string name, string? description, DateTimeOffset createdAtUtc)
    {
        using var connection = OpenConnection();
        Execute(connection,
            """
            INSERT INTO workplaces (id, name, description, created_at_utc, updated_at_utc) VALUES (@id, @name, @description, @created, @now)
            ON CONFLICT(id) DO UPDATE SET name = @name, description = @description, updated_at_utc = @now
            """,
            ("@id", id.ToString()), ("@name", name), ("@description", (object?)description ?? DBNull.Value),
            ("@created", Format(createdAtUtc)), ("@now", Format(DateTimeOffset.UtcNow)));
    }

    public IReadOnlyList<(Guid Id, string Name, string? Description, DateTimeOffset CreatedAtUtc)> GetWorkplaces()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, description, created_at_utc FROM workplaces ORDER BY name";

        var results = new List<(Guid, string, string?, DateTimeOffset)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                Guid.Parse((string)reader["id"]),
                (string)reader["name"],
                reader["description"] is DBNull ? null : (string)reader["description"],
                DateTimeOffset.Parse((string)reader["created_at_utc"], CultureInfo.InvariantCulture)));
        }
        return results;
    }

    public void DeleteWorkplace(Guid id)
    {
        using var connection = OpenConnection();
        Execute(connection, "DELETE FROM workplaces WHERE id = @id", ("@id", id.ToString()));
    }

    private static LibraryFileRecord ReadLibraryFileRecord(SqliteDataReader reader) => new(
        Guid.Parse((string)reader["id"]),
        (string)reader["folder_path"],
        (string)reader["file_name"],
        JsonSerializer.Deserialize<List<string>>((string)reader["tags_json"]) ?? [],
        (long)(reader["size_bytes"] is long l ? l : Convert.ToInt64(reader["size_bytes"])),
        Guid.Parse((string)reader["uploaded_by_device_id"]),
        DateTimeOffset.Parse((string)reader["uploaded_at_utc"], CultureInfo.InvariantCulture));

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    /// <summary>Same as the connection-only overload, but enlisted in an in-progress transaction — used by the Document Library review workflow's multi-statement operations (e.g. approving a version touches both library_files and library_documents atomically).</summary>
    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader["name"] as string, column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static byte[] HashSecret(string secret, byte[] salt)
    {
        using var hmac = new HMACSHA256(salt);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(secret));
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
