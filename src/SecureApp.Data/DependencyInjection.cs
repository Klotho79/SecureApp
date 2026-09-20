using Microsoft.Extensions.DependencyInjection;
using SecureApp.Data.Auditing;
using SecureApp.Data.Cryptography;
using SecureApp.Data.Identity;
using SecureApp.Data.Import;
using SecureApp.Data.Messaging;
using SecureApp.Data.Persistence;
using SecureApp.Data.Repositories;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Data;

/// <summary>
/// Platform-agnostic storage configuration for the Data layer.
/// The OS-specific app-data directory (e.g. FileSystem.AppDataDirectory on MAUI)
/// is supplied by the Presentation layer at startup, so this class library
/// never takes a direct dependency on MAUI/platform APIs.
/// </summary>
public sealed record DataStorageOptions(string AppDataDirectory, string DatabaseFileName = "secureapp.db3")
{
    public string DatabasePath => Path.Combine(AppDataDirectory, DatabaseFileName);
}

/// <summary>Registers Data-layer (infrastructure) services with the DI container.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Takes a factory rather than a ready-made <see cref="DataStorageOptions"/> on purpose:
    /// on Windows, resolving the platform app-data path (e.g. MAUI's FileSystem.AppDataDirectory)
    /// touches WinRT/COM before the UI thread's apartment is set up if it's called during
    /// MauiProgram.CreateMauiApp() itself, which silently kills the app on startup. Deferring
    /// evaluation to first DI resolution (after the app has actually launched) avoids that.
    /// </summary>
    public static IServiceCollection AddDataInfrastructure(this IServiceCollection services, Func<DataStorageOptions> optionsFactory, Func<string>? defaultDisplayNameFactory = null)
    {
        services.AddSingleton(_ => optionsFactory());

        services.AddSingleton<ICryptoService, BouncyCastleCryptoService>();               // ML-KEM / ML-DSA (PQC)
        services.AddSingleton<ISecureDatabaseConnectionFactory, SqlCipherConnectionFactory>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IEncryptionKeyMetadataRepository, EncryptionKeyMetadataRepository>();
        services.AddScoped<IDocumentFolderRepository, DocumentFolderRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IDocumentImportService, DocumentImportService>();
        services.AddScoped<ISpreadsheetParsingService, ExcelDataReaderSpreadsheetParsingService>();

        // Milestone 3 (RBAC): single local current-user record + role. Singleton so every
        // caller shares one cached User instance for the app's lifetime — see CurrentUserService's remarks.
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<ICurrentUserService>(sp =>
            new CurrentUserService(sp.GetRequiredService<IUserRepository>(), defaultDisplayNameFactory?.Invoke()));

        // Milestone 5 (E2EE Chat, Domain+Data only — see DEVELOPMENT_PLAN.md's note): Direct
        // (1-on-1) sessions, Double Ratchet, messages. IMessageTransport has no implementation
        // yet in this pass and is deliberately NOT registered here — same reason
        // ISecureVaultKeyStore/IDocumentRenderingService are registered from Presentation instead:
        // a real transport will need platform-specific networking APIs this library can't reference.
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IRatchetSessionStateRepository, RatchetSessionStateRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IGroupChatRepository, GroupChatRepository>();
        services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();
        services.AddScoped<ITransportSettingsRepository, TransportSettingsRepository>();
        services.AddScoped<IRatchetService, RatchetService>();
        services.AddScoped<IMessagingService, MessagingService>();

        // Logbook (2026-09-09) — checklists + an admin-managed procedure catalog + per-entry log.
        services.AddScoped<ILogbookChecklistRepository, LogbookChecklistRepository>();
        services.AddScoped<ILogbookProcedureTypeRepository, LogbookProcedureTypeRepository>();
        services.AddScoped<ILogbookProcedureEntryRepository, LogbookProcedureEntryRepository>();

        // Notification Hub (2026-09-20, see NOTIFICATION_HUB_SPEC.md).
        services.AddScoped<INotificationRepository, NotificationRepository>();

        // Workplace/Calendar (2026-09-20, NOTIFICATION_HUB_SPEC.md Phase 5) — the personal schedule
        // itself is local per-device; the shared Workplace catalog it references is relay-synced
        // (IWorkplaceCatalogService, registered from Presentation like every other Http*Service).
        services.AddScoped<IWorkAssignmentRepository, WorkAssignmentRepository>();

        // NOTE: ISecureVaultKeyStore and IDocumentRenderingService are registered from the
        // Presentation layer instead (MauiProgram.cs) — they need MAUI/platform APIs
        // (SecureStorage, SkiaSharp) that this platform-agnostic class library cannot reference.

        return services;
    }
}
