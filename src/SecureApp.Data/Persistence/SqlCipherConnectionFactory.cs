using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Services;
using SQLite;

namespace SecureApp.Data.Persistence;

/// <summary>
/// <see cref="ISecureDatabaseConnectionFactory"/> backed by SQLCipher (via
/// SQLitePCLRaw.bundle_sqlcipher). The database encryption key is a random 256-bit
/// value, generated once and held in <see cref="ISecureVaultKeyStore"/> — never
/// derived from or equal to any of <c>ICryptoService</c>'s own key material.
///
/// The connection is opened and its schema created/migrated at most once per process
/// (guarded by <see cref="_initLock"/>); every caller after that gets back the same
/// <see cref="SQLiteAsyncConnection"/>, which is itself safe for concurrent use.
/// </summary>
public sealed class SqlCipherConnectionFactory : ISecureDatabaseConnectionFactory
{
    private const string DatabaseKeyVaultName = "sqlcipher:database-key";
    private const int DatabaseKeySizeBytes = 32; // 256-bit, used as a raw SQLCipher key (not a passphrase put through PBKDF2)
    private const int CurrentSchemaVersion = 12;

    private readonly DataStorageOptions _options;
    private readonly ISecureVaultKeyStore _vault;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    static SqlCipherConnectionFactory()
    {
        // Registers the SQLCipher-backed native provider with SQLitePCLRaw. Must run
        // before the first connection is opened; a static constructor guarantees that
        // regardless of how many factory instances get created.
        //
        // NOTE: this is SQLitePCLRaw.bundle_e_sqlcipher, not the older bundle_sqlcipher —
        // the latter is stuck at SQLitePCLRaw.core 1.1.14 (its last release) and throws
        // MissingMethodException the moment anything in the app graph pulls a newer core
        // (sqlite-net-base itself requires core >= 2.1.2). bundle_e_sqlcipher tracks core's
        // 2.1.x line and is the actively-maintained replacement.
        SQLitePCL.Batteries_V2.Init();
    }

