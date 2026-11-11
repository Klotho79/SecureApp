using System.Globalization;
using System.Text.RegularExpressions;

namespace SecureApp.Presentation.HomeAi;

/// <summary>
/// After an answer about an attached document: every number the model wrote is looked up in the sources (the document, the
/// program's exact results, the question). A number found nowhere is shown to the user, because the model either calculated
/// it on its own or made it up (2026-11-11, user: more precise answers). It does not make the model better, it makes a
/// wrong number visible.
/// </summary>
internal static partial class AnswerNumberCheck
{
    /// <summary>A grey note for the chat, or null when the answer has no numbers to check.</summary>
    public static string? Check(string answer, IEnumerable<string?> sources)
    {
        var known = new HashSet<double>();
        foreach (var source in sources)
        {
            if (string.IsNullOrEmpty(source))
                continue;
            foreach (var n in SourceNumbers(source))
                known.Add(n);
        }

        var checkedCount = 0;
        var missing = new List<string>();
        foreach (var (text, value, decimals) in Numbers(ListMarker().Replace(answer, string.Empty)))
        {
            checkedCount++;
            if (known.Contains(value))
                continue;
            // "12,35" for a source value 12.3456 is a rounding, not an invention; integers must match exactly.
            if (decimals > 0 && known.Any(k => Math.Abs(Math.Round(k, decimals, MidpointRounding.AwayFromZero) - value) < 1e-9))
                continue;
            if (!missing.Contains(text))
                missing.Add(text);
        }

        if (checkedCount == 0)
            return null;
        if (missing.Count == 0)
            return checkedCount == 1
                ? "✓ Kontrola čísel: číslo v odpovědi je v podkladech."
                : $"✓ Kontrola čísel: všech {checkedCount} čísel v odpovědi je v podkladech.";

        var shown = string.Join(", ", missing.Take(10)) + (missing.Count > 10 ? $" a dalších {missing.Count - 10}" : string.Empty);
        return $"⚠ Kontrola čísel: v podkladech jsem nenašel {shown}. Model je mohl sám dopočítat nebo vymyslet — ověřte je.";
    }

    /// <summary>Plain numbers in the text ("12", "12,5", "1 234"); codes and references like C50.1, Ř12, 2024-2025 or 5.3.2026 are skipped.</summary>
    internal static IEnumerable<(string Text, double Value, int Decimals)> Numbers(string text)
    {
        var joined = ThousandsSpace().Replace(text, "$1"); // "1 234" → "1234"
        foreach (Match token in Token().Matches(joined))
        {
            var t = token.Value.TrimEnd('.', ',', ':');
            if (!PlainNumber().IsMatch(t))
                continue;
            var decimals = t.IndexOfAny(['.', ',']) is var sep and >= 0 ? t.Length - sep - 1 : 0;
            if (double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                yield return (t, value, decimals);
        }
    }

    /// <summary>Every run of digits in a source (also inside Ř12, C50.1, dates): the sources are read generously, only the answer strictly.</summary>
    internal static IEnumerable<double> SourceNumbers(string text)
    {
        foreach (Match m in DigitRun().Matches(ThousandsSpace().Replace(text, "$1")))
        {
            if (double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                yield return value;
            // "12,5" also counts as the 12 and 5 a sloppier answer might quote.
            if (m.Value.IndexOfAny(['.', ',']) >= 0)
                foreach (var part in m.Value.Split('.', ','))
                    if (double.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                        yield return p;
        }
    }

    [GeneratedRegex(@"(\d)[   ](?=\d{3}(?!\d))")]
    private static partial Regex ThousandsSpace();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}.,:/\-]*")]
    private static partial Regex Token();

    [GeneratedRegex(@"^\d+(?:[.,]\d+)?$")]
    private static partial Regex PlainNumber();

    /// <summary>"1. ", "2) " at the start of a line: list numbering, not data.</summary>
    [GeneratedRegex(@"^\s*\d{1,2}[.)]\s", RegexOptions.Multiline)]
    private static partial Regex ListMarker();
}
