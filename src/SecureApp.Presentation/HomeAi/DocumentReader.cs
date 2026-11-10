using System.Runtime.CompilerServices;
using System.Text;

namespace SecureApp.Presentation.HomeAi;

/// <summary>Reading a long document in pieces: piece <paramref name="Index"/> of <paramref name="Total"/> is being read.</summary>
public sealed record ChunkStarted(int Index, int Total) : DecomposeEvent;

/// <summary>A long document got shortened to fit the limit on pieces (<paramref name="UsedChars"/> of <paramref name="TotalChars"/> characters are read).</summary>
public sealed record DocumentShortened(int UsedChars, int TotalChars) : DecomposeEvent;

/// <summary>
/// Lets a small model work with a document of any length (2026-11-10): if the document plus the question fit the model's
/// context it goes in one prompt; otherwise it is read in pieces that each fit, every piece is asked "what in here matters
/// for the question?", and only those short notes (condensed again if they are still too long) go into the final answer.
/// The sizes follow the context length of the loaded model, so a 4k model simply takes more, smaller pieces.
/// </summary>
public sealed class DocumentReader(HomeAiClient client)
{
    /// <summary>Characters per token: Czech text tokenizes heavily, so stay on the safe side.</summary>
    private const double CharsPerToken = 2.2;
    private const int ReservedTokens = 1800;
    private const int MaxChunks = 40;
    private const int MaxNoteChars = 700;

    /// <summary>True when the whole document fits the model's context together with the question and the answer.</summary>
    public static bool FitsInOnePrompt(string documentText, int contextTokens)
        => documentText.Length <= SingleBudget(contextTokens);

    private static int SingleBudget(int contextTokens) => (int)(Math.Max(1024, contextTokens - ReservedTokens) * CharsPerToken);

    private static int ChunkSize(int contextTokens) => Math.Clamp((int)(Math.Max(1024, contextTokens - 2400) * 1.6), 2000, 12000);

    public async IAsyncEnumerable<DecomposeEvent> AnswerAsync(
        HomeAiEndpoint endpoint,
        string model,
        string systemPrompt,
        string question,
        string fileName,
        string documentText,
        int contextTokens,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chunkSize = ChunkSize(contextTokens);
        var usable = documentText;
        if (usable.Length > chunkSize * MaxChunks)
        {
            usable = usable[..(chunkSize * MaxChunks)];
            yield return new DocumentShortened(usable.Length, documentText.Length);
        }

        var chunks = Split(usable, chunkSize);
        var notes = new List<string>();
        for (var i = 0; i < chunks.Count; i++)
        {
            yield return new ChunkStarted(i + 1, chunks.Count);
            var note = (await client.CompleteAsync(endpoint, model,
            [
                new("system", systemPrompt),
                new("user", $"Otázka uživatele: {question}\n\nÚsek {i + 1}/{chunks.Count} dokumentu „{fileName}“:\n<<<\n{chunks[i]}\n>>>\n\n" +
                             "Vypiš stručně (nejvýš 6 vět nebo odrážek) vše z tohoto úseku, co se týká otázky. Když tam nic relevantního není, napiš jen: nic."),
            ], temperature: 0.2, maxTokens: 400, ct).ConfigureAwait(false)).Trim();

            if (note.Length > 0 && !note.Equals("nic", StringComparison.OrdinalIgnoreCase) && !note.Equals("nic.", StringComparison.OrdinalIgnoreCase))
                notes.Add($"[úsek {i + 1}] {Trim(note, MaxNoteChars)}");
        }

        // Still too many notes for one prompt: condense them in groups, a few rounds at most.
        var noteBudget = SingleBudget(contextTokens) / 2;
        for (var round = 0; round < 3 && notes.Sum(n => n.Length) > noteBudget && notes.Count > 1; round++)
        {
            var condensed = new List<string>();
            foreach (var group in Group(notes, chunkSize))
            {
                condensed.Add(Trim((await client.CompleteAsync(endpoint, model,
                [
                    new("system", systemPrompt),
                    new("user", $"Otázka uživatele: {question}\n\nPoznámky:\n{string.Join("\n", group)}\n\nSpoj je do nejvýš 8 stručných odrážek, které se týkají otázky. Nic nevymýšlej."),
                ], temperature: 0.2, maxTokens: 500, ct).ConfigureAwait(false)).Trim(), MaxNoteChars * 2));
            }

            notes = condensed;
        }

        var prompt = new StringBuilder()
            .Append("Otázka uživatele: ").AppendLine(question)
            .AppendLine()
            .Append("Poznámky vybrané z dokumentu „").Append(fileName).AppendLine("“:");
        foreach (var note in notes)
            prompt.AppendLine(note);
        if (notes.Count == 0)
            prompt.AppendLine("(V dokumentu jsem k této otázce nic relevantního nenašel.)");
        prompt.AppendLine()
              .Append("Na základě těchto poznámek odpověz na otázku srozumitelně a přehledně. Vycházej jen z poznámek, nic si nevymýšlej; když v nich odpověď chybí, řekni to.");

        await foreach (var piece in client.StreamChatAsync(endpoint, model,
        [
            new("system", systemPrompt),
            new("user", prompt.ToString()),
        ], temperature: 0.4, maxTokens: null, ct).ConfigureAwait(false))
        {
            yield return new FinalToken(piece);
        }
    }

