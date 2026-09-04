using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IEncryptionKeyMetadataRepository"/>
public sealed class EncryptionKeyMetadataRepository : IEncryptionKeyMetadataRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public EncryptionKeyMetadataRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<EncryptionKeyMetadata?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<EncryptionKeyMetadataRow>(
            "SELECT * FROM encryption_key_metadata WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<EncryptionKeyMetadata?> GetActiveKeyAsync(KeyPurpose purpose, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<EncryptionKeyMetadataRow>(
            "SELECT * FROM encryption_key_metadata WHERE purpose = ? AND is_active = 1 LIMIT 1", (int)purpose);
        return row is null ? null : ToEntity(row);
    }

    public async Task AddAsync(EncryptionKeyMetadata keyMetadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyMetadata);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO encryption_key_metadata (id, algorithm, purpose, is_active, rotated_at_utc, created_at_utc, modified_at_utc)
            VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            keyMetadata.Id.ToString(),
            (int)keyMetadata.Algorithm,
            (int)keyMetadata.Purpose,
            keyMetadata.IsActive ? 1 : 0,
            FormatNullable(keyMetadata.RotatedAtUtc),
            Format(keyMetadata.CreatedAtUtc),
            Format(keyMetadata.ModifiedAtUtc));
    }

    public async Task UpdateAsync(EncryptionKeyMetadata keyMetadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyMetadata);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync(
            """
            UPDATE encryption_key_metadata
            SET algorithm = ?, purpose = ?, is_active = ?, rotated_at_utc = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            (int)keyMetadata.Algorithm,
            (int)keyMetadata.Purpose,
            keyMetadata.IsActive ? 1 : 0,
            FormatNullable(keyMetadata.RotatedAtUtc),
            Format(keyMetadata.ModifiedAtUtc),
            keyMetadata.Id.ToString());

        if (rowsAffected == 0)
            throw new InvalidOperationException($"EncryptionKeyMetadata '{keyMetadata.Id}' was not found; cannot update a key that was never added.");
    }

    private static EncryptionKeyMetadata ToEntity(EncryptionKeyMetadataRow row)
    {
        var entity = EntityMaterializer.Create<EncryptionKeyMetadata>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(EncryptionKeyMetadata.Algorithm), (EncryptionAlgorithm)row.Algorithm);
        EntityMaterializer.Set(entity, nameof(EncryptionKeyMetadata.Purpose), (KeyPurpose)row.Purpose);
        EntityMaterializer.Set(entity, nameof(EncryptionKeyMetadata.IsActive), row.IsActive != 0);
        EntityMaterializer.Set(entity, nameof(EncryptionKeyMetadata.RotatedAtUtc), ParseNullable(row.RotatedAtUtc));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string? FormatNullable(DateTimeOffset? value) => value is null ? null : Format(value.Value);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static DateTimeOffset? ParseNullable(string? value) => value is null ? null : Parse(value);
}
