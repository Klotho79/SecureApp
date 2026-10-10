using System.Globalization;
using System.Text;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Compares the shared ARIM contact list (originally the WhatsApp group's "Telefonní seznam
/// ARIM.xlsx") against every name Opicentrum shows (2026-10-10, user's ask). Pure text report — the
/// admin reads it and fixes whichever side is wrong; nothing is changed automatically, since either
/// list can be the one with the typo.
///
/// Uses <see cref="OpicentrumParsing.NamesMatch"/> — the very matcher the Rozpis sync and the duty
/// widget use — so "shoda" here means "the app will pair these two correctly", not merely "looks alike".
/// </summary>
public static class OpicentrumNameCheck
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    public static string BuildReport(IEnumerable<string> contactNames, IReadOnlyList<string> opicentrumNames)
    {
        // "Vilém Dvořák - služební" etc. are service phones of a real person — compare the person part.
        var contacts = contactNames
            .Select(n => (Raw: n, Person: StripSuffix(n)))
            .Where(c => c.Person.Any(char.IsLetter))
            .ToList();

        var exact = 0;
        var spelledDifferently = new List<string>();
        var probableTypos = new List<string>();
        var onlyInContacts = new List<string>();
        var matchedOpicentrum = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (raw, person) in contacts)
        {
            var match = opicentrumNames.FirstOrDefault(o => OpicentrumParsing.NamesMatch(o, person));
            if (match is not null)
            {
                matchedOpicentrum.Add(match);
                if (SameWords(match, person)) exact++;
                else spelledDifferently.Add($"{raw}  ↔  {match}");
                continue;
            }

            var lookalikes = opicentrumNames.Where(o => !matchedOpicentrum.Contains(o) && LikelySamePerson(o, person)).ToList();
            if (lookalikes.Count > 0)
            {
                matchedOpicentrum.UnionWith(lookalikes);
                probableTypos.Add($"{raw}  ↔  {string.Join(" / ", lookalikes)}");
            }
            else
            {
                onlyInContacts.Add(raw);
            }
        }

        var onlyInOpicentrum = opicentrumNames.Where(o => !matchedOpicentrum.Contains(o)).ToList();

        var report = new StringBuilder();
        report.AppendLine($"ARIM seznam: {contacts.Count} kontaktů, Opicentrum: {opicentrumNames.Count} jmen.");
        report.AppendLine($"✓ Shoda: {exact}");
        AppendSection(report, "≈ Stejná osoba, jiný zápis (diakritika / pořadí / titul) — appka je spáruje", spelledDifferently);
        AppendSection(report, "⚠ Pravděpodobný překlep (stejné příjmení, jiné jméno) — appka je NESPÁRUJE", probableTypos);
        AppendSection(report, "Jen v ARIM seznamu (v Opicentru nenalezeno)", onlyInContacts);
        AppendSection(report, "Jen v Opicentru (chybí v ARIM seznamu)", onlyInOpicentrum);
        return report.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder report, string title, List<string> lines)
    {
        if (lines.Count == 0) return;
        report.AppendLine();
        report.AppendLine($"{title}: {lines.Count}");
        foreach (var line in lines.OrderBy(l => l, StringComparer.Create(Czech, ignoreCase: true)))
            report.AppendLine($"  • {line}");
    }

    private static string StripSuffix(string name)
    {
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        return (dash > 0 ? name[..dash] : name).Trim();
    }

    /// <summary>Same words with the same accents, order ignored — i.e. NamesMatch without its diacritics/title folding.</summary>
    private static bool SameWords(string a, string b)
    {
        static string[] Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLower(Czech)).OrderBy(w => w, StringComparer.Ordinal).ToArray();
        return Words(a).SequenceEqual(Words(b));
    }

    /// <summary>
    /// Probable typo of the same person: two-word names (either order) with one word identical and the
    /// other merely similar — "Batková Elina" vs "Batková Eliška". A shared word alone isn't enough:
    /// two different colleagues called Jana share one too, but their surnames won't look alike.
    /// </summary>
    private static bool LikelySamePerson(string a, string b)
    {
        var left = OpicentrumParsing.NormalizeName(a).Split(' ');
        var right = OpicentrumParsing.NormalizeName(b).Split(' ');
        if (left.Length != 2 || right.Length != 2) return false;

        foreach (var (l0, l1) in new[] { (left[0], left[1]), (left[1], left[0]) })
        {
            if (l0 == right[0] && Similar(l1, right[1])) return true;
            if (l0 == right[1] && Similar(l1, right[0])) return true;
        }
        return false;
    }

    private static bool Similar(string x, string y)
        => (x.Length >= 3 && y.Length >= 3 && string.CompareOrdinal(x, 0, y, 0, 3) == 0) || Levenshtein(x, y) <= 2;

    private static int Levenshtein(string x, string y)
    {
        var previous = Enumerable.Range(0, y.Length + 1).ToArray();
        for (var i = 1; i <= x.Length; i++)
        {
            var current = new int[y.Length + 1];
            current[0] = i;
            for (var j = 1; j <= y.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[y.Length];
    }
}