    /// <summary>Pieces of at most <paramref name="size"/> characters, cut at paragraph or line ends where possible.</summary>
    internal static List<string> Split(string text, int size)
    {
        var result = new List<string>();
        var start = 0;
        var header = string.Empty; // spreadsheet sheet header, repeated at the top of every later piece of that sheet
        while (start < text.Length)
        {
            var length = Math.Min(size, text.Length - start);
            if (start + length < text.Length)
            {
                var window = text.AsSpan(start, length);
                var cut = window.LastIndexOf("\n\n");
                if (cut < size / 2) cut = window.LastIndexOf('\n');
                if (cut < size / 2) cut = window.LastIndexOf(". ");
                if (cut >= size / 2) length = cut + 1;
            }

            var piece = text.Substring(start, length).Trim();
            if (piece.Length > 0)
            {
                result.Add(header.Length > 0 && !piece.StartsWith("## List:", StringComparison.Ordinal) ? header + "\n" + piece : piece);
                header = TrackSheetHeader(piece, header);
            }

            start += length;
        }

        return result;
    }

    /// <summary>The "## List / Sloupce / Souhrn" lines of the sheet the piece ends in (see DocumentTextExtractor), or the previous header when the piece starts none.</summary>
    private static string TrackSheetHeader(string piece, string current)
    {
        var lines = new List<string>();
        var collecting = false;
        var found = false;
        foreach (var line in piece.Split('\n'))
        {
            if (line.StartsWith("## List:", StringComparison.Ordinal))
            {
                lines.Clear();
                lines.Add(line);
                collecting = true;
                found = true;
            }
            else if (collecting && (line.StartsWith("Sloupce:", StringComparison.Ordinal) || line.StartsWith("Souhrn čísel", StringComparison.Ordinal)))
            {
                lines.Add(line.Length > 900 ? line[..900] + "…" : line);
            }
            else
            {
                collecting = false;
            }
        }

        return found ? string.Join("\n", lines) : current;
    }

    private static IEnumerable<List<string>> Group(List<string> notes, int maxChars)
    {
        var group = new List<string>();
        var size = 0;
        foreach (var note in notes)
        {
            if (group.Count > 0 && size + note.Length > maxChars)
            {
                yield return group;
                group = [];
                size = 0;
            }

            group.Add(note);
            size += note.Length;
        }

        if (group.Count > 0)
            yield return group;
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
