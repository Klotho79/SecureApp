using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureApp.Presentation.Translation;

/// <summary>One downloaded model found on a local AI server — picking it sets BOTH the server address and the model name, so the admin never has to know which server (Ollama/LM Studio) or what exact id it has.</summary>
public sealed record LocalAiModelOption(string ServerName, string BaseUrl, string ModelId, string? SizeText, bool IsRecommended, bool IsTooBigForOneGpu)
{
    public string Label => $"{(IsRecommended ? "⭐ " : "")}{ModelId}  ·  {ServerName}{(SizeText is null ? "" : $"  ·  {SizeText}")}"
        + (IsRecommended ? "  (doporučeno)" : IsTooBigForOneGpu ? "  (velký – nevejde se na 1 kartu, pomalý)" : "");
}

/// <summary>
/// Finds every model already downloaded on this PC's local AI servers (2026-10-09, user's own ask:
/// "nevím jestli použít Ollamu nebo LM Studio … nevím který je na překlad vhodný a už vůbec jak se
/// jmenuje"). Probes both servers' default ports plus whatever address is currently configured, in
/// parallel, short timeout each — a server that isn't running just contributes nothing.
///
/// Uses each server's NATIVE listing where it has one (Ollama <c>/api/tags</c>, LM Studio
/// <c>/api/v0/models</c>) because only those say which entries are embedding models (useless for
/// translation); plain OpenAI <c>/v1/models</c> is the fallback for anything else. No QuestPDF/PdfPig
/// here, so unlike <see cref="LocalAiLibraryTranslationService"/> this needs no <c>#if WINDOWS</c>.
/// </summary>
public static class LocalAiModelDiscovery
{
    public const string OllamaBaseUrl = "http://localhost:11434";
    public const string LmStudioBaseUrl = "http://localhost:1234";

    /// <summary>~Q4 weights + context still fit one 16 GB card (the admin's RTX 5060 Ti) up to about this size.</summary>
    private const double MaxSingleGpuBillions = 20;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly HttpClient HttpClient = new() { Timeout = ProbeTimeout };

    public static async Task<IReadOnlyList<LocalAiModelOption>> DiscoverAsync(string? configuredBaseUrl, CancellationToken ct = default)
    {
        var probes = new List<Task<List<Candidate>>>
        {
            ProbeOllamaAsync(ct),
            ProbeLmStudioAsync(ct),
        };
        var configured = configuredBaseUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured)
            && !string.Equals(configured, OllamaBaseUrl, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(configured, LmStudioBaseUrl, StringComparison.OrdinalIgnoreCase))
            probes.Add(ProbeOpenAiCompatibleAsync("Vlastní server", configured, ct));

        var candidates = (await Task.WhenAll(probes)).SelectMany(c => c).ToList();
        var best = candidates.OrderByDescending(c => c.Score).FirstOrDefault();

