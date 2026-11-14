using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Diagnostics;

/// <inheritdoc cref="IDiagnosticsReporter"/>
/// <remarks>
/// Endpoint, device auth and sending come from <see cref="RelayServiceBase"/>.
///
/// <see cref="ReportAsync"/> swallows every failure of its own (no relay configured yet, not
/// connected, network error, anything) — it is called from catch blocks and a global
/// unhandled-exception hook, the exact places that can least afford a reporting call to introduce a
/// NEW exception. <see cref="GetRecentAsync"/> is the opposite: it's called from an explicit
/// "show me the log" user action, so it lets a real failure surface normally.
/// </remarks>
public sealed class HttpDiagnosticsReporter(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    : RelayServiceBase(vault, transportSettingsRepository), IDiagnosticsReporter
{
    public Task ReportAsync(DiagnosticLogLevel level, string message, string? context = null, Exception? exception = null, CancellationToken ct = default)
        // Best-effort: a failed report (401 before this device has registered, network error, relay down) must never
        // surface as a NEW exception from what is itself already error-handling code.
        => TrySendAsync(HttpMethod.Post, "diagnostics/logs",
            new { Level = level.ToString(), Message = message, Context = context, ExceptionDetails = exception?.ToString() }, ct);

    public async Task<IReadOnlyList<DiagnosticLogEntry>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        var dtos = await GetJsonAsync<List<DiagnosticLogEntryDto>>($"diagnostics/logs?limit={limit}", ct) ?? [];
        return dtos.Select(d => new DiagnosticLogEntry(
            d.Id, d.DeviceDisplayName, Enum.Parse<DiagnosticLogLevel>(d.Level), d.Message, d.Context, d.ExceptionDetails, d.CreatedAtUtc)).ToList();
    }

    private sealed record DiagnosticLogEntryDto(Guid Id, string DeviceDisplayName, string Level, string Message, string? Context, string? ExceptionDetails, DateTimeOffset CreatedAtUtc);
}