    public SqlCipherConnectionFactory(DataStorageOptions options, ISecureVaultKeyStore vault)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public async Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection is not null)
            return _connection;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_connection is not null)
                return _connection;

            Directory.CreateDirectory(_options.AppDataDirectory);

            var key = await GetOrCreateDatabaseKeyAsync(ct);
            var connectionString = new SQLiteConnectionString(_options.DatabasePath, storeDateTimeAsTicks: false, key: key);
            var connection = new SQLiteAsyncConnection(connectionString);

            await MigrateAsync(connection, ct);

            _connection = connection;
            return connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<byte[]> GetOrCreateDatabaseKeyAsync(CancellationToken ct)
    {
        var existingKey = await _vault.RetrieveSecretAsync(DatabaseKeyVaultName, ct);
        if (existingKey is { Length: DatabaseKeySizeBytes })
            return existingKey;

        var newKey = new byte[DatabaseKeySizeBytes];
        RandomNumberGenerator.Fill(newKey);
        await _vault.StoreSecretAsync(DatabaseKeyVaultName, newKey, ct);
        return newKey;
    }

    private static async Task MigrateAsync(SQLiteAsyncConnection connection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var schemaVersion = await connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        if (schemaVersion < CurrentSchemaVersion)
            await ApplyPendingMigrationsAsync(connection, schemaVersion);

        // Runs every time, not just on a fresh v0→vN migration (2026-09-09 — a real gap caught
        // building this: a device already sitting at v8 from an earlier deploy, before this seed
        // data existed, would otherwise never pick it up, since the version-gated block above is
        // skipped entirely once schemaVersion >= CurrentSchemaVersion). Idempotent — checks the
        // table is actually empty first, so it never re-adds anything a user deliberately deleted.
        await SeedLogbookChecklistsIfEmptyAsync(connection);
    }

    private static async Task ApplyPendingMigrationsAsync(SQLiteAsyncConnection connection, int schemaVersion)
    {
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON");

        if (schemaVersion < 1)
            await ApplyV1SchemaAsync(connection);

        if (schemaVersion < 2)
            await ApplyV2SchemaAsync(connection);

        if (schemaVersion < 3)
            await ApplyV3SchemaAsync(connection);

        if (schemaVersion < 4)
            await ApplyV4SchemaAsync(connection);

        if (schemaVersion < 5)
            await ApplyV5SchemaAsync(connection);

        if (schemaVersion < 6)
            await ApplyV6SchemaAsync(connection);

        if (schemaVersion < 7)
            await ApplyV7SchemaAsync(connection);

        if (schemaVersion < 8)
            await ApplyV8SchemaAsync(connection);

        if (schemaVersion < 9)
            await ApplyV9SchemaAsync(connection);

        if (schemaVersion < 10)
            await ApplyV10SchemaAsync(connection);

        if (schemaVersion < 11)
            await ApplyV11SchemaAsync(connection);

        if (schemaVersion < 12)
            await ApplyV12SchemaAsync(connection);

        await connection.ExecuteAsync($"PRAGMA user_version = {CurrentSchemaVersion}");
    }

    /// <summary>
    /// Initial schema, mirroring the Milestone-1 Domain entities. Guid ids are stored as
    /// TEXT; enums as their underlying INTEGER; the value-object fields of an encrypted
    /// document (<c>FileHash</c>, <c>EncryptedPayload</c>) are flattened into their own
    /// columns rather than serialized, so the ciphertext/hash can never be read except
    /// through the columns that are actually declared BLOB/TEXT.
    /// </summary>
    private static async Task ApplyV1SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS document_folders (
                id                TEXT PRIMARY KEY NOT NULL,
                name              TEXT NOT NULL,
                parent_folder_id  TEXT NULL REFERENCES document_folders(id) ON DELETE CASCADE,
                created_at_utc    TEXT NOT NULL,
                modified_at_utc   TEXT NOT NULL
            )
            """);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_document_folders_parent ON document_folders(parent_folder_id)");

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS documents (
                id                      TEXT PRIMARY KEY NOT NULL,
                title                   TEXT NOT NULL,
                file_name               TEXT NOT NULL,
                document_type           INTEGER NOT NULL,
                original_size_bytes     INTEGER NOT NULL,
                content_hash_algorithm  INTEGER NOT NULL,
                content_hash_hex        TEXT NOT NULL,
                encryption_key_id       TEXT NOT NULL,
                encryption_algorithm    INTEGER NOT NULL,
                cipher_text             BLOB NOT NULL,
                nonce                   BLOB NOT NULL,
                auth_tag                BLOB NOT NULL,
                folder_id               TEXT NULL REFERENCES document_folders(id) ON DELETE SET NULL,
                is_favorite             INTEGER NOT NULL DEFAULT 0,
                tags                    TEXT NOT NULL DEFAULT '[]',
                created_at_utc          TEXT NOT NULL,
                modified_at_utc         TEXT NOT NULL
            )
            """);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_documents_folder ON documents(folder_id)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_documents_type ON documents(document_type)");

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS spreadsheet_datasets (
                id               TEXT PRIMARY KEY NOT NULL,
                document_id      TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                sheet_name       TEXT NOT NULL,
                row_count        INTEGER NOT NULL,
                column_count     INTEGER NOT NULL,
                created_at_utc   TEXT NOT NULL,
                modified_at_utc  TEXT NOT NULL
            )
            """);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_spreadsheet_datasets_document ON spreadsheet_datasets(document_id)");

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS encryption_key_metadata (
                id               TEXT PRIMARY KEY NOT NULL,
                algorithm        INTEGER NOT NULL,
                purpose          INTEGER NOT NULL,
                is_active        INTEGER NOT NULL,
                rotated_at_utc   TEXT NULL,
                created_at_utc   TEXT NOT NULL,
                modified_at_utc  TEXT NOT NULL
            )
            """);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_encryption_key_metadata_active ON encryption_key_metadata(purpose, is_active)");

        // Append-only audit trail (see AuditLogEntry's doc comment: never updated or
        // deleted by application code) — deliberately has NO foreign key on document_id,
        // so an entry outlives the document it refers to instead of being cascade-deleted
        // or blocking deletion.
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS audit_log_entries (
                id               TEXT PRIMARY KEY NOT NULL,
                action           INTEGER NOT NULL,
                document_id      TEXT NULL,
                details          TEXT NULL,
                created_at_utc   TEXT NOT NULL,
                modified_at_utc  TEXT NOT NULL
            )
            """);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_audit_log_entries_document ON audit_log_entries(document_id)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_audit_log_entries_created ON audit_log_entries(created_at_utc)");
    }

    /// <summary>
    /// Milestone 3 (RBAC): a single local "current user" record — see <c>IUserRepository</c>'s
    /// remarks. Deliberately just one row (no multi-user model yet), so no index is needed
    /// beyond the primary key.
    /// </summary>
    private static async Task ApplyV2SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS users (
                id                TEXT PRIMARY KEY NOT NULL,
                display_name      TEXT NOT NULL,
                role              INTEGER NOT NULL,
                created_at_utc    TEXT NOT NULL,
                modified_at_utc   TEXT NOT NULL
            )
            """);
    }

    /// <summary>
    /// Milestone 5 (E2EE Chat): Direct (1-on-1) sessions, per-session Double Ratchet bookkeeping,
    /// and messages. <c>ratchet_session_states.id</c> equals its owning session's id (1:1 — see
    /// <c>RatchetSessionState</c>'s remarks). Deleting a session cascades its ratchet state and
    /// messages; deleting a document referenced as a message attachment SETs NULL rather than
    /// deleting the message, mirroring <c>document_folders</c>' precedent — chat history must
    /// outlive a deleted document. The actual secret root/chain/message keys are never in this
    /// schema at all — they live only in the vault (<c>ISecureVaultKeyStore</c>), namespaced by
    /// session id.
    /// </summary>
    private static async Task ApplyV3SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS chat_sessions (
                id                        TEXT PRIMARY KEY NOT NULL,
                peer_display_name         TEXT NOT NULL,
                peer_identity_public_key  BLOB NOT NULL,
                local_identity_key_id     TEXT NOT NULL,
                state                     INTEGER NOT NULL,
                last_ratcheted_at_utc     TEXT NULL,
                created_at_utc            TEXT NOT NULL,
                modified_at_utc           TEXT NOT NULL
            )
            """);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS ratchet_session_states (
                id                             TEXT PRIMARY KEY NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
                send_message_number            INTEGER NOT NULL,
                receive_message_number         INTEGER NOT NULL,
                previous_send_chain_length     INTEGER NOT NULL,
                current_send_chain_public_key  BLOB NOT NULL,
                remote_ratchet_public_key      BLOB NULL,
                created_at_utc                 TEXT NOT NULL,
                modified_at_utc                TEXT NOT NULL
            )
            """);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS messages (
                id                          TEXT PRIMARY KEY NOT NULL,
                chat_session_id             TEXT NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
                direction                   INTEGER NOT NULL,
                status                      INTEGER NOT NULL,
                header_dh_public_key        BLOB NOT NULL,
                header_previous_chain_length INTEGER NOT NULL,
                header_message_number       INTEGER NOT NULL,
                payload_key_id              TEXT NOT NULL,
                payload_algorithm           INTEGER NOT NULL,
                payload_cipher_text         BLOB NOT NULL,
                payload_nonce               BLOB NOT NULL,
                payload_auth_tag            BLOB NOT NULL,
                attachment_document_id      TEXT NULL REFERENCES documents(id) ON DELETE SET NULL,
                delivered_at_utc            TEXT NULL,
                read_at_utc                 TEXT NULL,
                created_at_utc              TEXT NOT NULL,
                modified_at_utc             TEXT NOT NULL
            )
            """);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_messages_chat_session_id ON messages(chat_session_id)");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_messages_attachment_document_id ON messages(attachment_document_id)");

        // Single stored default relay endpoint (see ITransportSettingsRepository's remarks) — one
        // row, same "no index beyond the primary key" shape as `users`.
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS transport_settings (
                id                       TEXT PRIMARY KEY NOT NULL,
                endpoint_uri             TEXT NULL,
                is_auto_connect_enabled  INTEGER NOT NULL,
                last_connected_at_utc    TEXT NULL,
                created_at_utc           TEXT NOT NULL,
                modified_at_utc          TEXT NOT NULL
            )
            """);
    }

    /// <summary>
    /// Real <c>IMessageTransport</c> implementation (relay server + client) needs a routing
    /// identifier that <c>MessageEnvelope</c> itself deliberately doesn't carry (see
    /// <c>ChatSession.PeerRelayDeviceId</c>'s remarks) — additive nullable columns, no data migration needed.
    /// </summary>
    private static async Task ApplyV4SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("ALTER TABLE chat_sessions ADD COLUMN peer_relay_device_id TEXT NULL");
        await connection.ExecuteAsync("ALTER TABLE transport_settings ADD COLUMN assigned_relay_device_id TEXT NULL");
    }

    /// <summary>
    /// Chat-attachment integration with the shared community library (deferred at the end of
    /// Milestone 5, now built): a library file is identified by a relay-global id both sender and
    /// recipient can independently resolve via <c>ISharedLibraryService</c> — unlike
    /// <c>attachment_document_id</c>, which only ever meant anything on the importing device.
    /// Additive nullable columns, no data migration needed.
    /// </summary>
    private static async Task ApplyV5SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN attachment_library_file_id TEXT NULL");
        await connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN attachment_file_name TEXT NULL");
    }

    /// <summary>Activation-request flow (2026-09-06, see <c>TransportEndpointConfiguration.PendingActivationRequestId</c>'s own remarks) — additive nullable column, no data migration needed.</summary>
    private static async Task ApplyV6SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("ALTER TABLE transport_settings ADD COLUMN pending_activation_request_id TEXT NULL");
    }

    /// <summary>
    /// Group chats (2026-09-07) — see <c>GroupChat</c>'s own remarks for the crypto design (a full
    /// mesh of ordinary pairwise <c>ChatSession</c>s, not a new group ratchet). `messages` gets two
    /// additive nullable columns (a group message is still stored as ordinary pairwise-session
    /// `Message` rows, just tagged); `group_chats`/`group_members` are new tables, not migrations of
    /// anything existing.
    /// </summary>
    private static async Task ApplyV7SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN group_chat_id TEXT NULL");
        await connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN group_message_id TEXT NULL");
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_messages_group_chat_id ON messages(group_chat_id)");

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS group_chats (
                id                     TEXT PRIMARY KEY NOT NULL,
                name                   TEXT NOT NULL,
                founder_public_key     BLOB NOT NULL,
                created_at_utc         TEXT NOT NULL,
                modified_at_utc        TEXT NOT NULL
            )
            """);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS group_members (
                id                  TEXT PRIMARY KEY NOT NULL,
                group_chat_id       TEXT NOT NULL REFERENCES group_chats(id) ON DELETE CASCADE,
                display_name        TEXT NOT NULL,
                public_key          BLOB NOT NULL,
                relay_device_id     TEXT NOT NULL,
                can_invite          INTEGER NOT NULL,
                created_at_utc      TEXT NOT NULL,
                modified_at_utc     TEXT NOT NULL
            )
            """);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_group_members_group_chat_id ON group_members(group_chat_id)");
    }

    /// <summary>Logbook (2026-09-09) — checklist templates, an admin-managed procedure-type catalog, and per-entry logged procedures. See <c>LogbookChecklistTemplate</c>/<c>LogbookProcedureType</c>/<c>LogbookProcedureEntry</c>'s own remarks for why each is shaped the way it is.</summary>
    private static async Task ApplyV8SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS logbook_checklist_templates (
                id                 TEXT PRIMARY KEY NOT NULL,
                name               TEXT NOT NULL,
                items_json         TEXT NOT NULL,
                created_at_utc     TEXT NOT NULL,
                modified_at_utc    TEXT NOT NULL
            )
            """);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS logbook_procedure_types (
                id                 TEXT PRIMARY KEY NOT NULL,
                name               TEXT NOT NULL,
                category           INTEGER NOT NULL,
                created_at_utc     TEXT NOT NULL,
                modified_at_utc    TEXT NOT NULL
            )
            """);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS logbook_procedure_entries (
                id                    TEXT PRIMARY KEY NOT NULL,
                procedure_type_id     TEXT NOT NULL REFERENCES logbook_procedure_types(id) ON DELETE CASCADE,
                level                 INTEGER NOT NULL,
                performed_at_utc      TEXT NOT NULL,
                note                  TEXT NULL,
                created_at_utc        TEXT NOT NULL,
                modified_at_utc       TEXT NOT NULL
            )
            """);
        await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS ix_logbook_procedure_entries_type_id ON logbook_procedure_entries(procedure_type_id)");
    }

    /// <summary>
    /// Two Logbook refinements requested together (2026-09-10): a required short code on each
    /// procedure type (see <c>LogbookProcedureType.Abbreviation</c>'s own remarks — the statistics
    /// view leads with it) and an optional place on each logged entry (see
    /// <c>LogbookProcedureEntry.Place</c>'s own remarks). Both additive columns — existing
    /// <c>logbook_procedure_types</c> rows backfill to an empty abbreviation (there were none
    /// pre-seeded, unlike the checklists, so this never actually fires against real data on a
    /// device that's been storing its own types already) rather than leaving a NULL a NOT NULL
    /// column can't hold.
    /// </summary>
    private static async Task ApplyV9SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("ALTER TABLE logbook_procedure_types ADD COLUMN abbreviation TEXT NOT NULL DEFAULT ''");
        await connection.ExecuteAsync("ALTER TABLE logbook_procedure_entries ADD COLUMN place TEXT NULL");
    }

    /// <summary>
    /// Backs <see cref="Entities.Message.IsSystemPayload"/> (2026-09-10) — see its own remarks: the
    /// shared library key now auto-offers itself over an already-paired chat session instead of
    /// needing a manual copy/paste, and this column is what lets a system-carried message stay
    /// invisible in <c>ChatViewModel</c>/<c>GroupChatViewModel</c>'s own thread (same filtering
    /// approach already proven for <c>group_chat_id</c>). Additive, defaults every existing row to
    /// 0/false — nothing already stored was ever a system payload, since this whole mechanism is new.
    /// </summary>
    private static async Task ApplyV10SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN is_system_payload INTEGER NOT NULL DEFAULT 0");
    }

    /// <summary>
    /// Backs message deletion with RBAC (2026-09-11) — see <c>Message.OriginMessageId</c>/<c>SenderRole</c>
    /// and <c>RoleAccessPolicy.CanDeleteMessage</c>. Both additive and nullable: existing rows predate
    /// this, so they carry no cross-device correlation id (deletable locally only, never propagated)
    /// and no recorded sender role (a Modifier can't delete such a message unless it's their own —
    /// the policy's safe degradation).
    /// </summary>
    private static async Task ApplyV11SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN origin_message_id TEXT NULL");
        await connection.ExecuteAsync("ALTER TABLE messages ADD COLUMN sender_role INTEGER NULL");
    }

    /// <summary>
    /// Backs <see cref="Entities.Document.SourceLibraryFileId"/> (2026-09-11) — lets an attachment
    /// opened from a chat/shared-library reuse its already-imported local copy instead of
    /// re-downloading the file from the relay every time (the real cost of opening an attachment).
    /// Additive, nullable: existing documents were imported from local files and carry no source.
    /// </summary>
    private static async Task ApplyV12SchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("ALTER TABLE documents ADD COLUMN source_library_file_id TEXT NULL");
    }

    /// <summary>
    /// Pre-loads the five checklists straight out of the reference "Příručka a logbook začínajícího
    /// anesteziologa" (KNTB Zlín ARIM) PDF the user attached — same "give it a non-empty floor before
    /// anyone's had to type anything in" reasoning <c>LibraryViewModel.SeedCategories</c> already
    /// established for the shared library's own category list. The reference PDF nests some of
    /// these under sub-headings (e.g. "anesteziologický přístroj:" listing selftest/zdroje
    /// plynů/odpařovač/… as its own group) — flattened here into individually-checkable items, since
    /// <see cref="Entities.LogbookChecklistTemplate"/>'s own list is intentionally one flat level, not
    /// a tree. The neuroaxiální-blokáda checklist's own "koagulační status" item folds in the
    /// reference table's headline numbers (INR/APTT/Trc thresholds) rather than reproducing that
    /// whole table as a separate, non-checkable checklist — a reasonable trim for this first pass,
    /// not an oversight. Called from <see cref="MigrateAsync"/> on EVERY app start, not just a fresh
    /// migration — see that call site's own remarks — so the empty-check here is what actually makes
    /// this idempotent (never re-adds anything a user deliberately deleted).
    /// </summary>
    private static async Task SeedLogbookChecklistsIfEmptyAsync(SQLiteAsyncConnection connection)
    {
        var existingCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM logbook_checklist_templates");
        if (existingCount > 0)
            return;

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        var templates = new (string Name, string[] Items)[]
        {
            ("Den před anestezií", new[]
            {
                "Projít dokumentaci plánovaných pacientů v NIS: anamnézy, alergie, závěry konzilií, předanestetické vyšetření",
                "Seznámit se s operačními výkony",
                "Nastudovat specifika anestezie u daných výkonů",
                "V případě jakýchkoli nejasností konzultovat VČAS školitele / atestovaného lékaře",
                "Nastudovat doporučené postupy odd. ARIM k plánovaným výkonům",
                "Projít si případné hrozící komplikace a jejich řešení (kniha: Naléhavé situace na operačním sále aneb Co dělat když… – Tomáš Vymazal)",
                "Průběžně studovat: Praktická anesteziologie (Málek), Praktické postupy v anestezii (Jindrová), Anesteziologie nejen k atestaci (Vymazal), Larsen, UpToDate, orphananesthesia.eu, Doporučené postupy ČSARIM"
            }),
            ("Příchod na sál (ráno)", new[]
            {
                "Přijít na sál včas a připraven",
                "Zkontrolovat operační program",
                "Zjistit personální obsazení (koho volat v případě komplikací)",
                "Anesteziologický přístroj: proveden selftest",
                "Zdroje plynů",
                "Odpařovač",
                "Vápno",
                "Odsávačka – odzkoušet",
                "Těsnost systému – odzkoušet",
                "Funkčnost a nastavení monitorace a alarmů",
                "Vím, kde je defibrilátor",
                "Vím, kde je ambuvak",
                "Vím, kde jsou pomůcky na obtížnou intubaci",
                "Vím, kde je videolaryngoskop",
                "Vím, kde jsou přetlakové manžety",
                "Vím, kde jsou infuzní roztoky",
                "Vím, kde jsou léky: dantrolen, intralipid"
            }),
            ("Před celkovou anestezií (CA)", new[]
            {
                "Pacient: identita, informovaný souhlas, předoperační vyšetření",
                "Ověření typu výkonu a operované strany",
                "Lačnění",
                "Alergie",
                "Komplikace s CA v anamnéze",
                "Monitorace dle typu výkonu – zajistit ještě před indukcí",
                "Anest. přístroj: kontrola těsnosti okruhu, funkce odsávačky",
                "Pomůcky k zajištění dýchacích cest",
                "Léky: indukce, opioidy, relaxace, vasopresory, antagonizace",
                "Funkční a spolehlivý žilní vstup",
                "Plán A/B pro zajištění dýchacích cest",
                "ATB profylaxe",
                "Dostupnost krevních derivátů"
            }),
            ("Před RSI (SOAP-ME)", new[]
            {
                "S – Suction: funkční odsávačka umístěná u hlavy pacienta",
                "O – Oxygen: zdroj 100% kyslíku, maska s rezervoárem, adekvátní preoxygenace",
                "A – Airway: intubační rourka (zvolená velikost + jedna menší), laryngoskop (ověřené světlo), zavaděč a záložní pomůcky a plán",
                "P – Positioning: optimalizace polohy pacienta (\"sniffing position\", nebo \"ramping position\" u obézních)",
                "M – Monitoring: připojené a funkční EKG, pulzní oxymetr, neinvazivní měření tlaku a kapnografie (EtCO2)",
                "E – Equipment / Emergency: zajištěný a funkční nitrožilní (IV/IO) přístup, připravené léky a záložní plán pro případ selhání intubace"
            }),
            ("Před neuroaxiální blokádou", new[]
            {
                "Pacient: identita, informovaný souhlas, předoperační vyšetření",
                "Ověření typu výkonu a operované strany",
                "Alergie",
                "Komplikace se SA v anamnéze",
                "Koagulační status: INR pod 1,4, APTT pod 42s, trombocyty nad 80–100 (viz doporučené postupy pro konkrétní antikoagulancia)",
                "Kontraindikace: místo vpichu",
                "Kontraindikace: celková infekce",
                "Kontraindikace: neurologické onemocnění / neurologický deficit",
                "Kontraindikace: KVS – šokový stav, aortální stenóza",
                "Kontraindikace: zvýšený nitrolební tlak",
                "Kontraindikace: nespolupráce pacienta",
                "Kontraindikace: anatomie / operace páteře",
                "Monitorace",
                "i.v. vstup",
                "Pomůcky a sterilita",
                "Být připraven na konverzi do CA a řešení komplikací"
            })
        };

        foreach (var (name, items) in templates)
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO logbook_checklist_templates (id, name, items_json, created_at_utc, modified_at_utc)
                VALUES (?, ?, ?, ?, ?)
                """,
                Guid.NewGuid().ToString(),
                name,
                JsonSerializer.Serialize(items),
                now,
                now);
        }
    }
}
