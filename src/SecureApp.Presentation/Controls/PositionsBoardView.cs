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
        var totalWidth = PositionsLayout.CellsLeft + dayCount * (PositionsLayout.ColWidth + PositionsLayout.Gap);

        _layout = Layer == PositionsBoardLayer.Header || Rows is null ? null : PositionsLayout.For(Rows);
        var totalHeight = _layout?.Height ?? PositionsLayout.RowsTop;

        switch (Layer)
        {
            case PositionsBoardLayer.Body:
                WidthRequest = totalWidth;
                HeightRequest = totalHeight;
                break;
            case PositionsBoardLayer.Names:
                WidthRequest = PositionsLayout.CellsLeft;
                HeightRequest = totalHeight;
                break;
            case PositionsBoardLayer.Header:
                WidthRequest = totalWidth;
                HeightRequest = PositionsLayout.HeaderHeight;
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
        foreach (var chip in _layout.Chips)
        {
            if (chip.Rect.Inflate(slack, slack).Contains((float)point.X, (float)point.Y))
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
        if (Handler is not null)
            app.RequestedThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    private sealed class BoardDrawable(PositionsBoardView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var sw = Stopwatch.StartNew();
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var pageBackground = dark ? BoardCanvas.Resource("Gray950", Colors.Black) : BoardCanvas.Resource("White", Colors.White);
            var cardStroke = dark ? BoardCanvas.Resource("Gray600", Colors.DimGray) : BoardCanvas.Resource("Gray100", Colors.LightGray);
            var chipBackground = dark ? BoardCanvas.Resource("SurfaceAltDark", Colors.DimGray) : BoardCanvas.Resource("SurfaceAlt", Colors.LightGray);
            var text = dark ? Colors.White : Colors.Black;

            switch (owner.Layer)
            {
                case PositionsBoardLayer.Body when owner._layout is { } layout:
                    DrawBody(canvas, layout, pageBackground, cardStroke, chipBackground, text);
                    break;
                case PositionsBoardLayer.Names when owner._layout is { } layout && owner.Rows is { } rows:
                    DrawNames(canvas, layout, rows, pageBackground, text);
                    break;
                case PositionsBoardLayer.Header when owner.DayHeaders is { } days:
                    DrawHeader(canvas, days, pageBackground, text);
                    break;
            }

            if (!owner._drawLogged && owner.Layer == PositionsBoardLayer.Body && owner._layout is { } drawn)
            {
                owner._drawLogged = true;
                AppLog.Metric("opicentrum.positions.draw", sw.Elapsed.TotalMilliseconds, "ms", ("cells", drawn.Cells.Count), ("chips", drawn.Chips.Count));
                owner.Drawn?.Invoke(owner, EventArgs.Empty);
            }
        }

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
            canvas.FillColor = chipBackground;
            foreach (var chip in layout.Chips)
            {
                canvas.FillRoundedRectangle(chip.Rect, PositionsLayout.ChipRadius);
                canvas.DrawString(chip.Text, chip.Rect.X + PositionsLayout.ChipPadX, chip.Rect.Y, chip.Rect.Width - 2 * PositionsLayout.ChipPadX, chip.Rect.Height,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        private static void DrawNames(ICanvas canvas, PositionsLayout layout, IReadOnlyList<OpicentrumRosterRowItem> rows, Color pageBackground, Color text)
        {
            canvas.FillColor = pageBackground;
            canvas.FillRectangle(0, 0, PositionsLayout.CellsLeft, layout.Height);

            canvas.Font = Font.DefaultBold;
            canvas.FontSize = PositionsLayout.NameFont;
            canvas.FontColor = text;
            for (var r = 0; r < rows.Count; r++)
            {
                var name = BoardCanvas.Fit(rows[r].WorkplaceName, PositionsLayout.NameWidth - 4, s => canvas.GetStringSize(s, Font.DefaultBold, PositionsLayout.NameFont).Width);
                canvas.DrawString(name, 0, layout.RowTops[r], PositionsLayout.NameWidth, layout.RowHeights[r], HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        private static void DrawHeader(ICanvas canvas, IReadOnlyList<string> days, Color pageBackground, Color text)
        {
            canvas.FillColor = pageBackground;
            canvas.FillRectangle(0, 0, PositionsLayout.CellsLeft + days.Count * (PositionsLayout.ColWidth + PositionsLayout.Gap), PositionsLayout.HeaderHeight);

            canvas.Font = Font.DefaultBold;
            canvas.FontSize = PositionsLayout.NameFont;
            canvas.FontColor = text;
            for (var d = 0; d < days.Count; d++)
                canvas.DrawString(days[d], PositionsLayout.CellsLeft + d * (PositionsLayout.ColWidth + PositionsLayout.Gap), 0, PositionsLayout.ColWidth, PositionsLayout.HeaderHeight,
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
    public const float NameWidth = 130;
    public const float ColWidth = 140;
    public const float Gap = 2;
    public const float HeaderHeight = 20;
    public const float CellsLeft = NameWidth + Gap;
    public const float RowsTop = HeaderHeight + Gap;

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

    public sealed record Chip(RectF Rect, string Text, OpicentrumRosterPersonChip Person);

    public required float[] RowTops { get; init; }
    public required float[] RowHeights { get; init; }
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
        var chipHeight = textHeight + 2 * ChipPadY;
        var lineHeight = chipHeight + ChipMargin;
        var cellInset = CellPadding + CellStroke;
        var inner = ColWidth - 2 * cellInset;

        var rowTops = new float[rows.Count];
        var rowHeights = new float[rows.Count];
        var cells = new List<RectF>();
        var chips = new List<Chip>();

        var y = RowsTop;
        for (var r = 0; r < rows.Count; r++)
        {
            // First pass: wrap each cell's chips relative to the cell's content origin and find the tallest cell.
            var placed = new List<(float X, float Y, float W, string Text, OpicentrumRosterPersonChip Person)>[rows[r].Cells.Count];
            var tallest = MinRowHeight;
            for (var c = 0; c < rows[r].Cells.Count; c++)
            {
                placed[c] = [];
                float x = 0, line = 0;
                foreach (var person in rows[r].Cells[c].People)
                {
                    var text = BoardCanvas.Fit(person.PersonName, inner - 2 * ChipPadX - ChipMargin, Width);
                    var w = Math.Min(Width(text) + 2 * ChipPadX, inner - ChipMargin);
                    if (x > 0 && x + w + ChipMargin > inner) { line++; x = 0; }
                    placed[c].Add((x, line * lineHeight, w, text, person));
                    x += w + ChipMargin;
                }
                var lines = placed[c].Count == 0 ? 0 : line + 1;
                tallest = Math.Max(tallest, 2 * cellInset + lines * lineHeight);
            }

            // Second pass: absolute rectangles.
            rowTops[r] = y;
            rowHeights[r] = tallest;
            for (var c = 0; c < placed.Length; c++)
            {
                var cellX = CellsLeft + c * (ColWidth + Gap);
                cells.Add(new RectF(cellX, y, ColWidth, tallest));
                foreach (var (x, cy, w, text, person) in placed[c])
                    chips.Add(new Chip(new RectF(cellX + cellInset + x, y + cellInset + cy, w, chipHeight), text, person));
            }
            y += tallest + Gap;
        }

        AppLog.Metric("opicentrum.positions.layout", sw.Elapsed.TotalMilliseconds, "ms", ("rows", rows.Count), ("chips", chips.Count));
        return new PositionsLayout { RowTops = rowTops, RowHeights = rowHeights, Cells = cells, Chips = chips, Height = y };
    }
}
