using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Button = Microsoft.UI.Xaml.Controls.Button;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// The Shell's back arrow on Windows is the WinUI NavigationView's own back button, and MAUI places it in the
/// window's title bar. The title bar is dark (#202020), but the button took the app's light-theme near-black
/// foreground, so it was practically invisible. 2026-11-14, user's ask: "návratovou šipku na PC v levém horním
/// rohu udělej bílou, nebo v barvách palety, ale světlejší, aby byla vidět".
/// Fix: the button gets the dark theme, which sets its hover, pressed and disabled states for a dark surface,
/// and a light tint of the palette's teal (PrimaryDark #6FC3E5 lightened to #C5ECFA) for its normal state.
/// Plain #6FC3E5 was tried first, but the thin 16px glyph antialiases down to about #5797B1, which is still dim.
/// </summary>
internal static class TitleBarBackButtonTint
{
    private const string BackButtonName = "NavigationViewBackButton";
    private static SolidColorBrush? _brush;

    public static void Register(MauiAppBuilder builder)
    {
        builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows.OnWindowCreated(window =>
        {
            // The content isn't there yet when the window is created; it is by the first activation.
            window.Activated += (_, _) => Apply(window);
        })));
    }

    /// <summary>Tints every NavigationView back button in <paramref name="window"/>; cheap to call again (after navigation), already-tinted buttons are skipped.</summary>
    public static void Apply(Microsoft.UI.Xaml.Window? window)
    {
        try
        {
            if (window?.Content is DependencyObject root)
                Tint(root);
        }
        catch
        {
            // Purely cosmetic — never let it break the window.
        }
    }

    private static void Tint(DependencyObject element)
    {
        if (element is Button { Name: BackButtonName } button)
        {
            _brush ??= new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xC5, 0xEC, 0xFA));
            if (!ReferenceEquals(button.Foreground, _brush))
            {
                button.RequestedTheme = ElementTheme.Dark;
                button.Foreground = _brush;
            }
            return;
        }

        var count = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
            Tint(VisualTreeHelper.GetChild(element, i));
    }
}
