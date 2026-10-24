using System.Diagnostics;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.ViewModels;
using Microsoft.Maui.Graphics;
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
/// Three instances share one grid geometry: <see cref="VolnoBoardLayer.Body"/> sits inside the page's
/// ScrollView (native scrolling stays smooth), while the <see cref="VolnoBoardLayer.Names"/> and
/// <see cref="VolnoBoardLayer.Header"/> overlays are translated by the page's code-behind on every
/// scroll so the names and day numbers stay visible — the same sticky behaviour the label version had.
/// Colours are the app's own tokens (SurfaceAlt/Gray950/White), re-read on every draw so a theme
/// switch only needs an invalidate.
/// </summary>
public sealed class VolnoBoardView : GraphicsView
{
    public const float NameWidth = 130;
    public const float CellWidth = 30;
    public const float CellHeight = 24;
    public const float HeaderHeight = 20;
    public const float Gap = 2;

    private const float ColPitch = CellWidth + Gap;
    private const float RowPitch = CellHeight + Gap;
    private const float CellsLeft = NameWidth + Gap;
    private const float RowsTop = HeaderHeight + Gap;

    public static readonly BindableProperty LayerProperty = BindableProperty.Create(
        nameof(Layer), typeof(VolnoBoardLayer), typeof(VolnoBoardView), VolnoBoardLayer.Body, propertyChanged: OnDataChanged);

    public static readonly BindableProperty RowsProperty = BindableProperty.Create(
        nameof(Rows), typeof(IReadOnlyList<OpicentrumVolnoRowItem>), typeof(VolnoBoardView), null, propertyChanged: OnDataChanged);

    public static readonly BindableProperty DayHeadersProperty = BindableProperty.Create(
        nameof(DayHeaders), typeof(IReadOnlyList<string>), typeof(VolnoBoardView), null, propertyChanged: OnDataChanged);

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

    private bool _drawLogged;

    public VolnoBoardView()
    {
        Drawable = new BoardDrawable(this);
    }

    private static void OnDataChanged(BindableObject bindable, object oldValue, object newValue) => ((VolnoBoardView)bindable).Refresh();

    private void Refresh()
    {
        var rowCount = Rows?.Count ?? 0;
        var dayCount = DayHeaders?.Count ?? 0;
        var totalWidth = CellsLeft + dayCount * ColPitch;
        var totalHeight = RowsTop + rowCount * RowPitch;

        switch (Layer)
        {
            case VolnoBoardLayer.Body:
                WidthRequest = totalWidth;
                HeightRequest = totalHeight;
                break;
            case VolnoBoardLayer.Names:
                WidthRequest = CellsLeft;
                HeightRequest = totalHeight;
                break;
            case VolnoBoardLayer.Header:
                WidthRequest = totalWidth;
                HeightRequest = HeaderHeight;
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
        if (Handler is not null)
            app.RequestedThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    private static Color Resource(string key, Color fallback) => BoardCanvas.Resource(key, fallback);

    private sealed class BoardDrawable(VolnoBoardView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var sw = Stopwatch.StartNew();
            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var pageBackground = dark ? Resource("Gray950", Colors.Black) : Resource("White", Colors.White);
            var cellBackground = dark ? Resource("SurfaceAltDark", Colors.DimGray) : Resource("SurfaceAlt", Colors.LightGray);
            var text = dark ? Colors.White : Colors.Black;

            var rows = owner.Rows;
            var days = owner.DayHeaders;
            switch (owner.Layer)
            {
                case VolnoBoardLayer.Body when rows is not null:
                    DrawBody(canvas, rows, cellBackground, text);
                    break;
                case VolnoBoardLayer.Names when rows is not null:
                    DrawNames(canvas, rows, pageBackground, text);
                    break;
                case VolnoBoardLayer.Header when days is not null:
                    DrawHeader(canvas, days, pageBackground, text);
                    break;
            }

            if (!owner._drawLogged && owner.Layer == VolnoBoardLayer.Body && rows is { Count: > 0 })
            {
                owner._drawLogged = true;
                AppLog.Metric("opicentrum.volno.draw", sw.Elapsed.TotalMilliseconds, "ms", ("rows", rows.Count));
            }
        }

        private static void DrawBody(ICanvas canvas, IReadOnlyList<OpicentrumVolnoRowItem> rows, Color cellBackground, Color text)
        {
            canvas.Font = Font.Default;
            canvas.FontSize = 8;
            canvas.FontColor = text;
            canvas.FillColor = cellBackground;

            for (var r = 0; r < rows.Count; r++)
            {
                var y = RowsTop + r * RowPitch;
                var codes = rows[r].DayCodes;
                for (var d = 0; d < codes.Count; d++)
                {
                    var x = CellsLeft + d * ColPitch;
                    canvas.FillRectangle(x, y, CellWidth, CellHeight);
                    if (codes[d].Length > 0)
                        canvas.DrawString(codes[d], x, y, CellWidth, CellHeight, HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }
        }

        private static void DrawNames(ICanvas canvas, IReadOnlyList<OpicentrumVolnoRowItem> rows, Color pageBackground, Color text)
        {
            canvas.FillColor = pageBackground;
            canvas.FillRectangle(0, 0, CellsLeft, RowsTop + rows.Count * RowPitch);

            canvas.Font = Font.Default;
            canvas.FontSize = 10;
            canvas.FontColor = text;
            for (var r = 0; r < rows.Count; r++)
            {
                var y = RowsTop + r * RowPitch;
                canvas.DrawString(Fit(canvas, rows[r].PersonName), 0, y, NameWidth, CellHeight, HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }

        private static void DrawHeader(ICanvas canvas, IReadOnlyList<string> days, Color pageBackground, Color text)
        {
            canvas.FillColor = pageBackground;
            canvas.FillRectangle(0, 0, CellsLeft + days.Count * ColPitch, HeaderHeight);

            canvas.Font = Font.DefaultBold;
            canvas.FontSize = 9;
            canvas.FontColor = text;
            for (var d = 0; d < days.Count; d++)
                canvas.DrawString(days[d], CellsLeft + d * ColPitch, 0, CellWidth, HeaderHeight, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        /// <summary>Single line that fits the name column, cut with an ellipsis like the label's TailTruncation.</summary>
        private static string Fit(ICanvas canvas, string name) =>
            BoardCanvas.Fit(name, NameWidth - 4, s => canvas.GetStringSize(s, Font.Default, 10).Width);
    }
}
