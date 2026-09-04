using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>transport_settings</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("transport_settings")]
internal sealed class TransportSettingsRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("endpoint_uri")] public string? EndpointUri { get; set; }
    [Column("is_auto_connect_enabled")] public int IsAutoConnectEnabled { get; set; }
    [Column("last_connected_at_utc")] public string? LastConnectedAtUtc { get; set; }
    [Column("assigned_relay_device_id")] public string? AssignedRelayDeviceId { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
