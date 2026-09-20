using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IContactRepository"/>
public sealed class ContactRepository : IContactRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public ContactRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<Contact>> GetAllOrderedAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<ContactRow>("SELECT * FROM contacts ORDER BY sort_order");
        return rows.Select(ToEntity).ToList();
    }

    public async Task<Contact?> GetByLinkedPublicKeyAsync(byte[] publicKey, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<ContactRow>("SELECT * FROM contacts WHERE linked_public_key = ?", publicKey);
        return row is null ? null : ToEntity(row);
    }

    public async Task AddAsync(Contact contact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO contacts (
                id, display_name, phone, email, note, sort_order,
                linked_public_key, linked_relay_device_id, created_at_utc, modified_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            contact.Id.ToString(),
            contact.DisplayName,
            contact.Phone,
            contact.Email,
            contact.Note,
            contact.SortOrder,
            contact.LinkedPublicKey,
            contact.LinkedRelayDeviceId?.ToString(),
            Format(contact.CreatedAtUtc),
            Format(contact.ModifiedAtUtc));
    }

    public async Task UpdateAsync(Contact contact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contact);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            UPDATE contacts
            SET display_name = ?, phone = ?, email = ?, note = ?, sort_order = ?,
                linked_public_key = ?, linked_relay_device_id = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            contact.DisplayName,
            contact.Phone,
            contact.Email,
            contact.Note,
            contact.SortOrder,
            contact.LinkedPublicKey,
            contact.LinkedRelayDeviceId?.ToString(),
            Format(contact.ModifiedAtUtc),
            contact.Id.ToString());
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM contacts WHERE id = ?", id.ToString());
    }

    private static Contact ToEntity(ContactRow row)
    {
        var entity = EntityMaterializer.Create<Contact>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(Contact.DisplayName), row.DisplayName);
        EntityMaterializer.Set(entity, nameof(Contact.Phone), row.Phone);
        EntityMaterializer.Set(entity, nameof(Contact.Email), row.Email);
        EntityMaterializer.Set(entity, nameof(Contact.Note), row.Note);
        EntityMaterializer.Set(entity, nameof(Contact.SortOrder), row.SortOrder);
        EntityMaterializer.Set(entity, nameof(Contact.LinkedPublicKey), row.LinkedPublicKey);
        EntityMaterializer.Set(entity, nameof(Contact.LinkedRelayDeviceId), row.LinkedRelayDeviceId is null ? null : (Guid?)Guid.Parse(row.LinkedRelayDeviceId));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
