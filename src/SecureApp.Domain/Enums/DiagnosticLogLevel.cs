namespace SecureApp.Domain.Enums;

/// <summary>Severity of one entry in the shared diagnostics log (2026-09-10) — see <see cref="Interfaces.Services.IDiagnosticsReporter"/>'s own remarks for why this exists.</summary>
public enum DiagnosticLogLevel
{
    Info,
    Warning,
    Error
}
