using SQLite;

namespace SecureApp.Data.Persistence;

/// <summary>
/// Provides the single shared, opened, keyed connection to the local SQLCipher
/// database, creating/migrating its schema on first use. This is an internal
/// collaborator between the DI composition root and repository implementations
/// within this project — Domain and Presentation never see a raw connection,
/// which is why the interface lives here rather than in SecureApp.Domain.
/// </summary>
public interface ISecureDatabaseConnectionFactory
{
    Task<SQLiteAsyncConnection> GetConnectionAsync(CancellationToken ct = default);
}