        return candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.ModelId, StringComparer.OrdinalIgnoreCase)
            .Select(c => new LocalAiModelOption(c.ServerName, c.BaseUrl, c.ModelId, c.SizeText, ReferenceEquals(c, best),
                (ParseBillions(c.SizeText) ?? ParseBillions(c.ModelId)) > MaxSingleGpuBillions))
            .ToList();
    }

    private sealed record Candidate(string ServerName, string BaseUrl, string ModelId, string? SizeText, int Score);

    private static async Task<List<Candidate>> ProbeOllamaAsync(CancellationToken ct)
    {
        using var doc = await TryGetJsonAsync($"{OllamaBaseUrl}/api/tags", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("models", out var models)) return [];

        var result = new List<Candidate>();
        foreach (var model in models.EnumerateArray())
        {
            var id = GetString(model, "name");
            if (id is null) continue;
            // Newer Ollama lists capabilities; an embedding-only model has no "completion".
            if (model.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
                && !caps.EnumerateArray().Any(c => c.GetString() == "completion"))
                continue;
            if (IsEmbeddingId(id)) continue;
            var size = model.TryGetProperty("details", out var details) ? GetString(details, "parameter_size") : null;
            result.Add(new Candidate("Ollama", OllamaBaseUrl, id, size, Score(id, size)));
        }
        return result;
    }

    private static async Task<List<Candidate>> ProbeLmStudioAsync(CancellationToken ct)
    {
        using var doc = await TryGetJsonAsync($"{LmStudioBaseUrl}/api/v0/models", ct);
        if (doc is null) return await ProbeOpenAiCompatibleAsync("LM Studio", LmStudioBaseUrl, ct);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return [];

        var result = new List<Candidate>();
        foreach (var model in data.EnumerateArray())
        {
            var id = GetString(model, "id");
            if (id is null) continue;
            var type = GetString(model, "type");
            if (type is not null && type != "llm" && type != "vlm") continue; // "embeddings"
            if (IsEmbeddingId(id)) continue;
            result.Add(new Candidate("LM Studio", LmStudioBaseUrl, id, null, Score(id, null)));
        }
        return result;
    }

    private static async Task<List<Candidate>> ProbeOpenAiCompatibleAsync(string serverName, string baseUrl, CancellationToken ct)
    {
        using var doc = await TryGetJsonAsync($"{baseUrl}/v1/models", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data)) return [];

        return data.EnumerateArray()
            .Select(m => GetString(m, "id"))
            .Where(id => id is not null && !IsEmbeddingId(id))
            .Select(id => new Candidate(serverName, baseUrl, id!, null, Score(id!, null)))
            .ToList();
    }

    /// <summary>
    /// Rough "how good is this for translating medical text into Czech" ranking — only used to put a
    /// ⭐ on one entry; the admin can pick anything. Based on a real side-by-side test on the admin's
    /// own PC (2026-10-09, an RSI paragraph into čeština):
    /// <list type="bullet">
    /// <item>gemma-4-26b was the only one that kept "avoid … UNLESS SpO2 &lt; 92 %" correct — but it
    /// doesn't fit one 16 GB card, and the admin explicitly doesn't want a model split across two GPUs
    /// or spilling into RAM (too slow). So &gt; <see cref="MaxSingleGpuBillions"/> B sinks to the bottom.</item>
    /// <item>Of what fits, gemma-3-12b wrote the most natural Czech terminology; gemma-4-12b was
    /// clumsier and once returned an empty answer. Both (and aya-expanse-8b) inverted "unless" to
    /// "if" — no single-GPU model was safe on that, which is what the review queue is for.</item>
    /// </list>
    /// Reasoning models are pushed to the bottom too: their thinking text would land in the PDF.
    /// </summary>
    private static int Score(string id, string? sizeText)
    {
        var lower = id.ToLowerInvariant();
        var score = lower switch
        {
            _ when lower.Contains("gemma-3") || lower.Contains("gemma3") => 40,
            _ when lower.Contains("gemma-4") || lower.Contains("gemma4") => 38,
            _ when lower.Contains("qwen") => 25,
            _ when lower.Contains("aya") => 20,
            _ when lower.Contains("mistral") || lower.Contains("llama") => 10,
            _ => 15,
        };

        var billions = ParseBillions(sizeText) ?? ParseBillions(lower);
        if (billions is { } b) score += b > MaxSingleGpuBillions ? -60 : (int)b / 2;
        if (lower.Contains("qat")) score += 1; // same weights, quantization-aware — smaller/faster at equal quality
        if (lower.Contains("reasoning") || lower.Contains("think") || lower.Contains("-r1")) score -= 50;
        return score;
    }

    private static double? ParseBillions(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        // "4.3B", "gemma3:4b", "gemma-4-26b-a4b" (first match = total size, not the active-experts "a4b")
        var match = Regex.Match(text, @"(?<![a-z])(\d+(?:\.\d+)?)b(?![a-z])", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : null;
    }

    private static bool IsEmbeddingId(string id) => id.Contains("embed", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<JsonDocument?> TryGetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await HttpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null; // server not running / not this kind of server — just contributes nothing
        }
    }
}
