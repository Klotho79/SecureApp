using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SecureApp.Presentation.Workplace;

/// <summary>One person found in one workplace cell of pracoviste.php's weekly roster.</summary>
internal sealed record PracovisteEntry(DateOnly Date, string WorkplaceName, string PersonName, int? PersonId);

/// <summary>
/// Pure HTML/name parsing for <see cref="OpicentrumSyncService"/> — no I/O, no MAUI, so it can be
/// exercised directly against saved copies of the real pages.
/// </summary>
/// <remarks>
/// 2026-09-26 — pracoviste.php used to be read ONLY through the <c>onmousedown="datumupravovany=…;
/// osoba=…"</c> attributes on each name. Those are the roster EDITOR's click handlers, rendered only for
/// accounts allowed to edit the roster — an ordinary member sees the same table visually, but without
/// them, so their workplaces silently never imported (reported live: a member got his on-call duties and
/// leave, which are matched differently, but no workplaces at all). <see cref="ParsePracovisteWeek"/>
/// now reads the table the way a person does — dates from the header row, the workplace from each row's
/// first cell, people by name inside each day's cell — and treats the edit attributes as an optional
/// extra (a person id when present), never a requirement. One code path for every account.
/// </remarks>
internal static partial class OpicentrumParsing
{
    /// <summary>
    /// Parses one week of pracoviste.php into every (date, workplace, person) it lists. The
    /// "NEPŘÍTOMNÍ" (absent) and "Plánované AKCE" rows are skipped — they are not workplace
    /// assignments (spravavolna.php is the source of truth for absence).
    /// </summary>
    public static List<PracovisteEntry> ParsePracovisteWeek(string html)
    {
        var entries = new List<PracovisteEntry>();
        List<DateOnly>? columnDates = null;

        foreach (Match rowMatch in TableRowRegex().Matches(html))
        {
            var cells = TableCellRegex().Matches(rowMatch.Value);
            if (cells.Count < 2) continue;

            // Header row: every cell after the first is exactly one date ("<b>21.09.2026</b>").
            var headerDates = new List<DateOnly>();
            for (var i = 1; i < cells.Count; i++)
            {
                var m = HeaderDateRegex().Match(cells[i].Groups["inner"].Value);
                if (!m.Success) { headerDates = null; break; }
                headerDates.Add(DateOnly.ParseExact(m.Groups["date"].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture));
            }
            if (headerDates is not null)
            {
                columnDates = headerDates;
                continue;
            }

            var label = CleanText(cells[0].Groups["inner"].Value);
            if (label.Length == 0 || label is "NEPŘÍTOMNÍ" or "Plánované AKCE") continue;
            var workplaceName = label == "NEZAŘAZENÍ" ? "Nezařazen" : label;

            for (var i = 1; i < cells.Count; i++)
            {
                DateOnly? columnDate = columnDates is not null && i - 1 < columnDates.Count ? columnDates[i - 1] : null;
                foreach (var (name, personId, cellDate) in ExtractPeople(cells[i].Groups["inner"].Value))
                {
                    // The header column is authoritative; the editor-only attribute date is a fallback
                    // for a page whose header could not be read.
                    if ((columnDate ?? cellDate) is { } date)
                        entries.Add(new PracovisteEntry(date, workplaceName, name, personId));
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Splits one day cell into the people it names. A cell holds one or more names, each in its own
    /// block (<c>&lt;div&gt;</c> or after a <c>&lt;br&gt;</c>), possibly underlined (L3 supervisors), possibly with a
    /// bold shift-code suffix ("Petr Faltus&amp;nbsp;&lt;b&gt;S6&lt;/b&gt;") that is not part of the name.
    /// </summary>
    private static IEnumerable<(string Name, int? PersonId, DateOnly? Date)> ExtractPeople(string cellHtml)
    {
        foreach (var chunk in PersonChunkSplitRegex().Split(cellHtml))
        {
            var attr = PersonAttributeRegex().Match(chunk);
            int? personId = attr.Success ? int.Parse(attr.Groups["osoba"].Value, CultureInfo.InvariantCulture) : null;
            DateOnly? date = attr.Success && DateOnly.TryParseExact(attr.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

            var names = CleanText(ShiftCodeSuffixRegex().Replace(chunk, " "))
                .Split(';')
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .ToList();

            foreach (var name in names)
                yield return (name, names.Count == 1 ? personId : null, date);
        }
    }

    /// <summary>
    /// Compares two names the way a human would, instead of by exact string equality. The portal is
    /// hand-maintained HTML: the same person can come back as "Petr Faltus" from the sidebar greeting
    /// and with a non-breaking space, a doubled space, a leading academic title or reversed order from
    /// a table cell. An exact <c>string.Equals</c> fails on every one of those and then silently yields
    /// an empty schedule.
    /// </summary>
    public static bool NamesMatch(string a, string b)
    {
        var left = NormalizeName(a);
        var right = NormalizeName(b);
        if (left.Length == 0 || right.Length == 0) return false;
        if (left == right) return true;

        // "Faltus Petr" vs "Petr Faltus" — the portal is not consistent about order between pages.
        var leftParts = left.Split(' ');
        var rightParts = right.Split(' ');
        return leftParts.Length > 1
            && leftParts.Length == rightParts.Length
            && leftParts.OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(rightParts.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>Lowercases, drops academic titles, strips diacritics, and collapses every run of any Unicode whitespace (incl. the U+00A0 a decoded <c>&amp;nbsp;</c> leaves behind) to one plain space.</summary>
    public static string NormalizeName(string value)
    {
        var withoutTitles = AcademicTitleRegex().Replace(value, " ");
        var decomposed = withoutTitles.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var ch in decomposed)
        {
            if (char.IsWhiteSpace(ch)) { pendingSpace = builder.Length > 0; continue; }
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue; // the accent half of a decomposed letter
            if (pendingSpace) { builder.Append(' '); pendingSpace = false; }
            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    public static string StripTags(string html) => TagRegex().Replace(html, string.Empty);

    private static string CleanText(string html) => WebUtility.HtmlDecode(StripTags(html)).Trim();

    [GeneratedRegex(@"<tr class=""planakci(?:sns)?""[^>]*>.*?</tr>", RegexOptions.Singleline)]
    private static partial Regex TableRowRegex();

    /// <summary>Tolerates an omitted <c>&lt;/td&gt;</c> (old hand-written HTML) by also ending a cell at the next cell or the row end.</summary>
    [GeneratedRegex(@"<td\b[^>]*>(?<inner>.*?)(?=</td>|<td\b|</tr>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TableCellRegex();

    [GeneratedRegex(@"^\s*<b>\s*(?<date>\d{2}\.\d{2}\.\d{4})\s*</b>\s*$")]
    private static partial Regex HeaderDateRegex();

    /// <summary>Boundaries between names inside one day cell: the start of any <c>&lt;div</c>, any <c>&lt;/div&gt;</c>, any <c>&lt;br&gt;</c>.</summary>
    [GeneratedRegex(@"(?=<div\b)|</div>|<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex PersonChunkSplitRegex();

    /// <summary>The roster editor's click handler — present only for accounts allowed to edit the roster, so optional.</summary>
    [GeneratedRegex(@"datumupravovany=(?<date>\d{8});\s*osoba=(?<osoba>\d+)")]
    private static partial Regex PersonAttributeRegex();

    /// <summary>The bold shift code after a name ("&amp;nbsp;&lt;b&gt;S6&lt;/b&gt;").</summary>
    [GeneratedRegex(@"<b>\s*S\d{1,2}\s*</b>", RegexOptions.IgnoreCase)]
    private static partial Regex ShiftCodeSuffixRegex();

    /// <summary>Czech academic titles that appear inconsistently between the sidebar greeting and the roster tables. Matched as whole words only, so a surname like "Mudra" is untouched.</summary>
    [GeneratedRegex(@"(?i)\b(MUDr|MVDr|MDDr|PharmDr|RNDr|PhDr|JUDr|Ing|Bc|Mgr|PhD|CSc|DrSc|prim|doc|prof)\b\.?", RegexOptions.CultureInvariant)]
    private static partial Regex AcademicTitleRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();
}
