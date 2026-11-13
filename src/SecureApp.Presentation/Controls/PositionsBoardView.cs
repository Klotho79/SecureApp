using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;
using Font = Microsoft.Maui.Graphics.Font;

namespace SecureApp.Presentation.Controls;

/// <summary>Which part of the position board a <see cref="PositionsBoardView"/> instance draws — same split as <see cref="VolnoBoardLayer"/>.</summary>
public enum PositionsBoardLayer
{
    /// <summary>The day cells with their person chips (scrolls natively inside the page's ScrollView; takes the taps).</summary>
    Body,
    /// <summary>The workplace-name column, kept pinned on the left by translating it vertically with the scroll.</summary>
    Names,
    /// <summary>The day header, kept pinned on top by translating it horizontally with the scroll.</summary>
    Header,
}

/// <summary>
/// The "Pozice" roster board drawn on a canvas (2026-10-24, user: opening it from Nástěnka took ~1 s every
/// time, while switching Pozice/Volno inside the page was instant). The page is rebuilt on every open and
/// the old version created ~1 300 views for it (a Border + FlexLayout per cell, a Border + Label per
/// person chip), which is what took the time — the 90 ms the metrics showed was only the data hand-over,
/// the native views are created afterwards. One canvas draws the same board in a few milliseconds.
///
/// Same three-layer structure as <see cref="VolnoBoardView"/>: the body scrolls natively, the names and
/// header overlays are translated by the page's code-behind. The body takes taps through a
/// TapGestureRecognizer (not GraphicsView's own touch events, which would compete with the ScrollView's
/// pan) and hit-tests them against the chip rectangles from <see cref="PositionsLayout"/>.
/// </summary>
public sealed class PositionsBoardView : GraphicsView
{
    public static readonly BindableProperty LayerProperty = BindableProperty.Create(
        nameof(Layer), typeof(PositionsBoardLayer), typeof(PositionsBoardView), PositionsBoardLayer.Body, propertyChanged: OnDataChanged);

    public static readonly BindableProperty RowsProperty = BindableProperty.Create(
        nameof(Rows), typeof(IReadOnlyList<OpicentrumRosterRowItem>), typeof(PositionsBoardView), null, propertyChanged: OnDataChanged);

    public static readonly BindableProperty DayHeadersProperty = BindableProperty.Create(
        nameof(DayHeaders), typeof(IReadOnlyList<string>), typeof(PositionsBoardView), null, propertyChanged: OnDataChanged);

    public static readonly BindableProperty MaxNamesWidthProperty = BindableProperty.Create(
        nameof(MaxNamesWidth), typeof(float), typeof(PositionsBoardView), float.PositiveInfinity, propertyChanged: OnDataChanged);

