using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IChatSessionRepository"/>
public sealed class ChatSessionRepository : IChatSessionRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public ChatSessionRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<ChatSession?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<ChatSessionRow>("SELECT * FROM chat_sessions WHERE id = ?", id.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task<IReadOnlyList<ChatSession>> GetAllAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<ChatSessionRow>("SELECT * FROM chat_sessions");
        return rows.Select(ToEntity).ToList();
    }

    public async Task<ChatSession?> GetByPeerPublicKeyAsync(byte[] peerIdentityPublicKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(peerIdentityPublicKey);
        var all = await GetAllAsync(ct);
        return all.FirstOrDefault(s => s.State != ChatSessionState.Closed && s.PeerIdentityPublicKey.AsSpan().SequenceEqual(peerIdentityPublicKey));
    }

    public async Task<IReadOnlyList<ChatSession>> GetAllByPeerPublicKeyAsync(byte[] peerIdentityPublicKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(peerIdentityPublicKey);
        var all = await GetAllAsync(ct);
        return all.Where(s => s.PeerIdentityPublicKey.AsSpan().SequenceEqual(peerIdentityPublicKey)).ToList();
    }

    public async Task AddAsync(ChatSession chatSession, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chatSession);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync(
            """
            INSERT INTO chat_sessions (
                id, peer_display_name, peer_identity_public_key, local_identity_key_id,
                state, last_ratcheted_at_utc, peer_relay_device_id, created_at_utc, modified_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            chatSession.Id.ToString(),
            chatSession.PeerDisplayName,
            chatSession.PeerIdentityPublicKey,
            chatSession.LocalIdentityKeyId.ToString(),
            (int)chatSession.State,
            chatSession.LastRatchetedAtUtc is null ? null : Format(chatSession.LastRatchetedAtUtc.Value),
            chatSession.PeerRelayDeviceId?.ToString(),
            Format(chatSession.CreatedAtUtc),
            Format(chatSession.ModifiedAtUtc));
    }

    public async Task UpdateAsync(ChatSession chatSession, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chatSession);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync(
            """
            UPDATE chat_sessions
            SET peer_display_name = ?, peer_identity_public_key = ?, local_identity_key_id = ?,
                state = ?, last_ratcheted_at_utc = ?, peer_relay_device_id = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            chatSession.PeerDisplayName,
            chatSession.PeerIdentityPublicKey,
            chatSession.LocalIdentityKeyId.ToString(),
            (int)chatSession.State,
            chatSession.LastRatchetedAtUtc is null ? null : Format(chatSession.LastRatchetedAtUtc.Value),
            chatSession.PeerRelayDeviceId?.ToString(),
            Format(chatSession.ModifiedAtUtc),
            chatSession.Id.ToString());

        if (rowsAffected == 0)
            throw new ChatSessionNotFoundException(chatSession.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rowsAffected = await connection.ExecuteAsync("DELETE FROM chat_sessions WHERE id = ?", id.ToString());
        if (rowsAffected == 0)
            throw new ChatSessionNotFoundException(id);
    }

    private static ChatSession ToEntity(ChatSessionRow row)
    {
        var entity = EntityMaterializer.Create<ChatSession>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(ChatSession.PeerDisplayName), row.PeerDisplayName);
        EntityMaterializer.Set(entity, nameof(ChatSession.PeerIdentityPublicKey), row.PeerIdentityPublicKey);
        EntityMaterializer.Set(entity, nameof(ChatSession.LocalIdentityKeyId), Guid.Parse(row.LocalIdentityKeyId));
        EntityMaterializer.Set(entity, nameof(ChatSession.State), (ChatSessionState)row.State);
        EntityMaterializer.Set(entity, nameof(ChatSession.LastRatchetedAtUtc), row.LastRatchetedAtUtc is null ? null : (DateTimeOffset?)Parse(row.LastRatchetedAtUtc));
        EntityMaterializer.Set(entity, nameof(ChatSession.PeerRelayDeviceId), row.PeerRelayDeviceId is null ? null : (Guid?)Guid.Parse(row.PeerRelayDeviceId));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
