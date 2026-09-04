using System.Globalization;
using SecureApp.Data.Persistence;
using SecureApp.Data.Persistence.Rows;
using SecureApp.Domain.Common;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Data.Repositories;

/// <inheritdoc cref="ITransportSettingsRepository"/>
/// <remarks>The <c>transport_settings</c> table only ever holds a single row — same "delete then insert" shape as <c>UserRepository</c>.</remarks>
public sealed class TransportSettingsRepository : ITransportSettingsRepository
{
    private readonly ISecureDatabaseConnectionFactory _connectionFactory;

    public TransportSettingsRepository(ISecureDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<TransportEndpointConfiguration?> GetAsync(CancellationToken ct = default)
    {
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        var rows = await connection.QueryAsync<TransportSettingsRow>("SELECT * FROM transport_settings LIMIT 1");
        return rows.Count == 0 ? null : ToEntity(rows[0]);
    }

    public async Task SaveAsync(TransportEndpointConfiguration configuration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connection = await _connectionFactory.GetConnectionAsync(ct);
        await connection.ExecuteAsync("DELETE FROM transport_settings");
        await connection.ExecuteAsync(
            "INSERT INTO transport_settings (id, endpoint_uri, is_auto_connect_enabled, last_connected_at_utc, assigned_relay_device_id, created_at_utc, modified_at_utc) VALUES (?, ?, ?, ?, ?, ?, ?)",
            configuration.Id.ToString(),
            configuration.EndpointUri?.ToString(),
            configuration.IsAutoConnectEnabled ? 1 : 0,
            configuration.LastConnectedAtUtc is null ? null : Format(configuration.LastConnectedAtUtc.Value),
            configuration.AssignedDeviceId?.ToString(),
            Format(configuration.CreatedAtUtc),
            Format(configuration.ModifiedAtUtc));
    }

    private static TransportEndpointConfiguration ToEntity(TransportSettingsRow row)
    {
        var entity = EntityMaterializer.Create<TransportEndpointConfiguration>();
        EntityMaterializer.Set(entity, nameof(Entity.Id), Guid.Parse(row.Id));
        EntityMaterializer.Set(entity, nameof(Entity.CreatedAtUtc), Parse(row.CreatedAtUtc));
        EntityMaterializer.Set(entity, nameof(Entity.ModifiedAtUtc), Parse(row.ModifiedAtUtc));
        EntityMaterializer.Set(entity, nameof(TransportEndpointConfiguration.EndpointUri), row.EndpointUri is null ? null : new Uri(row.EndpointUri));
        EntityMaterializer.Set(entity, nameof(TransportEndpointConfiguration.IsAutoConnectEnabled), row.IsAutoConnectEnabled != 0);
        EntityMaterializer.Set(entity, nameof(TransportEndpointConfiguration.LastConnectedAtUtc), row.LastConnectedAtUtc is null ? null : (DateTimeOffset?)Parse(row.LastConnectedAtUtc));
        EntityMaterializer.Set(entity, nameof(TransportEndpointConfiguration.AssignedDeviceId), row.AssignedRelayDeviceId is null ? null : (Guid?)Guid.Parse(row.AssignedRelayDeviceId));
        return entity;
    }

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
