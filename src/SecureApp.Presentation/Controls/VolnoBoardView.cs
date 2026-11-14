using System.Diagnostics;
using System.Runtime.CompilerServices;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using Font = Microsoft.Maui.Graphics.Font;

namespace SecureApp.Presentation.Controls;

/// <summary>
/// The "Volno" leave board drawn on a canvas (2026-10-24, user: "rozpis a volno se otevira strasne
/// dlouho"). It used to be ~58 people x ~30 days = ~1 740 <c>Label</c>s (each with an AppThemeBinding),
/// built on the UI thread when the tab opened; one canvas draws the same grid in a few milliseconds.
/// It is read-only, so no hit-testing is needed.
///
/// Three instances share one grid geometry (<see cref="VolnoLayout"/>): <see cref="BoardLayer.Body"/> sits
/// inside the page's ScrollView (native scrolling stays smooth), while the <see cref="BoardLayer.Names"/> and
/// <see cref="BoardLayer.Header"/> overlays are translated by the page's code-behind on every
/// scroll so the names and day numbers stay visible — the same sticky behaviour the label version had.
/// Sizing, zoom, the name cap, the name column and the header are shared with the Pozice board in <see cref="BoardLayerView"/>.
/// </summary>
public sealed class VolnoBoardView : BoardLayerView
{
    /// <summary>The name column's width when nothing caps it (see <see cref="BoardLayerView.MaxNamesWidth"/>).</summary>
    public const float NameWidth = 130;
    public const float CellWidth = 30;
    public const float CellHeight = 24;
    public const float HeaderHeight = 20;

    internal const float ColPitch = CellWidth + Gap;
    internal const float RowsTop = HeaderHeight + Gap;

    public static readonly BindableProperty RowsProperty = BindableProperty.Create(
        nameof(Rows), typeof(IReadOnlyList<OpicentrumVolnoRowItem>), typeof(VolnoBoardView), null, propertyChanged: OnDataChanged);

    public IReadOnlyList<OpicentrumVolnoRowItem>? Rows
    {
        get => (IReadOnlyList<OpicentrumVolnoRowItem>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    private VolnoLayout? _layout;

    public VolnoBoardView()
    {
        Drawable = new BoardDrawable(this);
    }

    protected override (float CellsLeft, float ColumnPitch, float Height, float HeaderHeight) RebuildLayout(float zoom)
    {
        _layout = Rows is null ? null : VolnoLayout.For(Rows, NameWidthCap(MaxNamesWidth, zoom));
        return (_layout?.CellsLeft ?? NameWidth + Gap, ColPitch, _layout?.Height ?? RowsTop, HeaderHeight);
    }

    private sealed class BoardDrawable(VolnoBoardView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var sw = Stopwatch.StartNew();
            canvas.Scale(BoardZoom.Value, BoardZoom.Value);
            var (dark, pageBackground, text) = ThemeColors();
            var cellBackground = dark ? BoardCanvas.Resource("SurfaceAltDark", Colors.DimGray) : BoardCanvas.Resource("SurfaceAlt", Colors.LightGray);

            var rows = owner.Rows;
            var days = owner.DayHeaders;
            var layout = owner._layout;
            switch (owner.Layer)
            {
                case BoardLayer.Body when rows is not null && layout is not null:
                    DrawBody(canvas, rows, layout, cellBackground, text);
                    break;
                case BoardLayer.Names when layout is not null:
                    DrawNameColumn(canvas, layout, Font.Default, VolnoLayout.NameFont, pageBackground, text);
                    break;
                case BoardLayer.Header when days is not null:
                    DrawDayHeader(canvas, days, layout?.CellsLeft ?? NameWidth + Gap, CellWidth, HeaderHeight, 9, pageBackground, text);
                    break;
            }

            if (!owner.DrawLogged && owner.Layer == BoardLayer.Body && rows is { Count: > 0 })
            {
                owner.DrawLogged = true;
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

    }
}

/// <summary>
/// Row geometry of the leave board for one name-column width: each person's name wrapped to the column, and every
/// row as tall as its name needs (at least <see cref="VolnoBoardView.CellHeight"/>). Shared by the three layers.
/// </summary>
internal sealed class VolnoLayout : IBoardRowsLayout
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
