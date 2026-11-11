using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace SecureApp.Presentation.HomeAi;

/// <summary>A grey note in the chat (what was selected / what the program computed).</summary>
public sealed record NoteStep(string Text) : DecomposeEvent;

internal sealed class TableQueryException(string message) : Exception(message);

/// <summary>What a model-written query turned into: the exact result of the calculation, ready for the final answer.</summary>
internal sealed record TableQueryOutcome(string Description, string ResultText);

/// <summary>
/// Exact spreadsheet calculations for the home AI (2026-11-10, user: "sečtení vyselektovaných řádků a počítání s nimi"). A model
/// cannot add up or count hundreds of rows reliably, so it only translates the question into a small JSON query (which rows,
/// what to count or sum, grouped by what); this class runs that query on the real table and the model then words the answer
/// from the exact numbers.
/// </summary>
internal sealed class TableQueryRunner(HomeAiClient client)
{
    private const string FormatHelp =
        "Formát odpovědi (jediný JSON objekt, nic jiného):\n" +
        "{\"query\":true,\"sheet\":\"název listu\",\"filters\":[{\"column\":\"název sloupce\",\"op\":\"=\",\"value\":\"hodnota\"}]," +
        "\"group_by\":[\"sloupec\"],\"count\":true,\"sum\":[\"sloupec\"],\"avg\":[],\"min\":[],\"max\":[],\"count_distinct\":[]}\n" +
        "Operátory: =, !=, contains, startswith, >, >=, <, <=, between (value je [\"od\",\"do\"]), in (value je [\"a\",\"b\"]), empty, not_empty.\n" +
        "Pravidla: názvy sloupců piš přesně ze seznamu; kódy diagnóz hledej operátorem startswith (C50 najde i C50.1), " +
        "více kódů najednou startswith se seznamem [\"C50\",\"C18\"]; " +
        "když se ptá „kolik bodů / korun / kusů / celkem“ a takový číselný sloupec existuje, dej ho do sum (count jen počítá řádky); " +
        "filters, group_by a ostatní pole vynech nebo nech prázdná, když je nepotřebuješ; count:true = spočítat vybrané řádky. " +
        "Když otázka nevyžaduje výběr řádků ani počítání (shrnutí, vysvětlení, přehled), vrať jen {\"query\":false}.\n" +
        "Příklady:\n" +
        "Otázka: Kolik výkonů má hlavní diagnóza C50 a kolik je to dohromady podle sloupce Počet?\n" +
        "{\"query\":true,\"filters\":[{\"column\":\"Hlavní diagnóza\",\"op\":\"startswith\",\"value\":\"C50\"}],\"count\":true,\"sum\":[\"Počet\"]}\n" +
        "Otázka: Kolik bodů mají výkony s diagnózou C50 nebo C18 v lednu 2026?\n" +
        "{\"query\":true,\"filters\":[{\"column\":\"Hlavní diagnóza\",\"op\":\"startswith\",\"value\":[\"C50\",\"C18\"]},{\"column\":\"Datum\",\"op\":\"between\",\"value\":[\"2026-01-01\",\"2026-01-31\"]}],\"count\":true,\"sum\":[\"Body\"]}\n" +
        "Otázka: Součet počtů podle diagnóz\n" +
        "{\"query\":true,\"group_by\":[\"Hlavní diagnóza\"],\"count\":true,\"sum\":[\"Počet\"]}\n" +
        "Otázka: Shrň tabulku\n{\"query\":false}";

