namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// Outcome of <see cref="Interfaces.Services.IOpicentrumSyncService.PushNoteAsync"/> — an explicit
/// status enum rather than a sentinel record instance on purpose (2026-09-22): a same-shaped-record
/// sentinel is exactly the bug already caught and fixed once in <c>OpicentrumSyncResult.NotConfigured</c>
/// (record value-equality made it indistinguishable from a genuine "ran fine, nothing to report"
/// result) — not repeating that mistake here.
/// </summary>
public enum OpicentrumNotePushStatus
{
    Success,
    NotConfigured,
    DayNotEditable,
    Failed
}

public sealed record OpicentrumNotePushResult(OpicentrumNotePushStatus Status, string? ErrorMessage);
