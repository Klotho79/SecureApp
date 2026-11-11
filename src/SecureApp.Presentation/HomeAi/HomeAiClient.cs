using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.HomeAi;

/// <summary>
/// Settings of one call to the home LM Studio (reached through the relay, see <see cref="HomeAiClient"/>).
/// <paramref name="TtlSeconds"/>: when set, every chat request carries LM Studio's idle <c>ttl</c>, so a model that the
/// chat itself had to load is unloaded again after that many idle seconds (a model loaded by hand stays until switched off).
/// </summary>
public sealed record HomeAiEndpoint(int? TtlSeconds = null);

/// <summary>One downloaded model on the home server, with whatever instances of it are currently loaded in memory (<paramref name="LoadedContext"/> = context length of the loaded instance, tokens).</summary>
public sealed record HomeAiModel(string Key, string DisplayName, string ParamsText, long SizeBytes, int MaxContext, bool IsVision, IReadOnlyList<string> LoadedInstanceIds, int LoadedContext = 0)
{
    public bool IsLoaded => LoadedInstanceIds.Count > 0;

    /// <summary>The context (tokens) a prompt can really use: the loaded instance's, else a conservative guess for a model that will be loaded on demand.</summary>
    public int UsableContext => LoadedContext > 0 ? LoadedContext : Math.Clamp(MaxContext, 4096, 8192);

    public string Label
    {
        get
        {
            var size = SizeBytes > 0 ? $" · {SizeBytes / (1024.0 * 1024 * 1024):F1} GB" : string.Empty;
            var parameters = string.IsNullOrEmpty(ParamsText) ? string.Empty : $" · {ParamsText}";
            return $"{(IsLoaded ? "● " : "○ ")}{DisplayName}{parameters}{size}";
        }
    }
}

public sealed record HomeAiChatMessage(string Role, string Content);

public sealed class HomeAiException(string message, int? statusCode = null) : Exception(message)
{
    /// <summary>The HTTP status the server answered with, when the failure was an error response.</summary>
    public int? StatusCode => statusCode;
}

/// <summary>
/// Talks to LM Studio (0.4+) on the user's own PC — chosen over Ollama because that is where the larger models live
/// and because it has explicit model load/unload calls (2026-11-10, user's ask: a private assistant for private
/// problems with no risk of leaking anything). Native REST for the model list and load/unload
/// (<c>/api/v1/models</c>), OpenAI-compatible streaming for the chat (<c>/v1/chat/completions</c>). Plain HTTP on
/// purpose: the only way to reach the server is the home Wi-Fi or the WireGuard tunnel, and no prompt is ever sent
/// to any third party. Nothing here logs prompts or answers.
/// </summary>
public sealed class HomeAiClient
{
    private readonly HttpClient _http = SecureApp.Presentation.Transport.RelayHttp.CreateClient(Timeout.InfiniteTimeSpan);
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly ISecureVaultKeyStore _vault;

    public HomeAiClient(ITransportSettingsRepository transportSettingsRepository, ISecureVaultKeyStore vault)
    {
        _transportSettingsRepository = transportSettingsRepository;
        _vault = vault;
    }

