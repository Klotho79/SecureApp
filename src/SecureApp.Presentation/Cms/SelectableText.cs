using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Cms;

/// <summary>
/// Text laid out word by word (a wrapping row of one Label per word) so selection is entirely our own
/// layer: there is no system text selection, hence no system "Copy" menu — copying out stays blocked
/// (CMS_PLAN.md, phase 1b). <see cref="Tokens"/> offsets index into <see cref="BlockPlain"/>, the
/// plain text an annotation anchor refers to.
/// </summary>
internal sealed class SelectableText : ContentView
{
    private static readonly Color HighlightColor = Color.FromArgb("#663B82F6");

    private readonly Label[] _labels;

    public string BlockId { get; }
    public string BlockPlain { get; }
    public IReadOnlyList<CmsToken> Tokens { get; }

    public event Action<SelectableText, int>? TokenTapped;

    /// <summary>Vertical padding per word label, as a fraction of the font size — the line spacing of body text (≈1.45). Markers beside the text (list bullets) use the same value so they sit on its first line.</summary>
    public const double LinePadding = 0.12;

    /// <summary>Gap after a word, as a fraction of the font size (≈ the advance of a space in Open Sans).</summary>
    private const double SpaceWidth = 0.27;

    /// <param name="style">Applied to every word label (heading font/colour); null keeps the defaults.</param>
    public SelectableText(string blockId, string blockPlain, IReadOnlyList<CmsTextRun> runs, int baseOffset, double fontSize, bool bold, Action<Label>? style = null)
    {
        BlockId = blockId;
        BlockPlain = blockPlain;
        Tokens = CmsText.Tokenize(runs, baseOffset);

        // '\n' in the text is a hard line break (2026-11-14: the editor keeps line ends now) — one wrapping
        // row per line, stacked. A token carries its surrounding whitespace, so a break can sit before its
        // word (a run starting with '\n') or after it.
        var lines = new VerticalStackLayout();
        var flex = NewRow();
        lines.Children.Add(flex);
        void Break(int count)
        {
            for (var n = 0; n < count; n++)
            {
                if (flex.Children.Count == 0) flex.HeightRequest = fontSize * 0.6; // blank line
                flex = NewRow();
                lines.Children.Add(flex);
            }
        }

        _labels = new Label[Tokens.Count];
        for (var i = 0; i < Tokens.Count; i++)
        {
            var token = Tokens[i];
            var wordStart = token.Text.Length - token.Text.TrimStart().Length;
            var wordEnd = wordStart;
            while (wordEnd < token.Text.Length && !char.IsWhiteSpace(token.Text[wordEnd])) wordEnd++;
            if (token.IsWord) Break(token.Text[..wordStart].Count(c => c == '\n'));

            var attrs = bold || token.Marks.Contains("bold") ? FontAttributes.Bold : FontAttributes.None;
            if (token.Marks.Contains("italic")) attrs |= FontAttributes.Italic;

            // The space after a word is right padding, not text: WinUI drops trailing whitespace when it
            // measures a label, which glued every word together on the PC ("Tojsoualejelita"). Padding
            // also keeps a selection highlight continuous across words.
            var hasSpaceAfter = token.IsWord && wordEnd < token.Text.Length;
            // A space that opens a run ("Dávka:" bold + " 20 mL") belongs to the next word's token.
            var hasSpaceBefore = token.IsWord && wordStart > 0 && flex.Children.Count > 0 && i > 0 && !char.IsWhiteSpace(Tokens[i - 1].Text[^1]);
            var label = new Label
            {
                Text = token.IsWord ? token.Text[wordStart..wordEnd] : string.Empty,
                FontSize = fontSize,
                FontAttributes = attrs,
                LineBreakMode = LineBreakMode.NoWrap,
                Padding = new Thickness(hasSpaceBefore ? fontSize * SpaceWidth : 0, fontSize * LinePadding, hasSpaceAfter ? fontSize * SpaceWidth : 0, fontSize * LinePadding),
                TextDecorations = token.Marks.Contains("underline") ? TextDecorations.Underline : TextDecorations.None
            };
            style?.Invoke(label);
            if (token.IsWord)
            {
                var index = i;
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => TokenTapped?.Invoke(this, index);
                label.GestureRecognizers.Add(tap);
            }
            _labels[i] = label;
            if (label.Text.Length > 0) flex.Children.Add(label);

            Break((token.IsWord ? token.Text[wordEnd..] : token.Text).Count(c => c == '\n'));
        }
        if (flex.Children.Count == 0 && lines.Children.Count > 1) lines.Children.Remove(flex); // trailing break
        Content = lines.Children.Count == 1 ? flex : lines;
    }

    private static FlexLayout NewRow() => new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Direction = Microsoft.Maui.Layouts.FlexDirection.Row };

    private static readonly Color MarkColor = Color.FromArgb("#55FACC15");

    private bool[] _marked = [];
    private int _selFrom = -1;
    private int _selTo = -1;

    /// <summary>Highlights tokens <paramref name="from"/>..<paramref name="to"/> inclusive; pass -1 to clear.</summary>
    public void Highlight(int from, int to)
    {
        _selFrom = from;
        _selTo = to;
        Repaint();
    }

    /// <summary>Marks the text covered by annotations (character ranges into <see cref="BlockPlain"/>), shown in a different tint than the current selection.</summary>
    public void SetMarks(IEnumerable<(int Start, int End)> ranges)
    {
        var marked = new bool[_labels.Length];
        foreach (var (start, end) in ranges)
            for (var i = 0; i < Tokens.Count; i++)
                if (Tokens[i].IsWord && Tokens[i].Start < end && Tokens[i].End > start)
                    marked[i] = true;
        _marked = marked;
        Repaint();
    }

    private void Repaint()
    {
        var lo = Math.Min(_selFrom, _selTo);
        var hi = Math.Max(_selFrom, _selTo);
        for (var i = 0; i < _labels.Length; i++)
        {
            var selected = _selFrom >= 0 && i >= lo && i <= hi;
            _labels[i].BackgroundColor = selected ? HighlightColor : i < _marked.Length && _marked[i] ? MarkColor : Colors.Transparent;
        }
    }
}