    /// <summary>Asks the model for a query, runs it and returns the result; null when the question is not a calculation or no valid query came out (<paramref name="note"/> says why).</summary>
    public async Task<TableQueryOutcome?> PrepareAsync(
        HomeAiEndpoint endpoint, string model, string question, IReadOnlyList<SpreadsheetTable> tables, Action<string> note, CancellationToken ct)
    {
        var schema = Describe(tables);
        string? lastError = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var prompt = $"{schema}\n\n{FormatHelp}\n\nOtázka: {question}" + (lastError is null ? string.Empty : $"\n\nPředchozí pokus selhal: {lastError}\nOpravená odpověď (jen JSON):");
            HomeAiChatMessage[] messages =
            [
                new("system", "Převádíš otázky uživatele na JSON dotaz nad tabulkou. Odpovídej POUZE jedním JSON objektem, bez vysvětlování a bez značek."),
                new("user", prompt),
            ];

            // The query's shape is enforced by the server (column names and operators as enums), so a small model cannot
            // invent a column or write broken JSON; a server or model that does not support it falls back to plain text.
            string reply;
            if (!s_schemaUnsupported)
            {
                try
                {
                    reply = await client.CompleteAsync(endpoint, model, messages, temperature: 0, maxTokens: 500, ct, ResponseFormat(tables)).ConfigureAwait(false);
                }
                catch (HomeAiException ex) when (ex.StatusCode == 400)
                {
                    s_schemaUnsupported = true;
                    reply = await client.CompleteAsync(endpoint, model, messages, temperature: 0, maxTokens: 500, ct).ConfigureAwait(false);
                }
            }
            else
            {
                reply = await client.CompleteAsync(endpoint, model, messages, temperature: 0, maxTokens: 500, ct).ConfigureAwait(false);
            }

            try
            {
                using var doc = ParseJson(reply);
                var root = doc.RootElement;
                if (root.TryGetProperty("query", out var flag) && flag.ValueKind == JsonValueKind.False)
                {
                    note("Otázka nevyžaduje výběr řádků ani počítání, čtu tabulku jako text.");
                    return null;
                }

                return TableQueryEngine.Execute(tables, root);
            }
            catch (Exception ex) when (ex is JsonException or TableQueryException)
            {
                lastError = ex.Message;
            }
        }