    /// <summary>A request to the relay's /homeai/ proxy: the relay's own address (pinned HTTPS from anywhere, LAN/tunnel at home) plus this device's login.</summary>
    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string path, string? json, CancellationToken ct)
    {
        HttpRequestMessage request;
        try
        {
            request = await SecureApp.Presentation.Transport.RelayHttp.CreateAuthorizedRequestAsync(method, "homeai/" + path.TrimStart('/'), _transportSettingsRepository, _vault, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new HomeAiException(ex.Message); // relay address / device login not set up
        }

        if (json is not null)
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    public async Task<IReadOnlyList<HomeAiModel>> ListModelsAsync(HomeAiEndpoint endpoint, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        HttpResponseMessage response;
        try
        {
            using var request = await RequestAsync(HttpMethod.Get, "api/v1/models", null, timeout.Token).ConfigureAwait(false);
            response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HomeAiException("Relay neodpovídá. Zkontrolujte připojení telefonu k internetu.");
        }
        catch (HttpRequestException ex)
        {
            throw new HomeAiException($"Nelze se připojit k relay: {RelayHttpMessage(ex)}");
        }

        using (response)
        {
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var result = new List<HomeAiModel>();
            if (!doc.RootElement.TryGetProperty("models", out var models))
                return result;

            foreach (var m in models.EnumerateArray())
            {
                if (GetString(m, "type") is { } type && type != "llm")
                    continue; // embeddings and the like cannot chat

                var key = GetString(m, "key");
                if (string.IsNullOrEmpty(key))
                    continue;

                var loaded = new List<string>();
                var loadedContext = 0;
                if (m.TryGetProperty("loaded_instances", out var instances) && instances.ValueKind == JsonValueKind.Array)
                {
                    foreach (var instance in instances.EnumerateArray())
                    {
                        if (GetString(instance, "id") is { Length: > 0 } id)
                            loaded.Add(id);
                        if (loadedContext == 0 && instance.ValueKind == JsonValueKind.Object && instance.TryGetProperty("config", out var config)
                            && config.ValueKind == JsonValueKind.Object && config.TryGetProperty("context_length", out var cl) && cl.TryGetInt32(out var contextLength))
                            loadedContext = contextLength;
                    }
                }

                var vision = m.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
                    && caps.TryGetProperty("vision", out var v) && v.ValueKind == JsonValueKind.True;

                result.Add(new HomeAiModel(
                    key,
                    GetString(m, "display_name") ?? key,
                    GetString(m, "params_string") ?? string.Empty,
                    m.TryGetProperty("size_bytes", out var size) && size.TryGetInt64(out var sizeBytes) ? sizeBytes : 0,
                    m.TryGetProperty("max_context_length", out var ctx) && ctx.TryGetInt32(out var maxContext) ? maxContext : 0,
                    vision,
                    loaded,
                    loadedContext));
            }

            return result.OrderByDescending(x => x.IsLoaded).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Loads a model into GPU/RAM; returns when it is ready (a big model can take a minute).</summary>
    public async Task LoadModelAsync(HomeAiEndpoint endpoint, string modelKey, CancellationToken ct = default)
    {
        using var request = await RequestAsync(HttpMethod.Post, "api/v1/models/load", JsonSerializer.Serialize(new { model = modelKey }), ct).ConfigureAwait(false);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    /// <summary>Frees a loaded model instance from memory.</summary>
    public async Task UnloadModelAsync(HomeAiEndpoint endpoint, string instanceId, CancellationToken ct = default)
    {
        using var request = await RequestAsync(HttpMethod.Post, "api/v1/models/unload", JsonSerializer.Serialize(new { instance_id = instanceId }), ct).ConfigureAwait(false);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    /// <summary>Streams the answer text piece by piece (cancelling the token stops the generation on the server).</summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        HomeAiEndpoint endpoint,
        string model,
        IReadOnlyList<HomeAiChatMessage> messages,
        double temperature,
        int? maxTokens,
        [EnumeratorCancellation] CancellationToken ct = default,
        object? responseFormat = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["stream"] = true,
            ["temperature"] = temperature,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
        };
        if (maxTokens is { } limit)
            payload["max_tokens"] = limit;
        if (responseFormat is not null)
            payload["response_format"] = responseFormat; // LM Studio constrains the output to this JSON schema (grammar), so it cannot be malformed
        if (endpoint.TtlSeconds is { } ttl and > 0)
            payload["ttl"] = ttl;

        using var request = await RequestAsync(HttpMethod.Post, "v1/chat/completions", JsonSerializer.Serialize(payload), ct).ConfigureAwait(false);
        using var response = await SendAsync(request, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line[5..].Trim();
            if (data == "[DONE]")
                yield break;
            if (data.Length == 0)
                continue;

            string? piece = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("delta", out var delta))
                {
                    piece = GetString(delta, "content");
                }
            }
            catch (JsonException)
            {
                // A partial/garbled line: skip it, the stream goes on.
            }

            if (!string.IsNullOrEmpty(piece))
                yield return piece;
        }
    }

    /// <summary>The whole answer at once (used for the short planning step).</summary>
    public async Task<string> CompleteAsync(HomeAiEndpoint endpoint, string model, IReadOnlyList<HomeAiChatMessage> messages, double temperature, int? maxTokens, CancellationToken ct = default, object? responseFormat = null)
    {
        var text = new StringBuilder();
        await foreach (var piece in StreamChatAsync(endpoint, model, messages, temperature, maxTokens, ct, responseFormat).ConfigureAwait(false))
            text.Append(piece);
        return text.ToString();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
    {
        try
        {
            return await _http.SendAsync(request, option, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new HomeAiException($"Spojení s relay selhalo: {RelayHttpMessage(ex)}");
        }
    }

    private static string RelayHttpMessage(HttpRequestException ex) => SecureApp.Presentation.Transport.RelayHttp.DescribeException(ex);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new HomeAiException("Relay vás nepřihlásilo (zařízení není zaregistrované).");

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HomeAiException($"Server odpověděl {(int)response.StatusCode}: {ExtractError(body)}", (int)response.StatusCode);
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString() ?? body;
                if (GetString(error, "message") is { } message)
                    return message;
            }
        }
        catch (JsonException)
        {
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
