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

    public LibraryFileRecord InsertLibraryFileMetadata(string folderPath, string fileName, IReadOnlyList<string> tags, long sizeBytes, string contentHash, Guid uploadedByDeviceId)
    {
        var id = Guid.NewGuid();
        var uploadedAtUtc = DateTimeOffset.UtcNow;
        var tagsJson = JsonSerializer.Serialize(tags);

        using var connection = OpenConnection();
        Execute(connection,
            "INSERT INTO library_files (id, folder_path, file_name, tags_json, size_bytes, content_hash, uploaded_by_device_id, uploaded_at_utc) VALUES (@id, @folder, @name, @tags, @size, @hash, @uploader, @created)",
            ("@id", id.ToString()), ("@folder", folderPath), ("@name", fileName), ("@tags", tagsJson),
            ("@size", sizeBytes), ("@hash", contentHash), ("@uploader", uploadedByDeviceId.ToString()), ("@created", Format(uploadedAtUtc)));

        return new LibraryFileRecord(id, folderPath, fileName, tags, sizeBytes, uploadedByDeviceId, uploadedAtUtc);
    }

    /// <summary>Metadata-only search — every filter is optional (an omitted one is not applied); <paramref name="query"/> matches against the file name.</summary>
    public IReadOnlyList<LibraryFileRecord> SearchLibraryFiles(string? query, string? folderPath, string? tag)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        var whereParts = new List<string>();
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

    /// <summary>Every published member except the caller — a device never needs to "start a chat" with its own identity.</summary>
    public IReadOnlyList<(Guid DeviceId, string DisplayName, byte[] PublicKey)> GetDirectoryMembers(Guid excludingDeviceId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT device_id, display_name, public_key FROM directory_entries WHERE device_id != @excluded ORDER BY display_name COLLATE NOCASE";
        command.Parameters.AddWithValue("@excluded", excludingDeviceId.ToString());

        var results = new List<(Guid, string, byte[])>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add((Guid.Parse((string)reader["device_id"]), (string)reader["display_name"], (byte[])reader["public_key"]));
        return results;
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

    private static byte[] HashSecret(string secret, byte[] salt)
    {
        using var hmac = new HMACSHA256(salt);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(secret));
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
