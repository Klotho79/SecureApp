using CommunityToolkit.Maui.Views;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Cms;

/// <summary>
/// Native, responsive renderer for a <see cref="CmsDocument"/> (CMS_PLAN.md, phase 1). Layout is driven
/// by the container's width class (compact &lt; 600, medium 600-840, expanded &gt; 840 device-independent
/// units), never by device model: rows collapse to one column when compact, media reserve their height
/// from the stored aspect ratio. <see cref="Zoom"/> is a reflow zoom (font sizes and the readable
/// column width scale; the document data is untouched).
/// </summary>
public sealed class CmsDocumentView : ContentView
{
    private enum WidthClass { Compact, Medium, Expanded }

    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document), typeof(CmsDocument), typeof(CmsDocumentView), null,
        propertyChanged: (b, _, _) => ((CmsDocumentView)b).Rebuild());

    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom), typeof(double), typeof(CmsDocumentView), 1.0,
        coerceValue: (_, v) => Math.Clamp((double)v, 0.5, 2.0),
        propertyChanged: (b, _, _) => ((CmsDocumentView)b).Rebuild());

    public CmsDocument? Document { get => (CmsDocument?)GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public ICmsMediaProvider? MediaProvider { get; set; }

    private const double BaseFontSize = 16;
    private const double MaxColumnWidth = 760;
    private const double PagePadding = 16;
    private const double Unit = 8;

    private readonly VerticalStackLayout _stack = new() { Spacing = Unit * 1.5, HorizontalOptions = LayoutOptions.Center };
    private WidthClass _widthClass = WidthClass.Compact;
    private double _contentWidth;
    private int _buildVersion;

    /// <summary>Raised when the reader asks to annotate the current selection (phase 4 will open the note editor).</summary>
    public event EventHandler<CmsAnchor>? AnnotationRequested;

    /// <summary>Raised when the reader taps an inline image, carrying the local path of its largest stored variant (phase 3, pinch-zoom lives in the host's full-screen viewer, not here).</summary>
    public event EventHandler<string>? ImageTapped;

    private readonly Border _selectionBar = new() { IsVisible = false, VerticalOptions = LayoutOptions.End, Margin = new Thickness(PagePadding), Padding = new Thickness(12, 8), StrokeThickness = 1 };
    private readonly Label _selectionQuote = new() { LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2, VerticalOptions = LayoutOptions.Center };

    // Selection model: tap a word = select it; tap another word in the same text = extend to a range;
    // tap again = start over at that word; tap the single selected word = clear. No drag handles needed.
    private SelectableText? _selectedText;
    private int _anchorToken = -1;
    private int _rangeFrom = -1;
    private int _rangeTo = -1;
    private CmsAnchor? _currentAnchor;

    public CmsDocumentView()
    {
        var addNote = new Button { Text = "💬 Poznámka" };
        addNote.Clicked += (_, _) => { if (_currentAnchor is not null) AnnotationRequested?.Invoke(this, _currentAnchor); };
        var cancel = new Button { Text = "✕" };
        cancel.Clicked += (_, _) => ClearSelection();

        var barGrid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = Unit };
        barGrid.Add(_selectionQuote, 0);
        barGrid.Add(addNote, 1);
        barGrid.Add(cancel, 2);
        _selectionBar.Content = barGrid;
        _selectionBar.SetAppThemeColor(Border.BackgroundColorProperty, Colors.White, Color.FromArgb("#2A2A2E"));
        _selectionBar.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#CCCCCC"), Color.FromArgb("#555555"));

        Content = new Grid
        {
            Children =
            {
                new ScrollView { Content = new Grid { Padding = new Thickness(PagePadding), Children = { _stack } } },
                _selectionBar
            }
        };
    }

    private void OnTokenTapped(SelectableText text, int index)
    {
        if (_selectedText != text || _rangeFrom < 0)
        {
            Select(text, index, index, anchor: index);
            return;
        }

        if (_rangeFrom == _rangeTo && _rangeFrom == index)
        {
            ClearSelection();
            return;
        }

        if (_rangeFrom == _rangeTo)
            Select(text, _anchorToken, index, _anchorToken); // second tap extends the single word to a range
        else
            Select(text, index, index, anchor: index);       // tap after a range starts over
    }

    private void Select(SelectableText text, int from, int to, int anchor)
    {
        _selectedText?.Highlight(-1, -1);
        _selectedText = text;
        _anchorToken = anchor;
        _rangeFrom = Math.Min(from, to);
        _rangeTo = Math.Max(from, to);
        text.Highlight(_rangeFrom, _rangeTo);

        var (start, end) = CmsText.SelectionRange(text.Tokens, text.BlockPlain, _rangeFrom, _rangeTo);
        _currentAnchor = CmsText.BuildAnchor(Document?.VersionId ?? string.Empty, text.BlockId, text.BlockPlain, start, end);
        _selectionQuote.Text = $"„{_currentAnchor.Exact}“";
        _selectionBar.IsVisible = true;
    }

    public void ClearSelection()
    {
        _selectedText?.Highlight(-1, -1);
        _selectedText = null;
        _anchorToken = _rangeFrom = _rangeTo = -1;
        _currentAnchor = null;
        _selectionBar.IsVisible = false;
    }

    private SelectableText BuildSelectable(string blockId, string plain, IReadOnlyList<CmsTextRun> runs, int baseOffset, double fontSize, bool bold)
    {
        var text = new SelectableText(blockId, plain, runs, baseOffset, fontSize, bold);
        text.TokenTapped += OnTokenTapped;
        _texts.Add(text);
        text.SetMarks(MarksFor(blockId));
        return text;
    }

    private readonly List<SelectableText> _texts = [];
    private IReadOnlyList<CmsAnchor> _marks = [];

    private IEnumerable<(int Start, int End)> MarksFor(string blockId) =>
        _marks.Where(a => a.BlockId == blockId).Select(a => (a.Start, a.End));

    /// <summary>Marks the passages that carry annotations (kept across rebuilds, e.g. when the width class changes).</summary>
    public void SetAnnotationMarks(IEnumerable<CmsAnchor> anchors)
    {
        _marks = anchors.ToList();
        foreach (var text in _texts)
            text.SetMarks(MarksFor(text.BlockId));
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;

        var newClass = width < 600 ? WidthClass.Compact : width <= 840 ? WidthClass.Medium : WidthClass.Expanded;
        var newContentWidth = Math.Min(width - 2 * PagePadding, MaxColumnWidth * Zoom);
        if (newClass == _widthClass && Math.Abs(newContentWidth - _contentWidth) < 1) return;

        var classChanged = newClass != _widthClass;
        _widthClass = newClass;
        _contentWidth = newContentWidth;
        _stack.WidthRequest = newContentWidth;
        if (classChanged || _stack.Children.Count == 0) Rebuild();
    }

    private void Rebuild()
    {
        _buildVersion++;
        ClearSelection();
        _stack.Children.Clear();
        _texts.Clear();
        if (Document is null) return;

        _stack.WidthRequest = _contentWidth > 0 ? _contentWidth : -1;
        foreach (var block in Document.Blocks)
            _stack.Children.Add(BuildBlock(block, _buildVersion));
    }

    private View BuildBlock(CmsBlock block, int version) => block switch
    {
        CmsHeadingBlock h => BuildSelectable(h.Id, CmsText.BlockPlainText(h), h.Text, 0, HeadingSize(h.Level), bold: true),
        CmsParagraphBlock p => BuildSelectable(p.Id, CmsText.BlockPlainText(p), p.Text, 0, BaseFontSize * Zoom, bold: false),
        CmsListBlock l => BuildList(l),
        CmsImageBlock i => BuildImage(i, version),
        CmsVideoBlock v => BuildVideo(v, version),
        CmsRowBlock r => BuildRow(r, version),
        _ => new Label { Text = string.Empty }
    };

    private static double HeadingScale(int level) => level switch { 1 => 1.75, 2 => 1.4, 3 => 1.2, _ => 1.05 };

    private double HeadingSize(int level) => BaseFontSize * Zoom * HeadingScale(level);

    private static Label BuildText(IReadOnlyList<CmsTextRun> runs, double fontSize, bool bold)
    {
        var formatted = new FormattedString();
        foreach (var run in runs)
        {
            var span = new Span { Text = run.T };
            var marks = run.Marks ?? [];
            var attrs = FontAttributes.None;
            if (bold || marks.Contains("bold")) attrs |= FontAttributes.Bold;
            if (marks.Contains("italic")) attrs |= FontAttributes.Italic;
            span.FontAttributes = attrs;
            if (marks.Contains("underline")) span.TextDecorations = TextDecorations.Underline;
            formatted.Spans.Add(span);
        }

        return new Label { FormattedText = formatted, FontSize = fontSize, LineBreakMode = LineBreakMode.WordWrap };
    }

    private View BuildList(CmsListBlock list)
    {
        var layout = new VerticalStackLayout { Spacing = Unit / 2 };
        var size = BaseFontSize * Zoom;
        var plain = CmsText.BlockPlainText(list);
        for (var i = 0; i < list.Items.Count; i++)
        {
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = Unit };
            row.Add(new Label { Text = list.Ordered ? $"{i + 1}." : "•", FontSize = size, VerticalOptions = LayoutOptions.Start }, 0);
            row.Add(BuildSelectable(list.Id, plain, list.Items[i], CmsText.ListItemOffset(list, i), size, bold: false), 1);
            layout.Children.Add(row);
        }
        return layout;
    }

    private View BuildImage(CmsImageBlock block, int version)
    {
        var image = new Image { Aspect = block.Fit == "cover" ? Aspect.AspectFill : Aspect.AspectFit };
        var box = ReserveAspect(image, block.AspectRatio);

        if (Document?.Media.TryGetValue(block.MediaId, out var media) == true && MediaProvider is { } provider)
        {
            var targetPx = (int)(_contentWidth * Microsoft.Maui.Devices.DeviceDisplay.Current.MainDisplayInfo.Density);
            _ = LoadAsync(async () =>
            {
                var path = await provider.GetImagePathAsync(media, targetPx);
                if (path is not null && version == _buildVersion)
                    MainThread.BeginInvokeOnMainThread(() => image.Source = ImageSource.FromFile(path));
            });

            // Tap to open full-screen (phase 3): always the LARGEST stored variant, regardless of
            // what's currently shown inline at this column width — a target width no real screen
            // reaches guarantees CmsImageProcessor's biggest variant (1800px) comes back.
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _ = LoadAsync(async () =>
            {
                var fullPath = await provider.GetImagePathAsync(media, 4000);
                if (fullPath is not null) MainThread.BeginInvokeOnMainThread(() => ImageTapped?.Invoke(this, fullPath));
            });
            image.GestureRecognizers.Add(tap);
        }

        View content = box;
        if (block.MaxWidthPct is > 0 and < 100)
        {
            var pct = block.MaxWidthPct;
            var grid = new Grid();
            switch (block.Align)
            {
                case "left":
                    grid.ColumnDefinitions = [new(new GridLength(pct, GridUnitType.Star)), new(new GridLength(100 - pct, GridUnitType.Star))];
                    grid.Add(box, 0);
                    break;
                case "right":
                    grid.ColumnDefinitions = [new(new GridLength(100 - pct, GridUnitType.Star)), new(new GridLength(pct, GridUnitType.Star))];
                    grid.Add(box, 1);
                    break;
                default:
                    var side = (100 - pct) / 2.0;
                    grid.ColumnDefinitions = [new(new GridLength(side, GridUnitType.Star)), new(new GridLength(pct, GridUnitType.Star)), new(new GridLength(side, GridUnitType.Star))];
                    grid.Add(box, 1);
                    break;
            }
            content = grid;
        }

        if (block.Caption is not { Count: > 0 }) return content;

        var caption = BuildText(block.Caption, BaseFontSize * Zoom * 0.85, bold: false);
        caption.FontAttributes = FontAttributes.Italic;
        caption.HorizontalTextAlignment = TextAlignment.Center;
        return new VerticalStackLayout { Spacing = Unit / 2, Children = { content, caption } };
    }

    private View BuildVideo(CmsVideoBlock block, int version)
    {
        // The poster shows immediately (it's a small decrypted JPEG, same provider path as any image);
        // the actual video — potentially a large file — is only fetched once the reader taps play, so
        // just scrolling past a video never spends bandwidth on it.
        var player = new MediaElement { ShouldAutoPlay = false, ShouldShowPlaybackControls = true, Aspect = Aspect.AspectFit, IsVisible = false };
        var poster = new Image { Aspect = Aspect.AspectFit, BackgroundColor = Color.FromArgb("#111111") };
        var playIcon = new Label { Text = "▶", FontSize = 36, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var overlay = new Grid { Children = { poster, playIcon } };
        var box = ReserveAspect(new Grid { Children = { player, overlay } }, block.AspectRatio);

        var loaded = false;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => _ = LoadAsync(async () =>
        {
            if (loaded || Document?.Media.TryGetValue(block.MediaId, out var media) != true || media is null || MediaProvider is not { } provider) return;
            loaded = true;
            var path = await provider.GetVideoPathAsync(media);
            if (path is null || version != _buildVersion) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                player.Source = MediaSource.FromFile(path);
                player.IsVisible = true;
                overlay.IsVisible = false;
                player.Play();
            });
        });
        overlay.GestureRecognizers.Add(tap);

        if (block.PosterMediaId is { } posterId && Document?.Media.TryGetValue(posterId, out var posterMedia) == true && MediaProvider is { } posterProvider)
        {
            _ = LoadAsync(async () =>
            {
                var targetPx = (int)(_contentWidth * Microsoft.Maui.Devices.DeviceDisplay.Current.MainDisplayInfo.Density);
                var path = await posterProvider.GetImagePathAsync(posterMedia, targetPx);
                if (path is not null && version == _buildVersion)
                    MainThread.BeginInvokeOnMainThread(() => poster.Source = ImageSource.FromFile(path));
            });
        }

        return box;
    }

    /// <summary>Wraps <paramref name="inner"/> in a box whose height always follows its width via <paramref name="aspectRatio"/> (width / height) — no fixed pixel heights, no layout shift on load.</summary>
    private static Border ReserveAspect(View inner, double aspectRatio)
    {
        var ratio = aspectRatio > 0 ? aspectRatio : 16.0 / 9;
        var box = new Border { StrokeThickness = 0, Padding = 0, Content = inner, HorizontalOptions = LayoutOptions.Fill };
        box.SizeChanged += (_, _) =>
        {
            if (box.Width <= 0) return;
            var h = box.Width / ratio;
            if (Math.Abs(box.HeightRequest - h) > 0.5) box.HeightRequest = h;
        };
        return box;
    }

    private View BuildRow(CmsRowBlock row, int version)
    {
        var gap = row.Gap * Unit;

        if (_widthClass == WidthClass.Compact)
        {
            var column = new VerticalStackLayout { Spacing = Unit * 1.5 };
            foreach (var cell in row.Children)
                foreach (var block in cell.Blocks)
                    column.Children.Add(BuildBlock(block, version));
            return column;
        }

        var grid = new Grid { ColumnSpacing = gap, RowSpacing = gap };
        for (var c = 0; c < 12; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        int gridRow = 0, col = 0;
        foreach (var cell in row.Children)
        {
            var span = Math.Clamp(_widthClass == WidthClass.Medium ? cell.Span.Medium : cell.Span.Expanded, 1, 12);
            if (col + span > 12)
            {
                gridRow++;
                col = 0;
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            var content = new VerticalStackLayout { Spacing = Unit * 1.5, VerticalOptions = LayoutOptions.Start };
            foreach (var block in cell.Blocks)
                content.Children.Add(BuildBlock(block, version));

            grid.Add(content, col, gridRow);
            Grid.SetColumnSpan(content, span);
            col += span;
        }
        return grid;
    }

    private static async Task LoadAsync(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) { SecureApp.Presentation.Infrastructure.AppLog.Error("CmsDocumentView.Media", "media load failed", ex); }
    }
}
