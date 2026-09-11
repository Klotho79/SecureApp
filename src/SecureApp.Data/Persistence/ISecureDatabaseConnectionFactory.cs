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

    /// <summary>
    /// Opens (and migrates) the database if it isn't already, without exposing the connection type
    /// (2026-09-11) — lets the Presentation layer warm the DB at startup so the first screen that
    /// reads it doesn't pay the one-time open cost behind a spinner, without needing a reference to
    /// SQLite-net's own types. Best-effort for the caller; swallows nothing itself.
    /// </summary>
    Task EnsureInitializedAsync(CancellationToken ct = default);
}
