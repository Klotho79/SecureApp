namespace SecureApp.Domain.ValueObjects;

/// <summary>A selectable unit: a word with its leading/trailing whitespace. Offsets index into the block's plain text.</summary>
public readonly record struct CmsToken(string Text, int Start, int End, IReadOnlyList<string> Marks)
{
    public bool IsWord => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>Pure text helpers behind selection and annotation anchoring (CMS_PLAN.md, phase 1b).</summary>
public static class CmsText
{
    private const int ContextLength = 32;

    public static string PlainText(IReadOnlyList<CmsTextRun> runs) => string.Concat(runs.Select(r => r.T));

    /// <summary>Plain text an annotation anchor refers to. List items are joined with '\n'.</summary>
    public static string BlockPlainText(CmsBlock block) => block switch
    {
        CmsHeadingBlock h => PlainText(h.Text),
        CmsParagraphBlock p => PlainText(p.Text),
        CmsListBlock l => string.Join('\n', l.Items.Select(PlainText)),
        _ => string.Empty
    };

    /// <summary>Offset of list item <paramref name="itemIndex"/> inside <see cref="BlockPlainText"/> of its list.</summary>
    public static int ListItemOffset(CmsListBlock list, int itemIndex)
    {
        var offset = 0;
        for (var i = 0; i < itemIndex; i++)
            offset += PlainText(list.Items[i]).Length + 1;
        return offset;
    }

    /// <summary>Splits runs into word tokens (each carries the whitespace around it); a word spanning two runs with different marks becomes two tokens.</summary>
    public static IReadOnlyList<CmsToken> Tokenize(IReadOnlyList<CmsTextRun> runs, int baseOffset = 0)
    {
        var tokens = new List<CmsToken>();
        var offset = baseOffset;
        foreach (var run in runs)
        {
            var text = run.T;
            var marks = run.Marks ?? [];
            var i = 0;
            while (i < text.Length)
            {
                var begin = i;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                tokens.Add(new CmsToken(text[begin..i], offset + begin, offset + i, marks));
            }
            offset += text.Length;
        }
        return tokens;
    }

    /// <summary>Selected range covering tokens <paramref name="from"/>..<paramref name="to"/> (inclusive), trimmed of surrounding whitespace.</summary>
    public static (int Start, int End) SelectionRange(IReadOnlyList<CmsToken> tokens, string plain, int from, int to)
    {
        var start = tokens[Math.Min(from, to)].Start;
        var end = tokens[Math.Max(from, to)].End;
        while (start < end && char.IsWhiteSpace(plain[start])) start++;
        while (end > start && char.IsWhiteSpace(plain[end - 1])) end--;
        return (start, end);
    }

    public static CmsAnchor BuildAnchor(string versionId, string blockId, string plain, int start, int end)
    {
        var ctxStart = Math.Max(0, start - ContextLength);
        var ctxEnd = Math.Min(plain.Length, end + ContextLength);
        return new CmsAnchor(versionId, blockId, plain[start..end], plain[ctxStart..start], plain[end..ctxEnd], start, end);
    }
}
