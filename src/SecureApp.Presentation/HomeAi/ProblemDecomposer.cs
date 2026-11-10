using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureApp.Presentation.HomeAi;

public abstract record DecomposeEvent;

/// <summary>The problem was split into these short questions.</summary>
public sealed record PlanReady(IReadOnlyList<string> Questions) : DecomposeEvent;

public sealed record StepStarted(int Index, int Total, string Question) : DecomposeEvent;

public sealed record StepFinished(int Index, int Total, string Question, string Answer) : DecomposeEvent;

/// <summary>A piece of the final, assembled answer.</summary>
public sealed record FinalToken(string Text) : DecomposeEvent;

/// <summary>
/// Lets a small model handle a big problem (2026-11-10, user's ask): instead of one long prompt the app (1) asks the
/// model only to split the problem into 2–5 short sub-questions, (2) asks each sub-question on its own, with just
/// the original problem and short notes from the earlier steps, and (3) asks for the final answer from those notes.
/// Every call stays small, so a 4–8B model keeps the thread; the cost is a few more (fast) requests.
/// </summary>
public sealed class ProblemDecomposer(HomeAiClient client)
{
    public const int MaxSteps = 5;

    private const int NoteLength = 600;
    private const int QuestionLength = 300;

    public async IAsyncEnumerable<DecomposeEvent> SolveAsync(
        HomeAiEndpoint endpoint,
        string model,
        string systemPrompt,
        string problem,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1) Plan: only a JSON list of questions, nothing else — low temperature so a small model sticks to the format.
        var planRaw = await client.CompleteAsync(endpoint, model,
        [
            new("system", "Jsi plánovač. Rozlož zadaný problém na 2 až 5 krátkých, na sobě navazujících dílčích otázek, " +
                          "na které lze odpovědět samostatně. Odpověz POUZE JSON polem řetězců, například [\"první otázka\",\"druhá otázka\"]. Nic jiného nepiš."),
            new("user", problem),
        ], temperature: 0.2, maxTokens: 400, ct).ConfigureAwait(false);

        var questions = ParsePlan(planRaw, problem);
        yield return new PlanReady(questions);

        // 2) Each sub-question on its own, with the problem and the earlier answers as short notes.
        var notes = new List<(string Question, string Answer)>();
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            yield return new StepStarted(i + 1, questions.Count, question);

            var prompt = new StringBuilder()
                .Append("Celkový problém: ").AppendLine(problem)
                .AppendLine();
            if (notes.Count > 0)
            {
                prompt.AppendLine("Dosavadní poznatky:");
                foreach (var (q, a) in notes)
                    prompt.Append("- ").Append(q).Append(" → ").AppendLine(Trim(a, NoteLength));
                prompt.AppendLine();
            }

            prompt.Append("Dílčí otázka ").Append(i + 1).Append('/').Append(questions.Count).Append(": ").AppendLine(question)
                  .Append("Odpověz jen na tuto dílčí otázku, stručně (nejvýš 5 vět).");

            var answer = (await client.CompleteAsync(endpoint, model,
            [
                new("system", systemPrompt),
                new("user", prompt.ToString()),
            ], temperature: 0.4, maxTokens: 500, ct).ConfigureAwait(false)).Trim();

            notes.Add((question, answer));
            yield return new StepFinished(i + 1, questions.Count, question, answer);
        }

        // 3) Final answer from the notes only (streamed).
        var summary = new StringBuilder()
            .Append("Problém: ").AppendLine(problem)
            .AppendLine()
            .AppendLine("Poznatky z jednotlivých kroků:");
        foreach (var (q, a) in notes)
            summary.Append("- ").Append(q).Append(" → ").AppendLine(Trim(a, NoteLength));
        summary.AppendLine()
               .Append("Na základě těchto poznatků napiš výslednou odpověď nebo doporučení, srozumitelně a přehledně. Pokud si poznatky odporují, upozorni na to.");

        await foreach (var piece in client.StreamChatAsync(endpoint, model,
        [
            new("system", systemPrompt),
            new("user", summary.ToString()),
        ], temperature: 0.4, maxTokens: null, ct).ConfigureAwait(false))
        {
            yield return new FinalToken(piece);
        }
    }

    /// <summary>A small model does not always produce clean JSON: take the first [...] if there is one, else numbered/bulleted lines, else the whole problem as one step.</summary>
    internal static IReadOnlyList<string> ParsePlan(string raw, string fallbackProblem)
    {
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            try
            {
                var items = JsonSerializer.Deserialize<string[]>(raw[start..(end + 1)]);
                var cleaned = Clean(items ?? []);
                if (cleaned.Count > 0)
                    return cleaned;
            }
            catch (JsonException)
            {
            }
        }

        var lines = raw.Split('\n')
            .Select(l => Regex.Replace(l.Trim(), @"^\s*(?:[-*•]|\d+[.)])\s*", string.Empty).Trim().Trim('"', ',', ' '))
            .Where(l => l.Length >= 8);
        var fromLines = Clean(lines);
        return fromLines.Count > 0 ? fromLines : [Trim(fallbackProblem, QuestionLength * 2)];
    }

    private static List<string> Clean(IEnumerable<string> items)
        => items.Select(i => Trim(i.Trim(), QuestionLength)).Where(i => i.Length > 0).Take(MaxSteps).ToList();

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
