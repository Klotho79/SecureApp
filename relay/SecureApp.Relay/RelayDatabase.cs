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

/// <summary>One pending/decided activation request — see <c>Contracts.cs</c>'s own remarks for the flow this replaces.</summary>
public sealed record ActivationRequestRecord(
    Guid Id,
    string DisplayName,
    string Email,
    string KeyFingerprint,
    string Status,
    DateTimeOffset CreatedAtUtc,
    Guid? AssignedDeviceId);

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

    public void UpsertDirectoryEntry(Guid deviceId, string displayName, byte[] publicKey)
    {
        using var connection = OpenConnection();
        Execute(connection,
            """
            INSERT INTO directory_entries (device_id, display_name, public_key, updated_at_utc) VALUES (@id, @name, @key, @now)
            ON CONFLICT(device_id) DO UPDATE SET display_name = @name, public_key = @key, updated_at_utc = @now
            """,
            ("@id", deviceId.ToString()), ("@name", displayName), ("@key", publicKey), ("@now", Format(DateTimeOffset.UtcNow)));
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
