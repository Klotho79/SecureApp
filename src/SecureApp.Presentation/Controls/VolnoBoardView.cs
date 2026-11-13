using System.Diagnostics;
using System.Runtime.CompilerServices;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Font = Microsoft.Maui.Graphics.Font;

namespace SecureApp.Presentation.Controls;

/// <summary>Which part of the leave board a <see cref="VolnoBoardView"/> instance draws — see its remarks.</summary>
public enum VolnoBoardLayer
{
    /// <summary>The day cells (scrolls natively inside the page's ScrollView).</summary>
    Body,
    /// <summary>The person-name column, kept pinned on the left by translating it vertically with the scroll.</summary>
    Names,
    /// <summary>The day-number header, kept pinned on top by translating it horizontally with the scroll.</summary>
    Header,
}

/// <summary>
/// The "Volno" leave board drawn on a canvas (2026-10-24, user: "rozpis a volno se otevira strasne
/// dlouho"). It used to be ~58 people x ~30 days = ~1 740 <c>Label</c>s (each with an AppThemeBinding),
/// built on the UI thread when the tab opened; one canvas draws the same grid in a few milliseconds.
/// It is read-only, so no hit-testing is needed.
///
/// Three instances share one grid geometry (<see cref="VolnoLayout"/>): <see cref="VolnoBoardLayer.Body"/> sits
/// inside the page's ScrollView (native scrolling stays smooth), while the <see cref="VolnoBoardLayer.Names"/> and
/// <see cref="VolnoBoardLayer.Header"/> overlays are translated by the page's code-behind on every
/// scroll so the names and day numbers stay visible — the same sticky behaviour the label version had.
/// Colours are the app's own tokens (SurfaceAlt/Gray950/White), re-read on every draw so a theme
/// switch only needs an invalidate.
/// </summary>
public sealed class VolnoBoardView : GraphicsView
{
    /// <summary>The name column's width when nothing caps it (see <see cref="MaxNamesWidth"/>).</summary>
    public const float NameWidth = 130;
    public const float CellWidth = 30;
    public const float CellHeight = 24;
    public const float HeaderHeight = 20;
    public const float Gap = 2;

    internal const float ColPitch = CellWidth + Gap;
    internal const float RowsTop = HeaderHeight + Gap;

    public static readonly BindableProperty LayerProperty = BindableProperty.Create(
        nameof(Layer), typeof(VolnoBoardLayer), typeof(VolnoBoardView), VolnoBoardLayer.Body, propertyChanged: OnDataChanged);

    public static readonly BindableProperty RowsProperty = BindableProperty.Create(
        nameof(Rows), typeof(IReadOnlyList<OpicentrumVolnoRowItem>), typeof(VolnoBoardView), null, propertyChanged: OnDataChanged);

    public static readonly BindableProperty DayHeadersProperty = BindableProperty.Create(
        nameof(DayHeaders), typeof(IReadOnlyList<string>), typeof(VolnoBoardView), null, propertyChanged: OnDataChanged);

    public static readonly BindableProperty MaxNamesWidthProperty = BindableProperty.Create(
        nameof(MaxNamesWidth), typeof(float), typeof(VolnoBoardView), float.PositiveInfinity, propertyChanged: OnDataChanged);

