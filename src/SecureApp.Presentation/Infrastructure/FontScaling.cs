namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// App-wide font size (2026-09-26, user's ask: Settings → Velikost písma must affect everything, the
/// chat included). Most pages set literal FontSize values, which the AppFontSize resource never
/// touched. On Android every text control's native size is instead multiplied by <see cref="Factor"/>
/// right after MAUI applies its own font — so explicit sizes scale too, with no XAML changes.
/// Other platforms keep the older AppFontSize-only scaling (see App.ApplyFontScale).
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
#endif
}
