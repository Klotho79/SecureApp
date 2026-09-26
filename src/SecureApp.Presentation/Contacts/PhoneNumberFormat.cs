namespace SecureApp.Presentation.Contacts;

public enum PhoneKind { Extension, Mobile, Landline }

public sealed record PhoneNumberPart(PhoneKind Kind, string Display, string? Dial);

/// <summary>
/// Tells an internal extension apart from a complete phone number (2026-09-26, user's ask: a complete
/// number must not read "kl.", mobile vs landline should be distinguishable, and complete numbers must
/// be callable). A field may hold several numbers separated by "," or ";".
/// </summary>
public static class PhoneNumberFormat
{
    public static IReadOnlyList<PhoneNumberPart> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        return raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParsePart)
            .ToList();
    }

    /// <summary>"mob. 777 123 456 · kl. 2280".</summary>
    public static string Describe(string? raw)
        => string.Join(" · ", Parse(raw).Select(p => p.Kind switch
        {
            PhoneKind.Mobile => $"mob. {p.Display}",
            PhoneKind.Landline => $"tel. {p.Display}",
            _ => $"kl. {p.Display}",
        }));

    /// <summary>The first complete number in the field, dial-ready ("+420777123456"), or null if it only holds extensions.</summary>
    public static string? FirstDialable(string? raw) => Parse(raw).FirstOrDefault(p => p.Dial is not null)?.Dial;

    private static PhoneNumberPart ParsePart(string part)
    {
        var hasPlus = part.StartsWith('+');
        var digits = new string(part.Where(char.IsDigit).ToArray());

        // A range ("6604-6605") or anything short is an internal extension.
        if ((part.Contains('-') && digits.Length <= 10) || digits.Length <= 5)
            return new PhoneNumberPart(PhoneKind.Extension, part, null);

        var national = digits;
        if (hasPlus && digits.StartsWith("420")) national = digits[3..];
        else if (digits.StartsWith("00420")) national = digits[5..];
        else if (hasPlus || digits.StartsWith("00"))
            return new PhoneNumberPart(PhoneKind.Landline, part, "+" + digits.TrimStart('0'));

        if (national.Length == 9)
        {
            var kind = national[0] is '6' or '7' ? PhoneKind.Mobile : PhoneKind.Landline;
            return new PhoneNumberPart(kind, $"{national[..3]} {national[3..6]} {national[6..]}", "+420" + national);
        }

        // 6–8 digits (or other odd lengths): not recognisable as a complete Czech number.
        return new PhoneNumberPart(PhoneKind.Extension, part, null);
    }
}
