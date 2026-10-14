namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// App-wide font size (2026-09-26, user's ask: Settings → Velikost písma must affect everything, the
/// chat included). Most pages set literal FontSize values, which the AppFontSize resource never
/// touched. On Android every text control's native size is instead multiplied by <see cref="Factor"/>
/// right after MAUI applies its own font — so explicit sizes scale too, with no XAML changes.
///
/// 2026-10-14, same trick extended to Windows (user's own ask: "zvetsi pismo na pc je skoro
/// necitelne" — literal sizes were untouched there too, same gap Android had before this existed;
/// see App.ApplyFontScale's own WindowsBaselineMultiplier for why Windows' "normal" is also a bigger
/// absolute size than the phone's, on top of this same per-control scaling).
/// </summary>
public static class FontScaling
{
    public static double Factor { get; private set; } = 1.0;

    public static double FactorFor(int index) => index switch { 0 => 0.9, 2 => 1.15, 3 => 1.3, _ => 1.0 };

    public static void Register()
    {
#if ANDROID
        const string key = nameof(ITextStyle.Font);
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(key, (h, v) => Scale(h.PlatformView, v));
        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(key, (h, v) => Scale(h.PlatformView, v));
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(key, (h, v) => Scale(h.PlatformView, v));
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping(key, (h, v) => Scale(h.PlatformView, v));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(key, (h, v) => Scale(h.PlatformView, v));
        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping(key, (h, v) => Scale(h.PlatformView, v));
        Microsoft.Maui.Handlers.TimePickerHandler.Mapper.AppendToMapping(key, (h, v) => Scale(h.PlatformView, v));
#elif WINDOWS
        const string key = nameof(ITextStyle.Font);
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(key, (h, v) => ScaleWindows(h.PlatformView));
        Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(key, (h, v) => ScaleWindows(h.PlatformView));
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(key, (h, v) => ScaleWindows(h.PlatformView));
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping(key, (h, v) => ScaleWindows(h.PlatformView));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping(key, (h, v) => ScaleWindows(h.PlatformView));
        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping(key, (h, v) => ScaleWindows(h.PlatformView));
        Microsoft.Maui.Handlers.TimePickerHandler.Mapper.AppendToMapping(key, (h, v) => ScaleWindows(h.PlatformView));
#endif
    }

    /// <summary>Stores the new factor and re-applies fonts on every view already on screen.</summary>
    public static void SetFactor(double factor)
    {
        Factor = factor;
        if (Application.Current is null) return;
        foreach (var window in Application.Current.Windows)
        {
            if (window.Page is IVisualTreeElement root)
                Refresh(root);
        }
    }

    private static void Refresh(IVisualTreeElement element)
    {
        if (element is ITextStyle && element is IView { Handler: { } handler })
            handler.UpdateValue(nameof(ITextStyle.Font));
        foreach (var child in element.GetVisualChildren())
            Refresh(child);
    }

#if ANDROID
    private static void Scale(object platformView, object virtualView)
    {
        // At 1.0 MAUI's own mapping (which runs first) already set the right size.
        if (Factor == 1.0) return;
        if (platformView is not Android.Widget.TextView textView || virtualView is not ITextStyle style) return;
        var font = style.Font;
        if (font.Size <= 0) return;
        var unit = font.AutoScalingEnabled ? Android.Util.ComplexUnitType.Sp : Android.Util.ComplexUnitType.Dip;
        textView.SetTextSize(unit, (float)(font.Size * Factor));
    }
#elif WINDOWS
    /// <summary>
    /// MAUI's own Font mapping (which runs first on every single invocation, not just once) already
    /// wrote the "factor 1.0" FontSize onto the platform control right before this runs — so reading
    /// it back and multiplying is always relative to a clean value, same reasoning as Android's own
    /// Scale above, just without the sp/dip unit conversion WinUI's FontSize doesn't need.
    /// </summary>
    private static void ScaleWindows(object platformView)
    {
        if (Factor == 1.0) return;
        switch (platformView)
        {
            case Microsoft.UI.Xaml.Controls.TextBlock textBlock:
                textBlock.FontSize *= Factor;
                break;
            case Microsoft.UI.Xaml.Controls.Control control:
                control.FontSize *= Factor;
                break;
        }
    }
#endif
}
