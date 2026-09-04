using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IDocumentFolderRepository"/>
public sealed class DocumentFolderRepository : IDocumentFolderRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public DocumentFolderRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<DocumentFolder?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<DocumentFolderRow>("SELECT * FROM document_folders WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<DocumentFolder>> GetChildrenAsync(Guid? parentFolderId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = parentFolderId is null
            ? await connection.QueryAsync<DocumentFolderRow>("SELECT * FROM document_folders WHERE parent_folder_id IS NULL")
            : await connection.QueryAsync<DocumentFolderRow>("SELECT * FROM document_folders WHERE parent_folder_id = ?", parentFolderId.Value.ToString());
        return rows.Select(ToEntity).ToList();
    }

    public async Task AddAsync(DocumentFolder folder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            "INSERT INTO document_folders (id, name, parent_folder_id, created_at_utc, modified_at_utc) VALUES (?, ?, ?, ?, ?)",
            folder.Id.ToString(),
            folder.Name,
            folder.ParentFolderId?.ToString(),
            Format(folder.CreatedAtUtc),
            Format(folder.ModifiedAtUtc));
    }

    public async Task UpdateAsync(DocumentFolder folder, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync(
            "UPDATE document_folders SET name = ?, parent_folder_id = ?, modified_at_utc = ? WHERE id = ?",
            folder.Name,
            folder.ParentFolderId?.ToString(),
            Format(folder.ModifiedAtUtc),
            folder.Id.ToString());

        if (rowsAffected == 0)
            throw new InvalidOperationException($"DocumentFolder '{folder.Id}' was not found; cannot update a folder that was never added.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        // Cascades to subfolders (document_folders.parent_folder_id is ON DELETE CASCADE) and
        // clears folder_id on any documents directly inside it (documents.folder_id is ON DELETE SET NULL) —
        // see SqlCipherConnectionFactory's schema comments.
        var rowsAffected = await connection.ExecuteAsync("DELETE FROM document_folders WHERE id = ?", id.ToString());
        if (rowsAffected == 0)
            throw new InvalidOperationException($"DocumentFolder '{id}' was not found; cannot delete a folder that was never added.");
    }

    private static DocumentFolder ToEntity(DocumentFolderRow row)
    {
        var entity = EntityMaterializer.Create<DocumentFolder>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(DocumentFolder.Name), row.Name);
        EntityMaterializer.Set(entity, nameof(DocumentFolder.ParentFolderId), row.ParentFolderId is null ? null : Guid.Parse(row.ParentFolderId));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
