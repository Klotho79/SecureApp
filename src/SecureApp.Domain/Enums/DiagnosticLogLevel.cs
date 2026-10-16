namespace SecureApp.Domain.Enums;

/// <summary>
/// Severity of one entry in the shared diagnostics log (2026-09-10) — see <see cref="Interfaces.Services.IDiagnosticsReporter"/>'s own remarks for why this exists.
/// Four-level scale (2026-10-16 log review): Info = ordinary event worth recording; Warning = something
/// went wrong but the app recovers by itself or the user was told what to do; Error = an unexpected
/// failure of a feature; Critical = the app cannot work (crash, startup failure, data loss risk) and the
/// admin should hear about it. Critical is appended last so existing stored values keep their meaning.
/// </summary>
public enum DiagnosticLogLevel
{
    Info,
    Warning,
    Error,
    Critical
}