        note($"Dotaz na tabulku se nepodařilo sestavit ({lastError}). Čtu tabulku jako text.");
        return null;
    }

    /// <summary>The final answer, worded from the exact result; the selection and the numbers are shown first as a grey note.</summary>
    public async IAsyncEnumerable<DecomposeEvent> AnswerAsync(
        HomeAiEndpoint endpoint, string model, string systemPrompt, string question, TableQueryOutcome outcome, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new NoteStep($"Výběr z tabulky: {outcome.Description}\n{outcome.ResultText}");

        var prompt = $"Otázka uživatele: {question}\n\nVýsledek výpočtu nad tabulkou (přesně spočítáno programem):\n{outcome.ResultText}\n\n" +
                     "Odpověz na otázku jen z těchto údajů. Čísla přepiš přesně tak, jak jsou, nic nepřepočítávej ani nezaokrouhluj. " +
                     "Pokud je výsledek ve skupinách, uveď je přehledně. Když z výsledku odpověď nevyplývá, řekni to.";
        await foreach (var piece in client.StreamChatAsync(endpoint, model,
        [
            new("system", systemPrompt),
            new("user", prompt),
        ], temperature: 0.2, maxTokens: null, ct).ConfigureAwait(false))
        {
            yield return new FinalToken(piece);
        }
    }

    /// <summary>Set once the server refused a JSON-schema request (older LM Studio / unsupported model), for the rest of the app's run.</summary>
    private static bool s_schemaUnsupported;

    private static readonly string[] Operators = ["=", "!=", "contains", "not_contains", "startswith", ">", ">=", "<", "<=", "between", "in", "not_in", "empty", "not_empty"];

    /// <summary>OpenAI-style <c>response_format</c> with a JSON schema of the query; LM Studio turns it into a grammar the model must follow.</summary>
    internal static object ResponseFormat(IReadOnlyList<SpreadsheetTable> tables)
    {
        var used = tables.Take(4).ToList();
        var columns = used.SelectMany(t => t.Columns).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList();
        var sheets = used.Select(t => t.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();

        Dictionary<string, object> Column() => columns.Count is > 0 and <= 300
            ? new() { ["type"] = "string", ["enum"] = columns }
            : new() { ["type"] = "string" };
        Dictionary<string, object> ColumnList(int? maxItems = null)
        {
            var list = new Dictionary<string, object> { ["type"] = "array", ["items"] = Column() };
            if (maxItems is { } max)
                list["maxItems"] = max;
            return list;
        }

        var properties = new Dictionary<string, object>
        {
            ["query"] = new { type = "boolean" },
        };
        if (sheets.Count > 0)
            properties["sheet"] = new { type = "string", @enum = sheets };
        properties["filters"] = new Dictionary<string, object>
        {
            ["type"] = "array",
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["column"] = Column(),
                    ["op"] = new { type = "string", @enum = Operators },
                    ["value"] = new { anyOf = new object[] { new { type = "string" }, new { type = "array", items = new { type = "string" } } } },
                },
                ["required"] = new[] { "column", "op" },
                ["additionalProperties"] = false,
            },
        };
        properties["group_by"] = ColumnList(2);
        properties["count"] = new { type = "boolean" };
        foreach (var aggregate in new[] { "sum", "avg", "min", "max", "count_distinct" })
            properties[aggregate] = ColumnList();

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "table_query",
                strict = true,
                schema = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = new[] { "query" },
                    ["additionalProperties"] = false,
                },
            },
        };
    }

    private static JsonDocument ParseJson(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new TableQueryException("odpověď modelu neobsahuje JSON");
        return JsonDocument.Parse(reply[start..(end + 1)], new JsonDocumentOptions { AllowTrailingCommas = true });
    }

    /// <summary>The columns of every sheet with their type and, for short lists of values, the values themselves: what the model needs to write a correct query.</summary>
    internal static string Describe(IReadOnlyList<SpreadsheetTable> tables)
    {
        var text = new StringBuilder("Tabulky:\n");
        foreach (var table in tables.Take(4))
        {
            text.Append("List „").Append(table.Name).Append("“ (").Append(table.Rows.Count).AppendLine(" řádků), sloupce:");
            for (var c = 0; c < table.Columns.Length; c++)
            {
                var values = table.Rows.Where(r => c < r.Values.Length).Select(r => r.Values[c]).Where(v => DocumentTextExtractor.CellText(v).Length > 0).ToList();
                if (values.Count == 0)
                    continue;

                text.Append("- ").Append(table.Columns[c]);
                var numbers = values.Select(TableQueryEngine.ToNumber).Where(n => n is not null).Select(n => n!.Value).ToList();
                if (values.All(v => v is DateTime))
                {
                    var dates = values.Cast<DateTime>().ToList();
                    text.Append(" (datum od ").Append(dates.Min().ToString("d. M. yyyy", CultureInfo.InvariantCulture)).Append(" do ").Append(dates.Max().ToString("d. M. yyyy", CultureInfo.InvariantCulture)).AppendLine(")");
                }
                else if (numbers.Count >= values.Count * 0.8)
                {
                    text.Append(" (číslo, ").Append(TableQueryEngine.Num(numbers.Min())).Append(" až ").Append(TableQueryEngine.Num(numbers.Max())).AppendLine(")");
                }
                else
                {
                    var distinct = values.Select(DocumentTextExtractor.CellText).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    text.Append(" (text; ").Append(distinct.Count).Append(" různých hodnot, ")
                        .Append(distinct.Count <= 30 ? "např. " + string.Join(", ", distinct.Take(30)) : "např. " + string.Join(", ", distinct.Take(6)))
                        .AppendLine(")");
                }
            }
        }

        return text.Length > 6000 ? text.ToString(0, 6000) : text.ToString();
    }
}

internal static class TableQueryEngine
{
    private sealed record Filter(int Column, string Op, string[] Values);

    public static TableQueryOutcome Execute(IReadOnlyList<SpreadsheetTable> tables, JsonElement q)
    {
        var table = PickTable(tables, q);
        var filters = new List<Filter>();
        if (q.TryGetProperty("filters", out var filterList) && filterList.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in filterList.EnumerateArray())
            {
                var column = Column(table, Str(f, "column"));
                var op = NormalizeOp(Str(f, "op"));
                filters.Add(new Filter(column, op, Values(f)));
            }
        }

