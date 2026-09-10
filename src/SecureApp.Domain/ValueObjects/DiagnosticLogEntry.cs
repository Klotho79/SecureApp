using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One entry read back from the relay's shared diagnostics log (2026-09-10) — see
/// <see cref="Interfaces.Services.IDiagnosticsReporter"/>'s own remarks. <see cref="DeviceDisplayName"/>
/// is resolved server-side against the relay's live member directory at READ time (same
/// "resolve against the live directory, don't trust a stale cached name" pattern
/// <c>DirectoryNameResolver</c> already established for chat) — falls back to a placeholder if the
/// reporting device was never published there (e.g. reported before ever connecting).
/// </summary>
public sealed record DiagnosticLogEntry(
    Guid Id,
    string DeviceDisplayName,
    DiagnosticLogLevel Level,
    string Message,
    string? Context,
    string? ExceptionDetails,
    DateTimeOffset CreatedAtUtc);
