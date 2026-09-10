using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Transport;

namespace SecureApp.Presentation.Diagnostics;

/// <inheritdoc cref="IDiagnosticsReporter"/>
/// <remarks>
/// Lives in Presentation (not Data), same reasoning as <see cref="Library.HttpContactDirectoryService"/>
/// — needs <c>HttpClient</c> + relay endpoint/device-identity configuration. The device-auth header
/// helper and ws-&gt;http rewrite are duplicated from that class rather than shared, same
/// "duplicated, stays independently readable" call this codebase already made more than once.
///
/// <see cref="ReportAsync"/> swallows every failure of its own (no relay configured yet, not
/// connected, network error, anything) — it is called from catch blocks and a global
/// unhandled-exception hook, the exact places that can least afford a reporting call to introduce a
/// NEW exception. <see cref="GetRecentAsync"/> is the opposite: it's called from an explicit
/// "show me the log" user action, so it lets a real failure surface normally.
/// </remarks>
public sealed class HttpDiagnosticsReporter : IDiagnosticsReporter
{
    private static readonly JsonSerializerOptions HttpJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISecureVaultKeyStore _vault;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly HttpClient _httpClient = new();

    public HttpDiagnosticsReporter(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
    }

    public async Task ReportAsync(DiagnosticLogLevel level, string message, string? context = null, Exception? exception = null, CancellationToken ct = default)
    {
        try
        {
            var endpoint = await GetHttpEndpointAsync(ct);
            var uri = new Uri(endpoint, "diagnostics/logs");

            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(
                    new { Level = level.ToString(), Message = message, Context = context, ExceptionDetails = exception?.ToString() },
                    options: HttpJsonOptions)
            };
            await AddDeviceAuthAsync(request, ct);
            using var response = await _httpClient.SendAsync(request, ct);
            // Deliberately no EnsureSuccessStatusCode() — a failed report (401 before this device
            // has registered, network error, relay down) must never surface as a NEW exception from
            // what is itself already error-handling code.
        }
        catch
        {
            // Best-effort by design — see this class's own remarks above.
        }
    }

    public async Task<IReadOnlyList<DiagnosticLogEntry>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        var endpoint = await GetHttpEndpointAsync(ct);
        var uri = new Uri(endpoint, $"diagnostics/logs?limit={limit}");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        await AddDeviceAuthAsync(request, ct);
        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<DiagnosticLogEntryDto>>(HttpJsonOptions, ct) ?? [];
        return dtos.Select(d => new DiagnosticLogEntry(
            d.Id, d.DeviceDisplayName, Enum.Parse<DiagnosticLogLevel>(d.Level), d.Message, d.Context, d.ExceptionDetails, d.CreatedAtUtc)).ToList();
    }

    private async Task<Uri> GetHttpEndpointAsync(CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var wsEndpoint = configuration?.EndpointUri
            ?? throw new InvalidOperationException("Zatím není nastavená adresa relay serveru — nastavte ji nejprve v Nastavení.");

        var scheme = wsEndpoint.Scheme switch
        {
            "ws" => "http",
            "wss" => "https",
            _ => wsEndpoint.Scheme
        };
        return new UriBuilder(wsEndpoint) { Scheme = scheme, Port = wsEndpoint.Port }.Uri;
    }

    private async Task AddDeviceAuthAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var configuration = await _transportSettingsRepository.GetAsync(ct);
        var deviceId = configuration?.AssignedDeviceId
            ?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
        var secretBytes = await _vault.RetrieveSecretAsync(RelayDeviceVaultKeys.DeviceSecret, ct)
            ?? throw new InvalidOperationException("V úložišti chybí tajný klíč zařízení pro relay.");

        request.Headers.Add("X-Device-Id", deviceId.ToString());
        request.Headers.Add("X-Device-Secret", Encoding.UTF8.GetString(secretBytes));
    }

    private sealed record DiagnosticLogEntryDto(Guid Id, string DeviceDisplayName, string Level, string Message, string? Context, string? ExceptionDetails, DateTimeOffset CreatedAtUtc);
}
