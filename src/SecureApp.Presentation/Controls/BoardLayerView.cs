using SecureApp.Presentation.Infrastructure;
using Font = Microsoft.Maui.Graphics.Font;

namespace SecureApp.Presentation.Controls;

/// <summary>Which part of a canvas board an instance draws (the Pozice and Volno boards both use three instances).</summary>
public enum BoardLayer
{
    /// <summary>The day cells (scrolls natively inside the page's ScrollView).</summary>
    Body,
    /// <summary>The name column, kept pinned on the left by translating it vertically with the scroll.</summary>
    Names,
    /// <summary>The day header, kept pinned on top by translating it horizontally with the scroll.</summary>
    Header,
}

/// <summary>The row geometry both boards share: each row's name wrapped to the name column, and every row's top and height.</summary>
internal interface IBoardRowsLayout
{
    float NameWidth { get; }
    float CellsLeft { get; }
    string[][] NameLines { get; }
    float NameLineHeight { get; }
    float[] RowTops { get; }
    float[] RowHeights { get; }
    float Height { get; }
}

/// <summary>
/// What the canvas-drawn Opicentrum boards (<see cref="PositionsBoardView"/>, <see cref="VolnoBoardView"/>) have in common
/// (2026-11-14 — it used to be copied in both): the layer/day-header/name-cap properties, sizing every layer from the shared
/// zoom, redrawing on a zoom or theme change, and drawing the pinned name column and day header.
/// </summary>
public abstract class BoardLayerView : GraphicsView
{
    public const float Gap = 2;

    public static readonly BindableProperty LayerProperty = BindableProperty.Create(
        nameof(Layer), typeof(BoardLayer), typeof(BoardLayerView), BoardLayer.Body, propertyChanged: OnDataChanged);

    public static readonly BindableProperty DayHeadersProperty = BindableProperty.Create(
        nameof(DayHeaders), typeof(IReadOnlyList<string>), typeof(BoardLayerView), null, propertyChanged: OnDataChanged);

    public static readonly BindableProperty MaxNamesWidthProperty = BindableProperty.Create(
        nameof(MaxNamesWidth), typeof(float), typeof(BoardLayerView), float.PositiveInfinity, propertyChanged: OnDataChanged);

    public BoardLayer Layer
    {
        get => (BoardLayer)GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    public IReadOnlyList<string>? DayHeaders
    {
        get => (IReadOnlyList<string>?)GetValue(DayHeadersProperty);
        set => SetValue(DayHeadersProperty, value);
    }

    /// <summary>
    /// Largest on-screen width the pinned name column may take (user: "max do 1/5 sirky displeje"); past it, zooming wraps
    /// the names onto more lines instead of widening the column. <see cref="float.PositiveInfinity"/> (the default) means no cap.
    /// </summary>
    public float MaxNamesWidth
    {
        get => (float)GetValue(MaxNamesWidthProperty);
        set => SetValue(MaxNamesWidthProperty, value);
    }

    /// <summary>The name column's width cap in board units at <paramref name="zoom"/>.</summary>
    internal static float NameWidthCap(float maxNamesWidth, float zoom) => maxNamesWidth / zoom - Gap;

    /// <summary>False until the body layer has drawn its current data once (the subclasses log that first draw).</summary>
    protected bool DrawLogged { get; set; }

    protected static void OnDataChanged(BindableObject bindable, object oldValue, object newValue) => ((BoardLayerView)bindable).Refresh();

    /// <summary>Rebuilds the layout for the current rows and zoom and returns the board's size in board units.</summary>
    protected abstract (float CellsLeft, float ColumnPitch, float Height, float HeaderHeight) RebuildLayout(float zoom);

    protected void Refresh()
    {
        var zoom = BoardZoom.Value;
        var size = RebuildLayout(zoom);
        var totalWidth = size.CellsLeft + (DayHeaders?.Count ?? 0) * size.ColumnPitch;

        switch (Layer)
        {
            case BoardLayer.Body:
                WidthRequest = totalWidth * zoom;
                HeightRequest = size.Height * zoom;
                break;
            case BoardLayer.Names:
                WidthRequest = size.CellsLeft * zoom;
                HeightRequest = size.Height * zoom;
                break;
            case BoardLayer.Header:
                WidthRequest = totalWidth * zoom;
                HeightRequest = size.HeaderHeight * zoom;
                break;
        }

        DrawLogged = false;
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

    /// <summary>The page background and text colour from the app's own tokens; re-read on every draw, so a theme switch only needs an invalidate.</summary>
    protected static (bool Dark, Color PageBackground, Color Text) ThemeColors()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var pageBackground = dark ? BoardCanvas.Resource("Gray950", Colors.Black) : BoardCanvas.Resource("White", Colors.White);
        return (dark, pageBackground, dark ? Colors.White : Colors.Black);
    }

    /// <summary>The pinned name column: each row's wrapped name, vertically centred in the row.</summary>
    internal static void DrawNameColumn(ICanvas canvas, IBoardRowsLayout layout, Font font, float fontSize, Color pageBackground, Color text)
    {
        canvas.FillColor = pageBackground;
        canvas.FillRectangle(0, 0, layout.CellsLeft, layout.Height);

        canvas.Font = font;
        canvas.FontSize = fontSize;
        canvas.FontColor = text;
        for (var r = 0; r < layout.NameLines.Length; r++)
        {
            var lines = layout.NameLines[r];
            var top = layout.RowTops[r] + (layout.RowHeights[r] - lines.Length * layout.NameLineHeight) / 2;
            for (var i = 0; i < lines.Length; i++)
                canvas.DrawString(lines[i], 0, top + i * layout.NameLineHeight, layout.NameWidth, layout.NameLineHeight, HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    /// <summary>The pinned day header, one centred label per day column.</summary>
    internal static void DrawDayHeader(ICanvas canvas, IReadOnlyList<string> days, float cellsLeft, float columnWidth, float headerHeight, float fontSize, Color pageBackground, Color text)
    {
        var pitch = columnWidth + Gap;
        canvas.FillColor = pageBackground;
        canvas.FillRectangle(0, 0, cellsLeft + days.Count * pitch, headerHeight);

        canvas.Font = Font.DefaultBold;
        canvas.FontSize = fontSize;
        canvas.FontColor = text;
        for (var d = 0; d < days.Count; d++)
            canvas.DrawString(days[d], cellsLeft + d * pitch, 0, columnWidth, headerHeight, HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}