        var groupBy = Names(q, "group_by").Select(n => Column(table, n)).Take(2).ToList();
        var sums = Names(q, "sum").Select(n => Column(table, n)).ToList();
        var avgs = Names(q, "avg").Select(n => Column(table, n)).ToList();
        var mins = Names(q, "min").Select(n => Column(table, n)).ToList();
        var maxs = Names(q, "max").Select(n => Column(table, n)).ToList();
        var distincts = Names(q, "count_distinct").Select(n => Column(table, n)).ToList();
        var count = (q.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.True)
                    || (sums.Count + avgs.Count + mins.Count + maxs.Count + distincts.Count == 0);

        var selected = table.Rows.Where(r => filters.All(f => Matches(r.Values, f))).ToList();

        var description = new StringBuilder("list „").Append(table.Name).Append('“');
        description.Append(filters.Count == 0
            ? ", všechny řádky"
            : ", " + string.Join(" a ", filters.Select(f => $"{table.Columns[f.Column]} {OpText(f.Op)} {string.Join(f.Op is "in" or "not_in" or "startswith" ? " / " : " … ", f.Values)}".Trim())));
        if (groupBy.Count > 0)
            description.Append(", po skupinách podle ").Append(string.Join(" a ", groupBy.Select(g => table.Columns[g])));

        var result = new StringBuilder();
        result.Append("Vyhovuje ").Append(selected.Count).Append(" z ").Append(table.Rows.Count).AppendLine(" řádků.");

        if (groupBy.Count == 0)
        {
            result.AppendLine(Aggregate(selected, table, count, sums, avgs, mins, maxs, distincts, "Celkem"));
        }
        else
        {
            var groups = selected.GroupBy(r => string.Join(" / ", groupBy.Select(g => KeyText(r.Values, g))), StringComparer.OrdinalIgnoreCase)
                .Select(g => (Key: g.Key, Rows: g.ToList()))
                .OrderByDescending(g => sums.Count > 0 ? g.Rows.Sum(r => ToNumber(Cell(r.Values, sums[0])) ?? 0) : g.Rows.Count)
                .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            result.Append("Skupin: ").Append(groups.Count).AppendLine(".");
            foreach (var group in groups.Take(80))
                result.AppendLine(Aggregate(group.Rows, table, count, sums, avgs, mins, maxs, distincts, group.Key));
            if (groups.Count > 80)
                result.Append("… dalších skupin: ").Append(groups.Count - 80).AppendLine(" (neuvedeno).");
        }

        if (groupBy.Count == 0 && selected.Count > 0 && selected.Count <= 40)
            result.Append("Řádky: ").Append(string.Join(", ", selected.Select(r => "Ř" + r.Number))).AppendLine();
        else if (groupBy.Count == 0 && selected.Count > 40)
            result.Append("Řádky: první ").Append(40).Append(" z ").Append(selected.Count).Append(": ").Append(string.Join(", ", selected.Take(40).Select(r => "Ř" + r.Number))).AppendLine();

