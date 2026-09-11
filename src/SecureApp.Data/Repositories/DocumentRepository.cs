using System.Globalization;
using System.Text.Json;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IDocumentRepository"/>
public sealed class DocumentRepository : IDocumentRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public DocumentRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<DocumentRow>("SELECT * FROM documents WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<Document?> GetBySourceLibraryFileIdAsync(Guid libraryFileId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<DocumentRow>("SELECT * FROM documents WHERE source_library_file_id = ?", libraryFileId.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<Document>> GetByFolderAsync(Guid? folderId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = folderId is null
            ? await connection.QueryAsync<DocumentRow>("SELECT * FROM documents WHERE folder_id IS NULL")
            : await connection.QueryAsync<DocumentRow>("SELECT * FROM documents WHERE folder_id = ?", folderId.Value.ToString());
        return rows.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<DocumentRow>("SELECT * FROM documents");
        return rows.Select(ToEntity).ToList();
    }

    public async Task AddAsync(Document document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO documents (
                id, title, file_name, document_type, original_size_bytes,
                content_hash_algorithm, content_hash_hex,
                encryption_key_id, encryption_algorithm, cipher_text, nonce, auth_tag,
                folder_id, source_library_file_id, is_favorite, tags, created_at_utc, modified_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            document.Id.ToString(),
            document.Title,
            document.FileName,
            (int)document.DocumentType,
            document.OriginalSizeBytes,
            (int)document.ContentHash.Algorithm,
            document.ContentHash.HexValue,
            document.EncryptedContent.KeyId.ToString(),
            (int)document.EncryptedContent.Algorithm,
            document.EncryptedContent.CipherText,
            document.EncryptedContent.Nonce,
            document.EncryptedContent.AuthTag,
            document.FolderId?.ToString(),
            document.SourceLibraryFileId?.ToString(),
            document.IsFavorite ? 1 : 0,
            JsonSerializer.Serialize(document.Tags),
            Format(document.CreatedAtUtc),
            Format(document.ModifiedAtUtc));
    }

    public async Task UpdateAsync(Document document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync(
            """
            UPDATE documents
            SET title = ?, file_name = ?, document_type = ?, original_size_bytes = ?,
                content_hash_algorithm = ?, content_hash_hex = ?,
                encryption_key_id = ?, encryption_algorithm = ?, cipher_text = ?, nonce = ?, auth_tag = ?,
                folder_id = ?, is_favorite = ?, tags = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            document.Title,
            document.FileName,
            (int)document.DocumentType,
            document.OriginalSizeBytes,
            (int)document.ContentHash.Algorithm,
            document.ContentHash.HexValue,
            document.EncryptedContent.KeyId.ToString(),
            (int)document.EncryptedContent.Algorithm,
            document.EncryptedContent.CipherText,
            document.EncryptedContent.Nonce,
            document.EncryptedContent.AuthTag,
            document.FolderId?.ToString(),
            document.IsFavorite ? 1 : 0,
            JsonSerializer.Serialize(document.Tags),
            Format(document.ModifiedAtUtc),
            document.Id.ToString());

        if (rowsAffected == 0)
            throw new DocumentNotFoundException(document.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync("DELETE FROM documents WHERE id = ?", id.ToString());
        if (rowsAffected == 0)
            throw new DocumentNotFoundException(id);
    }

    private static Document ToEntity(DocumentRow row)
    {
        var entity = EntityMaterializer.Create<Document>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(Document.Title), row.Title);
        EntityMaterializer.Set(entity, nameof(Document.FileName), row.FileName);
        EntityMaterializer.Set(entity, nameof(Document.DocumentType), (DocumentType)row.DocumentType);
        EntityMaterializer.Set(entity, nameof(Document.OriginalSizeBytes), row.OriginalSizeBytes);
        EntityMaterializer.Set(entity, nameof(Document.ContentHash), new FileHash((HashAlgorithmKind)row.ContentHashAlgorithm, row.ContentHashHex));
        EntityMaterializer.Set(entity, nameof(Document.EncryptedContent), new EncryptedPayload(
            Guid.Parse(row.EncryptionKeyId), (EncryptionAlgorithm)row.EncryptionAlgorithm, row.CipherText, row.Nonce, row.AuthTag));
        EntityMaterializer.Set(entity, nameof(Document.FolderId), row.FolderId is null ? null : Guid.Parse(row.FolderId));
        EntityMaterializer.Set(entity, nameof(Document.SourceLibraryFileId), row.SourceLibraryFileId is null ? null : (Guid?)Guid.Parse(row.SourceLibraryFileId));
        EntityMaterializer.Set(entity, nameof(Document.IsFavorite), row.IsFavorite != 0);
        EntityMaterializer.Set(entity, nameof(Document.Tags), (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(row.Tags) ?? []));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
