using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace SecureApp.Presentation.HomeAi;

/// <summary>Address (and optional API token) of the home LM Studio server. Never leaves the home network / the WireGuard tunnel.</summary>
public sealed record HomeAiEndpoint(string BaseUrl, string? Token);

/// <summary>One downloaded model on the home server, with whatever instances of it are currently loaded in memory.</summary>
public sealed record HomeAiModel(string Key, string DisplayName, string ParamsText, long SizeBytes, int MaxContext, bool IsVision, IReadOnlyList<string> LoadedInstanceIds)
{
    public bool IsLoaded => LoadedInstanceIds.Count > 0;

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

public sealed class HomeAiException(string message) : Exception(message);

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
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static Uri Url(HomeAiEndpoint endpoint, string path)
        => new(new Uri(endpoint.BaseUrl.Trim().TrimEnd('/') + "/"), path.TrimStart('/'));

    private static HttpRequestMessage Request(HttpMethod method, HomeAiEndpoint endpoint, string path, string? json = null)
    {
        var request = new HttpRequestMessage(method, Url(endpoint, path));
        if (!string.IsNullOrWhiteSpace(endpoint.Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token.Trim());
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
            using var request = Request(HttpMethod.Get, endpoint, "api/v1/models");
            response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HomeAiException("Server neodpovídá. Zkontrolujte, že je domácí PC zapnuté a přihlášené (tunel se spouští po přihlášení), že běží LM Studio a že jste doma nebo přes WireGuard.");
        }
        catch (HttpRequestException ex)
        {
            throw new HomeAiException($"Nelze se připojit k {endpoint.BaseUrl}: {ex.Message}");
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
                if (m.TryGetProperty("loaded_instances", out var instances) && instances.ValueKind == JsonValueKind.Array)
                {
                    foreach (var instance in instances.EnumerateArray())
                        if (GetString(instance, "id") is { Length: > 0 } id)
                            loaded.Add(id);
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
                    loaded));
            }

            return result.OrderByDescending(x => x.IsLoaded).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Loads a model into GPU/RAM; returns when it is ready (a big model can take a minute).</summary>
    public async Task LoadModelAsync(HomeAiEndpoint endpoint, string modelKey, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Post, endpoint, "api/v1/models/load", JsonSerializer.Serialize(new { model = modelKey }));
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response).ConfigureAwait(false);
    }

    /// <summary>Frees a loaded model instance from memory.</summary>
    public async Task UnloadModelAsync(HomeAiEndpoint endpoint, string instanceId, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Post, endpoint, "api/v1/models/unload", JsonSerializer.Serialize(new { instance_id = instanceId }));
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
        [EnumeratorCancellation] CancellationToken ct = default)
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

        using var request = Request(HttpMethod.Post, endpoint, "v1/chat/completions", JsonSerializer.Serialize(payload));
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
    public async Task<string> CompleteAsync(HomeAiEndpoint endpoint, string model, IReadOnlyList<HomeAiChatMessage> messages, double temperature, int? maxTokens, CancellationToken ct = default)
    {
        var text = new StringBuilder();
        await foreach (var piece in StreamChatAsync(endpoint, model, messages, temperature, maxTokens, ct).ConfigureAwait(false))
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
            throw new HomeAiException($"Spojení se serverem selhalo: {ex.Message}");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HomeAiException("Server vyžaduje API token (nebo je špatný). Zadejte ho níže.");

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new HomeAiException($"Server odpověděl {(int)response.StatusCode}: {ExtractError(body)}");
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