    public VolnoBoardLayer Layer
    {
        get => (VolnoBoardLayer)GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    public IReadOnlyList<OpicentrumVolnoRowItem>? Rows
    {
        get => (IReadOnlyList<OpicentrumVolnoRowItem>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public IReadOnlyList<string>? DayHeaders
    {
        get => (IReadOnlyList<string>?)GetValue(DayHeadersProperty);
        set => SetValue(DayHeadersProperty, value);
    }

    /// <summary>
    /// Largest on-screen width the pinned name column may take (2026-11-13, user: same 1/5-of-the-display cap as the
    /// Pozice board); past it, zooming wraps the names onto more lines instead of widening the column.
    /// <see cref="float.PositiveInfinity"/> (the default) means no cap.
    /// </summary>
    public float MaxNamesWidth
    {
        get => (float)GetValue(MaxNamesWidthProperty);
        set => SetValue(MaxNamesWidthProperty, value);
    }

    /// <summary>The name column's width cap in board units at <paramref name="zoom"/>, for <see cref="VolnoLayout.For"/>.</summary>
    internal static float NameWidthCap(float maxNamesWidth, float zoom) => maxNamesWidth / zoom - Gap;

    private VolnoLayout? _layout;
    private bool _drawLogged;

    public VolnoBoardView()
    {
        Drawable = new BoardDrawable(this);
    }

    private static void OnDataChanged(BindableObject bindable, object oldValue, object newValue) => ((VolnoBoardView)bindable).Refresh();

    private void Refresh()
    {
        var dayCount = DayHeaders?.Count ?? 0;
        var zoom = BoardZoom.Value;
        _layout = Rows is null ? null : VolnoLayout.For(Rows, NameWidthCap(MaxNamesWidth, zoom));
        var cellsLeft = _layout?.CellsLeft ?? NameWidth + Gap;
        var totalWidth = cellsLeft + dayCount * ColPitch;
        var totalHeight = _layout?.Height ?? RowsTop;

        switch (Layer)
        {
            case VolnoBoardLayer.Body:
                WidthRequest = totalWidth * zoom;
                HeightRequest = totalHeight * zoom;
                break;
            case VolnoBoardLayer.Names:
                WidthRequest = cellsLeft * zoom;
                HeightRequest = totalHeight * zoom;
                break;
            case VolnoBoardLayer.Header:
                WidthRequest = totalWidth * zoom;
                HeightRequest = HeaderHeight * zoom;
                break;
        }

        _drawLogged = false;
        Invalidate();
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

    private static Color Resource(string key, Color fallback) => BoardCanvas.Resource(key, fallback);

    private sealed class BoardDrawable(VolnoBoardView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var sw = Stopwatch.StartNew();
            canvas.Scale(BoardZoom.Value, BoardZoom.Value);
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var pageBackground = dark ? Resource("Gray950", Colors.Black) : Resource("White", Colors.White);
            var cellBackground = dark ? Resource("SurfaceAltDark", Colors.DimGray) : Resource("SurfaceAlt", Colors.LightGray);
            var text = dark ? Colors.White : Colors.Black;

            var rows = owner.Rows;
            var days = owner.DayHeaders;
            var layout = owner._layout;
            switch (owner.Layer)
            {
                case VolnoBoardLayer.Body when rows is not null && layout is not null:
                    DrawBody(canvas, rows, layout, cellBackground, text);
                    break;
                case VolnoBoardLayer.Names when layout is not null:
                    DrawNames(canvas, layout, pageBackground, text);
                    break;
                case VolnoBoardLayer.Header when days is not null:
                    DrawHeader(canvas, days, layout?.CellsLeft ?? NameWidth + Gap, pageBackground, text);
                    break;
            }

            if (!owner._drawLogged && owner.Layer == VolnoBoardLayer.Body && rows is { Count: > 0 })
            {
                owner._drawLogged = true;
                AppLog.Metric("opicentrum.volno.draw", sw.Elapsed.TotalMilliseconds, "ms", ("rows", rows.Count));
            }
        }

        private static void DrawBody(ICanvas canvas, IReadOnlyList<OpicentrumVolnoRowItem> rows, VolnoLayout layout, Color cellBackground, Color text)
        {
            canvas.Font = Font.Default;
            canvas.FontSize = 8;
            canvas.FontColor = text;
            canvas.FillColor = cellBackground;

            for (var r = 0; r < rows.Count; r++)
            {
                var y = layout.RowTops[r];
                var height = layout.RowHeights[r];
                var codes = rows[r].DayCodes;
                for (var d = 0; d < codes.Count; d++)
                {
                    var x = layout.CellsLeft + d * ColPitch;
                    canvas.FillRectangle(x, y, CellWidth, height);
                    if (codes[d].Length > 0)
                        canvas.DrawString(codes[d], x, y, CellWidth, height, HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }
        }

        private static void DrawNames(ICanvas canvas, VolnoLayout layout, Color pageBackground, Color text)
        {
            canvas.FillColor = pageBackground;
            canvas.FillRectangle(0, 0, layout.CellsLeft, layout.Height);

            canvas.Font = Font.Default;
            canvas.FontSize = VolnoLayout.NameFont;
            canvas.FontColor = text;
            for (var r = 0; r < layout.NameLines.Length; r++)
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
            canvas.FillRectangle(0, 0, cellsLeft + days.Count * ColPitch, HeaderHeight);

            canvas.Font = Font.DefaultBold;
            canvas.FontSize = 9;
            canvas.FontColor = text;
            for (var d = 0; d < days.Count; d++)
                canvas.DrawString(days[d], cellsLeft + d * ColPitch, 0, CellWidth, HeaderHeight, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}

/// <summary>
/// Row geometry of the leave board for one name-column width: each person's name wrapped to the column, and every
/// row as tall as its name needs (at least <see cref="VolnoBoardView.CellHeight"/>). Shared by the three layers.
/// </summary>
internal sealed class VolnoLayout
{
    public const float NameFont = 10;
    private const float MinCappedNameWidth = 24;

    public required float NameWidth { get; init; }
    public float CellsLeft => NameWidth + VolnoBoardView.Gap;
    public required string[][] NameLines { get; init; }
    public required float NameLineHeight { get; init; }
    public required float[] RowTops { get; init; }
    public required float[] RowHeights { get; init; }
    public required float Height { get; init; }

    private static readonly ConditionalWeakTable<object, Names> Cache = new();

    /// <summary>The layout of <paramref name="rows"/> with the name column at most <paramref name="maxNameWidth"/> board units wide.</summary>
    public static VolnoLayout For(IReadOnlyList<OpicentrumVolnoRowItem> rows, float maxNameWidth = float.PositiveInfinity)
    {
        var names = Cache.GetValue(rows, _ => new Names([.. rows.Select(row => row.PersonName)]));
        var nameWidth = maxNameWidth >= VolnoBoardView.NameWidth ? VolnoBoardView.NameWidth : MathF.Max(MinCappedNameWidth, MathF.Floor(maxNameWidth));
        return names.LayoutFor(nameWidth);
    }

    private sealed class Names(string[] names)
    {
        private readonly PlatformStringSizeService _sizes = new();
        private readonly Dictionary<string, float> _widths = [];
        private readonly Dictionary<float, VolnoLayout> _layouts = [];

        // Same slack as the Pozice chips: the measured width is a little short of what DrawString lays out.
        private float Width(string s)
        {
            if (!_widths.TryGetValue(s, out var w)) _widths[s] = w = _sizes.GetStringSize(s, Font.Default, NameFont).Width * 1.06f + 2;
            return w;
        }

        public VolnoLayout LayoutFor(float nameWidth)
        {
            if (_layouts.TryGetValue(nameWidth, out var cached)) return cached;
            if (_layouts.Count > 64) _layouts.Clear(); // a pinch passes through many widths

            var lineHeight = _sizes.GetStringSize("Ag", Font.Default, NameFont).Height + 1;
            var lines = new string[names.Length][];
            var tops = new float[names.Length];
            var heights = new float[names.Length];
            var y = VolnoBoardView.RowsTop;
            for (var r = 0; r < names.Length; r++)
            {
                lines[r] = BoardCanvas.WrapText(names[r], nameWidth - 4, Width);
                tops[r] = y;
                heights[r] = Math.Max(VolnoBoardView.CellHeight, lines[r].Length * lineHeight + 4);
                y += heights[r] + VolnoBoardView.Gap;
            }

            var layout = new VolnoLayout { NameWidth = nameWidth, NameLines = lines, NameLineHeight = lineHeight, RowTops = tops, RowHeights = heights, Height = y };
            _layouts[nameWidth] = layout;
            return layout;
        }
    }
}