    public PositionsBoardLayer Layer
    {
        get => (PositionsBoardLayer)GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    public IReadOnlyList<OpicentrumRosterRowItem>? Rows
    {
        get => (IReadOnlyList<OpicentrumRosterRowItem>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public IReadOnlyList<string>? DayHeaders
    {
        get => (IReadOnlyList<string>?)GetValue(DayHeadersProperty);
        set => SetValue(DayHeadersProperty, value);
    }

    /// <summary>
    /// Largest pixel width the pinned room-name column may take (2026-11-12, user: "sloupec se saly... max do 1/5
    /// sirky displeje"); past it, further pinch-zoom only makes the rows taller, not the column wider.
    /// <see cref="float.PositiveInfinity"/> (the default) means no cap.
    /// </summary>
    public float MaxNamesWidth
    {
        get => (float)GetValue(MaxNamesWidthProperty);
        set => SetValue(MaxNamesWidthProperty, value);
    }

    /// <summary>The room-name column's actual on-screen width: zoomed normally, but capped at <paramref name="maxNamesWidth"/>.</summary>
    internal static float CappedNamesWidth(float cellsLeft, float zoom, float maxNamesWidth) => Math.Min(cellsLeft * zoom, maxNamesWidth);

    /// <summary>Raised on the body layer when a person chip is tapped.</summary>
    public event EventHandler<OpicentrumRosterPersonChip>? ChipTapped;

    /// <summary>Raised by the body layer after it has drawn a new data set for the first time — the page uses it to measure the real time to pixels.</summary>
    public event EventHandler? Drawn;

    private PositionsLayout? _layout;
    private bool _drawLogged;

    public PositionsBoardView()
    {
        Drawable = new BoardDrawable(this);
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    private static void OnDataChanged(BindableObject bindable, object oldValue, object newValue) => ((PositionsBoardView)bindable).Refresh();

    private void Refresh()
    {
        var dayCount = DayHeaders?.Count ?? 0;

        // All three layers need the layout now: the name column's width follows the longest workplace name.
        _layout = Rows is null ? null : PositionsLayout.For(Rows);
        var cellsLeft = _layout?.CellsLeft ?? PositionsLayout.DefaultNameWidth + PositionsLayout.Gap;
        var totalWidth = cellsLeft + dayCount * (PositionsLayout.ColWidth + PositionsLayout.Gap);
        var totalHeight = _layout?.Height ?? PositionsLayout.RowsTop;
        var zoom = BoardZoom.Value;
        var namesPixels = CappedNamesWidth(cellsLeft, zoom, MaxNamesWidth);
        var saved = cellsLeft * zoom - namesPixels; // >= 0: how much narrower the capped name column is than plain zoom

        switch (Layer)
        {
            case PositionsBoardLayer.Body:
                WidthRequest = totalWidth * zoom - saved;
                HeightRequest = totalHeight * zoom;
                break;
            case PositionsBoardLayer.Names:
                WidthRequest = namesPixels;
                HeightRequest = totalHeight * zoom;
                break;
            case PositionsBoardLayer.Header:
                WidthRequest = totalWidth * zoom - saved;
                HeightRequest = PositionsLayout.HeaderHeight * zoom;
                break;
        }

        _drawLogged = false;
        Invalidate();
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Layer != PositionsBoardLayer.Body || _layout is null || e.GetPosition(this) is not { } point) return;

        // A little slack around the chip: it is only ~20 dp tall.
        const float slack = 3;
        var zoom = BoardZoom.Value;
        foreach (var chip in _layout.Chips)
        {
            if (chip.Rect.Inflate(slack, slack).Contains((float)point.X / zoom, (float)point.Y / zoom))
            {
                ChipTapped?.Invoke(this, chip.Person);
                return;
            }
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Application.Current is not { } app) return;
        app.RequestedThemeChanged -= OnThemeChanged;
        BoardZoom.Changed -= Refresh;
        if (Handler is not null)
        {
            app.RequestedThemeChanged += OnThemeChanged;
            BoardZoom.Changed += Refresh;
        }
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    private sealed class BoardDrawable(PositionsBoardView owner) : IDrawable
    {
        private static readonly Color DutyChipBackground = Color.FromArgb("#A65F5F");

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var sw = Stopwatch.StartNew();
            var zoom = BoardZoom.Value;
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var pageBackground = dark ? BoardCanvas.Resource("Gray950", Colors.Black) : BoardCanvas.Resource("White", Colors.White);
            var cardStroke = dark ? BoardCanvas.Resource("Gray600", Colors.DimGray) : BoardCanvas.Resource("Gray100", Colors.LightGray);
            var chipBackground = dark ? BoardCanvas.Resource("SurfaceAltDark", Colors.DimGray) : BoardCanvas.Resource("SurfaceAlt", Colors.LightGray);
            var text = dark ? Colors.White : Colors.Black;

            switch (owner.Layer)
            {
                case PositionsBoardLayer.Body when owner._layout is { } layout:
                    // Day columns keep the plain zoom; shifted left by however much the name column was capped,
                    // so they start right after it instead of leaving a gap (see MaxNamesWidth's remarks).
                    canvas.Scale(zoom, zoom);
                    canvas.Translate(TranslateX(layout.CellsLeft, zoom), 0);
                    DrawBody(canvas, layout, pageBackground, cardStroke, chipBackground, text);
                    break;
                case PositionsBoardLayer.Names when owner._layout is { } layout && owner.Rows is { } rows:
                    // Past the cap, the column's width stops growing with zoom (scaleX saturates) while its rows
                    // still get taller (scaleY keeps following zoom) — "roztahovat jen do vysky".
                    var namesScaleX = CappedNamesWidth(layout.CellsLeft, zoom, owner.MaxNamesWidth) / layout.CellsLeft;
                    canvas.Scale(namesScaleX, zoom);
                    DrawNames(canvas, layout, rows, pageBackground, text);
                    break;
                case PositionsBoardLayer.Header when owner.DayHeaders is { } days:
                    var cellsLeft = owner._layout?.CellsLeft ?? PositionsLayout.DefaultNameWidth + PositionsLayout.Gap;
                    canvas.Scale(zoom, zoom);
                    canvas.Translate(TranslateX(cellsLeft, zoom), 0);
                    DrawHeader(canvas, days, cellsLeft, pageBackground, text);
                    break;
            }

            if (!owner._drawLogged && owner.Layer == PositionsBoardLayer.Body && owner._layout is { } drawn)
            {
                owner._drawLogged = true;
                AppLog.Metric("opicentrum.positions.draw", sw.Elapsed.TotalMilliseconds, "ms", ("cells", drawn.Cells.Count), ("chips", drawn.Chips.Count));
                owner.Drawn?.Invoke(owner, EventArgs.Empty);
            }
        }

        /// <summary>Logical-unit shift (applied after the zoom Scale, so it lands in already-scaled pixels) that moves
        /// the day columns left to start right after the capped name column instead of the plain-zoom one.</summary>
        private float TranslateX(float cellsLeft, float zoom) =>
            (CappedNamesWidth(cellsLeft, zoom, owner.MaxNamesWidth) - cellsLeft * zoom) / zoom;

        private static void DrawBody(ICanvas canvas, PositionsLayout layout, Color pageBackground, Color cardStroke, Color chipBackground, Color text)
        {
            canvas.StrokeSize = 1;
            foreach (var cell in layout.Cells)
            {
                canvas.FillColor = pageBackground;
                canvas.FillRoundedRectangle(cell, PositionsLayout.CellRadius);
                canvas.StrokeColor = cardStroke;
                canvas.DrawRoundedRectangle(cell.Inflate(-0.5f, -0.5f), PositionsLayout.CellRadius);
            }

            canvas.Font = Font.Default;
            canvas.FontSize = PositionsLayout.ChipFont;
            canvas.FontColor = text;
            foreach (var chip in layout.Chips)
            {
                // On duty (slouží): a muted red chip with white text, same hue as the duty colour of the widget
                // calendar; the duty slot sits on its own line under the name instead of crowding one line together
                // (2026-11-12, user: "sluzby zalomit pod jmeno").
                var onDuty = chip.Person.DutyPosition is not null;
                canvas.FillColor = onDuty ? DutyChipBackground : chipBackground;
                canvas.FontColor = onDuty ? Colors.White : text;
                canvas.FillRoundedRectangle(chip.Rect, PositionsLayout.ChipRadius);
                var lineHeight = chip.Rect.Height / chip.Lines.Length;
                for (var i = 0; i < chip.Lines.Length; i++)
                    canvas.DrawString(chip.Lines[i], chip.Rect.X + PositionsLayout.ChipPadX, chip.Rect.Y + i * lineHeight, chip.Rect.Width - 2 * PositionsLayout.ChipPadX, lineHeight,
                        HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        private static void DrawNames(ICanvas canvas, PositionsLayout layout, IReadOnlyList<OpicentrumRosterRowItem> rows, Color pageBackground, Color text)
        {
            canvas.FillColor = pageBackground;
            canvas.FillRectangle(0, 0, layout.CellsLeft, layout.Height);

            canvas.Font = Font.DefaultBold;
            canvas.FontSize = PositionsLayout.NameFont;
            canvas.FontColor = text;
            // A long workplace name ("Anest. ambulance") wraps onto several lines instead of forcing a wide name column.
            for (var r = 0; r < rows.Count; r++)
            {
                var lines = layout.NameLines[r];
                var top = layout.RowTops[r] + (layout.RowHeights[r] - lines.Length * layout.NameLineHeight) / 2;
                for (var i = 0; i < lines.Length; i++)
                    canvas.DrawString(lines[i], 0, top + i * layout.NameLineHeight, layout.NameWidth, layout.NameLineHeight, HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        private static void DrawHeader(ICanvas canvas, IReadOnlyList<string> days, float cellsLeft, Color pageBackground, Color text)
        {
            canvas.FillColor = pageBackground;
            canvas.FillRectangle(0, 0, cellsLeft + days.Count * (PositionsLayout.ColWidth + PositionsLayout.Gap), PositionsLayout.HeaderHeight);

            canvas.Font = Font.DefaultBold;
            canvas.FontSize = PositionsLayout.NameFont;
            canvas.FontColor = text;
            for (var d = 0; d < days.Count; d++)
                canvas.DrawString(days[d], cellsLeft + d * (PositionsLayout.ColWidth + PositionsLayout.Gap), 0, PositionsLayout.ColWidth, PositionsLayout.HeaderHeight,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}

/// <summary>
/// Where everything on the position board sits, computed once per data set (a new rows collection means a
/// new layout; the three layers of the board share it). Reproduces what the view-based board got from
/// MAUI: a card per cell, person chips wrapped left to right like the FlexLayout did, every cell of a row
/// as tall as the row's tallest one.
/// </summary>
internal sealed class PositionsLayout
{
    /// <summary>Width of the name column when there is nothing to measure yet; the real one follows the workplace names (see <see cref="NameWidth"/>).</summary>
    public const float DefaultNameWidth = 84;
    private const float MinNameWidth = 44;
    private const float MaxNameWidth = 130;
    private const int NameMaxLines = 2;
    public const float ColWidth = 140;
    public const float Gap = 2;
    public const float HeaderHeight = 20;
    public const float RowsTop = HeaderHeight + Gap;

    /// <summary>
    /// Dynamic name column (2026-11-07, user: "sirka by mela byt dynamicka"): the narrowest width at which every
    /// workplace name fits on <see cref="NameMaxLines"/> lines, between <see cref="MinNameWidth"/> and <see cref="MaxNameWidth"/>.
    /// </summary>
    public required float NameWidth { get; init; }
    public float CellsLeft => NameWidth + Gap;

    public const float CellPadding = 4;
    public const float CellStroke = 1;
    public const float CellRadius = 12;
    public const float ChipPadX = 5;
    public const float ChipPadY = 3;
    public const float ChipMargin = 3;
    public const float ChipRadius = 6;
    public const float ChipFont = 9;
    public const float NameFont = 10;
    private const float MinRowHeight = 18;

    /// <summary><see cref="Lines"/> is one line for a plain name chip, two (name, then duty slot) for an on-duty one.</summary>
    public sealed record Chip(RectF Rect, string[] Lines, OpicentrumRosterPersonChip Person);

    public required float[] RowTops { get; init; }
    public required float[] RowHeights { get; init; }
    /// <summary>The workplace name of each row, word-wrapped to <see cref="NameWidth"/>.</summary>
    public required string[][] NameLines { get; init; }
    public required float NameLineHeight { get; init; }
    public required List<RectF> Cells { get; init; }
    public required List<Chip> Chips { get; init; }
    public required float Height { get; init; }

    private static readonly ConditionalWeakTable<object, PositionsLayout> Cache = new();

    public static PositionsLayout For(IReadOnlyList<OpicentrumRosterRowItem> rows) =>
        Cache.GetValue(rows, _ => Build(rows));

    private static PositionsLayout Build(IReadOnlyList<OpicentrumRosterRowItem> rows)
    {
        var sw = Stopwatch.StartNew();
        var sizes = new PlatformStringSizeService();
        float Width(string s) => sizes.GetStringSize(s, Font.Default, ChipFont).Width;

        var textHeight = sizes.GetStringSize("Ag", Font.Default, ChipFont).Height;
        var cellInset = CellPadding + CellStroke;
        var inner = ColWidth - 2 * cellInset;

        var nameLineHeight = sizes.GetStringSize("Ag", Font.DefaultBold, NameFont).Height + 1;
        float NameWidthOf(string s) => sizes.GetStringSize(s, Font.DefaultBold, NameFont).Width;
        var nameLines = new string[rows.Count][];
        var nameWidth = ComputeNameWidth(rows, NameWidthOf);
        var cellsLeft = nameWidth + Gap;

        var rowTops = new float[rows.Count];
        var rowHeights = new float[rows.Count];
        var cells = new List<RectF>();
        var chips = new List<Chip>();

        var y = RowsTop;
        for (var r = 0; r < rows.Count; r++)
        {
            // First pass: wrap each cell's chips relative to the cell's content origin and find the tallest cell.
            // A person on duty gets a two-line chip (name, then the duty slot on its own line, kvůli přehlednosti —
            // 2026-11-12) which takes the cell's whole width; plain name chips stay one line and flow side by side.
            var placed = new List<(float X, float Y, float W, string[] Lines, OpicentrumRosterPersonChip Person)>[rows[r].Cells.Count];
            var tallest = MinRowHeight;
            for (var c = 0; c < rows[r].Cells.Count; c++)
            {
                var items = new List<(float W, string[] Lines, OpicentrumRosterPersonChip Person)>();
                foreach (var person in rows[r].Cells[c].People)
                {
                    if (person.DutyPosition is { Length: > 0 } slot)
                    {
                        var nameLine = BoardCanvas.Fit(person.PersonName, inner - 2 * ChipPadX - ChipMargin, Width);
                        var slotLine = BoardCanvas.Fit(slot, inner - 2 * ChipPadX - ChipMargin, Width);
                        items.Add((inner - ChipMargin, [nameLine, slotLine], person));
                    }
                    else
                    {
                        var text = BoardCanvas.Fit(person.PersonName, inner - 2 * ChipPadX - ChipMargin, Width);
                        items.Add((Math.Min(Width(text) + 2 * ChipPadX, inner - ChipMargin), [text], person));
                    }
                }

                // Flex-wrap left to right; a line's height follows its tallest chip (a duty chip's two lines).
                var lineOf = new int[items.Count];
                float x = 0;
                var lineCount = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (x > 0 && x + items[i].W + ChipMargin > inner) { lineCount++; x = 0; }
                    lineOf[i] = lineCount;
                    x += items[i].W + ChipMargin;
                }
                if (items.Count > 0) lineCount++;

                var lineHeights = new float[lineCount];
                for (var i = 0; i < items.Count; i++)
                    lineHeights[lineOf[i]] = Math.Max(lineHeights[lineOf[i]], items[i].Lines.Length * textHeight + 2 * ChipPadY);
                var lineTops = new float[lineCount];
                for (var l = 1; l < lineCount; l++)
                    lineTops[l] = lineTops[l - 1] + lineHeights[l - 1] + ChipMargin;

                placed[c] = [];
                x = 0;
                var curLine = 0;
                for (var i = 0; i < items.Count; i++)
                {
                    if (lineOf[i] != curLine) { curLine = lineOf[i]; x = 0; }
                    placed[c].Add((x, lineTops[curLine], items[i].W, items[i].Lines, items[i].Person));
                    x += items[i].W + ChipMargin;
                }

                var cellHeight = lineCount == 0 ? 0 : lineTops[^1] + lineHeights[^1];
                tallest = Math.Max(tallest, 2 * cellInset + cellHeight);
            }

            // The name column may need more height than the cells.
            nameLines[r] = WrapName(rows[r].WorkplaceName, nameWidth - 4, NameWidthOf);
            tallest = Math.Max(tallest, nameLines[r].Length * nameLineHeight + 4);

            // Second pass: absolute rectangles.
            rowTops[r] = y;
            rowHeights[r] = tallest;
            for (var c = 0; c < placed.Length; c++)
            {
                var cellX = cellsLeft + c * (ColWidth + Gap);
                cells.Add(new RectF(cellX, y, ColWidth, tallest));
                foreach (var (x, cy, w, lines, person) in placed[c])
                    chips.Add(new Chip(new RectF(cellX + cellInset + x, y + cellInset + cy, w, lines.Length * textHeight + 2 * ChipPadY), lines, person));
            }
            y += tallest + Gap;
        }

        AppLog.Metric("opicentrum.positions.layout", sw.Elapsed.TotalMilliseconds, "ms", ("rows", rows.Count), ("chips", chips.Count));
        return new PositionsLayout { RowTops = rowTops, RowHeights = rowHeights, NameWidth = nameWidth, NameLines = nameLines, NameLineHeight = nameLineHeight, Cells = cells, Chips = chips, Height = y };
    }

    private static float ComputeNameWidth(IReadOnlyList<OpicentrumRosterRowItem> rows, Func<string, float> width)
    {
        var needed = MinNameWidth;
        foreach (var row in rows)
        {
            var name = row.WorkplaceName;
            var widestWord = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(width).DefaultIfEmpty(0).Max();
            var full = width(name);
            var w = Math.Min(widestWord, full);
            // Grow from the widest single word until the name needs no more than NameMaxLines lines.
            while (w < full && w + 4 < MaxNameWidth && WrapName(name, w, width).Length > NameMaxLines)
                w += 2;
            needed = Math.Max(needed, w + 4);
        }
        return Math.Min(needed, MaxNameWidth);
    }

    /// <summary>Greedy word wrap; a single word wider than the column is cut with an ellipsis.</summary>
    private static string[] WrapName(string name, float maxWidth, Func<string, float> width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (width(candidate) <= maxWidth) { current = candidate; continue; }
            if (current.Length > 0) lines.Add(current);
            current = BoardCanvas.Fit(word, maxWidth, width);
        }
        if (current.Length > 0) lines.Add(current);
        return lines.Count == 0 ? [string.Empty] : [.. lines];
    }
}
