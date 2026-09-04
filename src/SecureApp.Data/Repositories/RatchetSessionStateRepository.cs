using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="IRatchetSessionStateRepository"/>
public sealed class RatchetSessionStateRepository : IRatchetSessionStateRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public RatchetSessionStateRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<RatchetSessionState?> GetBySessionIdAsync(Guid chatSessionId, CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var row = await connection.FindWithQueryAsync<RatchetSessionStateRow>("SELECT * FROM ratchet_session_states WHERE id = ?", chatSessionId.ToString());
        return row is null ? null : ToEntity(row);
    }

    public async Task UpsertAsync(RatchetSessionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var connection = await _connectionFactory.GetConnectionAsync(ct);

        var rowsAffected = await connection.ExecuteAsync(
            """
            UPDATE ratchet_session_states
            SET send_message_number = ?, receive_message_number = ?, previous_send_chain_length = ?,
                current_send_chain_public_key = ?, remote_ratchet_public_key = ?, modified_at_utc = ?
            WHERE id = ?
            """,
            state.SendMessageNumber,
            state.ReceiveMessageNumber,
            state.PreviousSendChainLength,
            state.CurrentSendChainPublicKey,
            state.RemoteRatchetPublicKey,
            Format(state.ModifiedAtUtc),
            state.Id.ToString());

        if (rowsAffected > 0)
            return;

        await connection.ExecuteAsync(
            """
            INSERT INTO ratchet_session_states (
                id, send_message_number, receive_message_number, previous_send_chain_length,
                current_send_chain_public_key, remote_ratchet_public_key, created_at_utc, modified_at_utc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            state.Id.ToString(),
            state.SendMessageNumber,
            state.ReceiveMessageNumber,
            state.PreviousSendChainLength,
            state.CurrentSendChainPublicKey,
            state.RemoteRatchetPublicKey,
            Format(state.CreatedAtUtc),
            Format(state.ModifiedAtUtc));
    }

    private static RatchetSessionState ToEntity(RatchetSessionStateRow row)
    {
        var entity = EntityMaterializer.Create<RatchetSessionState>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(RatchetSessionState.SendMessageNumber), row.SendMessageNumber);
        EntityMaterializer.Set(entity, nameof(RatchetSessionState.ReceiveMessageNumber), row.ReceiveMessageNumber);
        EntityMaterializer.Set(entity, nameof(RatchetSessionState.PreviousSendChainLength), row.PreviousSendChainLength);
        EntityMaterializer.Set(entity, nameof(RatchetSessionState.CurrentSendChainPublicKey), row.CurrentSendChainPublicKey);
        EntityMaterializer.Set(entity, nameof(RatchetSessionState.RemoteRatchetPublicKey), row.RemoteRatchetPublicKey);
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
