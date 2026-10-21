namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Outcome of <see cref="Interfaces.Services.IOpicentrumSyncService.MoveRosterPersonAsync"/> — an
/// explicit status enum rather than a sentinel record instance, same reasoning as
/// <see cref="OpicentrumNotePushStatus"/>'s own remarks.
/// </summary>
public enum OpicentrumMoveStatus
{
    Success,
    LoginFailed,
    NotConfirmed
}

public sealed record OpicentrumMoveResult(OpicentrumMoveStatus Status, string? ErrorMessage);