        return new TableQueryOutcome(description.ToString(), result.ToString().TrimEnd());
    }

    private static string Aggregate(List<(int Number, object?[] Values)> rows, SpreadsheetTable table, bool count, List<int> sums, List<int> avgs, List<int> mins, List<int> maxs, List<int> distincts, string label)
    {
        var parts = new List<string>();
        if (count)
            parts.Add($"počet řádků {rows.Count}");
        foreach (var c in sums)
            parts.Add($"součet {table.Columns[c]} = {Num(Numbers(rows, c).Sum())}");
        foreach (var c in avgs)
        {
            var n = Numbers(rows, c);
            parts.Add(n.Count == 0 ? $"průměr {table.Columns[c]} = (žádná čísla)" : $"průměr {table.Columns[c]} = {Num(n.Average())}");
        }

        foreach (var c in mins)
        {
            var n = Numbers(rows, c);
            parts.Add(n.Count == 0 ? $"min {table.Columns[c]} = (žádná čísla)" : $"min {table.Columns[c]} = {Num(n.Min())}");
        }

        foreach (var c in maxs)
        {
            var n = Numbers(rows, c);
            parts.Add(n.Count == 0 ? $"max {table.Columns[c]} = (žádná čísla)" : $"max {table.Columns[c]} = {Num(n.Max())}");
        }

        foreach (var c in distincts)
            parts.Add($"různých hodnot {table.Columns[c]} = {rows.Select(r => DocumentTextExtractor.CellText(Cell(r.Values, c))).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count()}");

        return $"{label}: {string.Join(", ", parts)}";
    }

    private static List<double> Numbers(List<(int Number, object?[] Values)> rows, int column)
        => rows.Select(r => ToNumber(Cell(r.Values, column))).Where(n => n is not null).Select(n => n!.Value).ToList();

    private static object? Cell(object?[] values, int column) => column < values.Length ? values[column] : null;

    private static string KeyText(object?[] values, int column)
    {
        var text = DocumentTextExtractor.CellText(Cell(values, column));
        return text.Length == 0 ? "(prázdné)" : text;
    }

    private static SpreadsheetTable PickTable(IReadOnlyList<SpreadsheetTable> tables, JsonElement q)
    {
        if (tables.Count == 0)
            throw new TableQueryException("v souboru není žádná tabulka");
        var name = Str(q, "sheet");
        if (name.Length > 0)
        {
            var wanted = Norm(name);
            var found = tables.FirstOrDefault(t => Norm(t.Name) == wanted) ?? tables.FirstOrDefault(t => Norm(t.Name).Contains(wanted));
            if (found is not null)
                return found;
        }

        return tables.OrderByDescending(t => t.Rows.Count).First();
    }

    private static int Column(SpreadsheetTable table, string name)
    {
        var wanted = Norm(name);
        if (wanted.Length > 0)
        {
            for (var i = 0; i < table.Columns.Length; i++)
                if (Norm(table.Columns[i]) == wanted)
                    return i;
            if (wanted.Length >= 3)
            {
                for (var i = 0; i < table.Columns.Length; i++)
                {
                    var column = Norm(table.Columns[i]);
                    if (column.Contains(wanted) || wanted.Contains(column))
                        return i;
                }
            }
        }

        throw new TableQueryException($"sloupec „{name}“ neexistuje; sloupce jsou: {string.Join(", ", table.Columns)}");
    }

    private static string Str(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static IEnumerable<string> Names(JsonElement q, string property)
    {
        if (!q.TryGetProperty(property, out var value))
            return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList(),
            JsonValueKind.String when !string.IsNullOrWhiteSpace(value.GetString()) => [value.GetString()!],
            _ => [],
        };
    }

    private static string[] Values(JsonElement filter)
    {
        if (!filter.TryGetProperty("value", out var value))
            return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Select(ScalarText).ToArray(),
            _ => [ScalarText(value)],
        };
    }

    private static string ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "ano",
        JsonValueKind.False => "ne",
        _ => string.Empty,
    };

    private static string NormalizeOp(string op) => op.Trim().ToLowerInvariant() switch
    {
        "=" or "==" or "eq" or "equals" or "je" => "=",
        "!=" or "<>" or "ne" or "neq" => "!=",
        "contains" or "like" or "obsahuje" => "contains",
        "not_contains" or "notcontains" => "not_contains",
        "startswith" or "starts_with" or "begins" or "zacina" => "startswith",
        ">" or "gt" => ">",
        ">=" or "gte" => ">=",
        "<" or "lt" => "<",
        "<=" or "lte" => "<=",
        "between" or "range" => "between",
        "in" => "in",
        "not_in" or "notin" => "not_in",
        "empty" or "is_empty" => "empty",
        "not_empty" or "notempty" or "is_not_empty" => "not_empty",
        var other => throw new TableQueryException($"neznámý operátor „{other}“"),
    };

    private static string OpText(string op) => op switch
    {
        "=" => "=",
        "!=" => "≠",
        "contains" => "obsahuje",
        "not_contains" => "neobsahuje",
        "startswith" => "začíná na",
        "between" => "je mezi",
        "in" => "je jedna z",
        "not_in" => "není žádná z",
        "empty" => "je prázdné",
        "not_empty" => "není prázdné",
        _ => op,
    };

    private static bool Matches(object?[] row, Filter filter)
    {
        var cell = Cell(row, filter.Column);
        var text = DocumentTextExtractor.CellText(cell);
        var values = filter.Values;

        switch (filter.Op)
        {
            case "empty":
                return text.Length == 0;
            case "not_empty":
                return text.Length > 0;
        }

        if (values.Length == 0)
            throw new TableQueryException("filtr nemá hodnotu");

        var norm = Norm(text);
        switch (filter.Op)
        {
            case "=":
                return Equal(cell, norm, values[0]);
            case "!=":
                return !Equal(cell, norm, values[0]);
            case "in":
                return values.Any(v => Equal(cell, norm, v));
            case "not_in":
                return !values.Any(v => Equal(cell, norm, v));
            case "contains":
                return norm.Contains(Norm(values[0]));
            case "not_contains":
                return !norm.Contains(Norm(values[0]));
            case "startswith":
                return values.Any(v => norm.StartsWith(Norm(v), StringComparison.Ordinal));
            case "between":
                if (values.Length < 2)
                    throw new TableQueryException("between potřebuje dvě hodnoty");
                return Compare(cell, norm, values[0]) is { } lo && lo >= 0 && Compare(cell, norm, values[1]) is { } hi && hi <= 0;
            default: // > >= < <=
                var cmp = Compare(cell, norm, values[0]);
                return cmp is not null && filter.Op switch { ">" => cmp > 0, ">=" => cmp >= 0, "<" => cmp < 0, _ => cmp <= 0 };
        }
    }

    private static bool Equal(object? cell, string normText, string value)
    {
        if (ToNumber(cell) is { } number && ToNumber(value) is { } wanted)
            return Math.Abs(number - wanted) < 1e-9;
        if (cell is DateTime date && ToDate(value) is { } wantedDate)
            return date.Date == wantedDate.Date;
        var wantedText = Norm(value);
        // Hierarchical codes: "C50" also means C50.1, C50.9 (the model asks for "C50 nebo C18" with = / in, not startswith).
        return normText == wantedText || (wantedText.Length > 0 && normText.StartsWith(wantedText + ".", StringComparison.Ordinal));
    }

    private static int? Compare(object? cell, string normText, string value)
    {
        if (ToNumber(cell) is { } number && ToNumber(value) is { } wanted)
            return number.CompareTo(wanted);
        if (cell is DateTime date && ToDate(value) is { } wantedDate)
            return date.Date.CompareTo(wantedDate.Date);
        return normText.Length == 0 ? null : string.CompareOrdinal(normText, Norm(value));
    }

    internal static double? ToNumber(object? value)
    {
        switch (value)
        {
            case double d:
                return d;
            case int i:
                return i;
            case long l:
                return l;
            case string s:
                var cleaned = s.Trim().Replace(" ", string.Empty).Replace(' '.ToString(), string.Empty).Replace(',', '.');
                return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            default:
                return null;
        }
    }

    private static DateTime? ToDate(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("cs-CZ"), DateTimeStyles.None, out var cz))
            return cz;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var inv) ? inv : null;
    }

    /// <summary>Lower case without diacritics and with single spaces, so "Hlavní diagnóza" and "hlavni  diagnoza" are the same.</summary>
    private static string Norm(string text)
    {
        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace)
                    sb.Append(' ');
                lastSpace = true;
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
            lastSpace = false;
        }

        return sb.ToString();
    }

    internal static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
