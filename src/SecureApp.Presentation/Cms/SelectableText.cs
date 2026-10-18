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

    public SelectableText(string blockId, string blockPlain, IReadOnlyList<CmsTextRun> runs, int baseOffset, double fontSize, bool bold)
    {
        BlockId = blockId;
        BlockPlain = blockPlain;
        Tokens = CmsText.Tokenize(runs, baseOffset);

        var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Direction = Microsoft.Maui.Layouts.FlexDirection.Row };
        _labels = new Label[Tokens.Count];
        for (var i = 0; i < Tokens.Count; i++)
        {
            var token = Tokens[i];
            var attrs = bold || token.Marks.Contains("bold") ? FontAttributes.Bold : FontAttributes.None;
            if (token.Marks.Contains("italic")) attrs |= FontAttributes.Italic;

            var label = new Label
            {
                Text = token.Text,
                FontSize = fontSize,
                FontAttributes = attrs,
                LineBreakMode = LineBreakMode.NoWrap,
                TextDecorations = token.Marks.Contains("underline") ? TextDecorations.Underline : TextDecorations.None
            };
            if (token.IsWord)
            {
                var index = i;
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) => TokenTapped?.Invoke(this, index);
                label.GestureRecognizers.Add(tap);
            }
            _labels[i] = label;
            flex.Children.Add(label);
        }
        Content = flex;
    }

    /// <summary>Highlights tokens <paramref name="from"/>..<paramref name="to"/> inclusive; pass -1 to clear.</summary>
    public void Highlight(int from, int to)
    {
        var lo = Math.Min(from, to);
        var hi = Math.Max(from, to);
        for (var i = 0; i < _labels.Length; i++)
            _labels[i].BackgroundColor = from >= 0 && i >= lo && i <= hi ? HighlightColor : Colors.Transparent;
    }
}
