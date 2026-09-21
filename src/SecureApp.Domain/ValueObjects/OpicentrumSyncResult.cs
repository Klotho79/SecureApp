namespace SecureApp.Domain.ValueObjects;

/// <summary>Outcome of one <see cref="Interfaces.Services.IOpicentrumSyncService"/> sync pass.</summary>
public sealed record OpicentrumSyncResult(bool Success, int CreatedCount, int UpdatedCount, string? ErrorMessage)
{
    public static OpicentrumSyncResult NotConfigured { get; } = new(true, 0, 0, null);
}
