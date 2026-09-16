using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace SecureApp.Presentation;

// WindowSoftInputMode (2026-09-15, user's live bug: opening the keyboard in a chat's compose box
// pans the whole window up instead of resizing it, pushing the top bar out of reach until the
// keyboard is dismissed) — with nothing set here, Android falls back to its own default (effectively
// "pan"), which is exactly this symptom. AdjustResize instead shrinks the content area to fit above
// the keyboard, keeping every page's header/tab bar always reachable.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density, WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// 2026-09-16 — the real fix for the same bug the [Activity] WindowSoftInputMode attribute above
    /// was meant to fix. Neither the attribute nor a one-shot <see cref="WindowCompat.SetDecorFitsSystemWindows"/>
    /// call in OnCreate actually worked live on a real Android 16 device (S23+): `dumpsys window`
    /// confirmed the RUNNING window's own attrs still read `sim={adjust=pan}` despite the compiled
    /// manifest correctly saying `adjustResize` — something (most likely .NET MAUI's own Android
    /// platform setup, which runs its own window configuration after Activity.OnCreate) was resetting
    /// it back to pan. Forcing it explicitly via <see cref="Android.Views.Window.SetSoftInputMode"/>
    /// — and re-asserting on every OnResume, not just once — wins the "last write" regardless of what
    /// MAUI's own internal timing does. <see cref="WindowCompat.SetDecorFitsSystemWindows"/>(Window,
    /// true) is still kept alongside it: since Android 15 (API 35), edge-to-edge rendering is
    /// mandatory once targetSdkVersion >= 35 (this app resolves to 36 — see the .csproj's own note on
    /// why that couldn't be pinned lower in this SDK release), and AdjustResize's "shrink the window"
    /// behavior needs the legacy (pre-edge-to-edge) layout model to actually have somewhere to shrink
    /// into.
    /// </summary>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplyWindowSoftInputMode();
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplyWindowSoftInputMode();
    }

    private void ApplyWindowSoftInputMode()
    {
        if (Window is null) return;
        WindowCompat.SetDecorFitsSystemWindows(Window, true);
        Window.SetSoftInputMode(SoftInput.AdjustResize);
    }
}
