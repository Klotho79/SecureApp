namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Whether Android lets SecureApp run in the background (battery-optimization exemption), and taking
/// the user there with an explanation (2026-09-26, user's ask: the app itself leads the member to the
/// setting and explains why). Without the exemption Doze cuts the relay connection and notifications
/// only arrive the next time the app is opened. No-op off Android.
/// </summary>
public static class BackgroundRun
{
    private const string LastPromptPreferenceKey = "bg_run_last_prompt_utc";
    private static readonly TimeSpan PromptInterval = TimeSpan.FromDays(3);

    public const string Title = "Zprávy i při zavřené aplikaci";

    public const string Explanation =
        "Aby vám zprávy a oznámení chodily i když je SecureApp zavřený, musí mít povoleno běžet na pozadí. " +
        "Jinak ho Android po chvíli uspí a zprávy dorazí až při dalším otevření aplikace.\n\n" +
        "Klepněte na „Nastavit“ a v dalším okně zvolte „Povolit“.\n\n" +
        "V liště pak uvidíte malé trvalé upozornění „SecureApp je připojen“ — to je v pořádku, znamená to, že aplikace přijímá zprávy.";

    public static bool IsSupported => DeviceInfo.Current.Platform == DevicePlatform.Android;

    public static bool IsAllowed()
    {
#if ANDROID
        try
        {
            var context = Platform.AppContext;
            var power = context.GetSystemService(global::Android.Content.Context.PowerService) as global::Android.OS.PowerManager;
            return power?.IsIgnoringBatteryOptimizations(context.PackageName) ?? true;
        }
        catch
        {
            return true;
        }
#else
        return true;
#endif
    }

    /// <summary>Opens Android's "allow background" prompt for this app; falls back to the app's own settings page.</summary>
    public static void OpenSettings()
    {
#if ANDROID
        var context = Platform.CurrentActivity ?? (global::Android.Content.Context)Platform.AppContext;
        try
        {
            var intent = new global::Android.Content.Intent(
                global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations,
                global::Android.Net.Uri.Parse("package:" + context.PackageName));
            if (context is not global::Android.App.Activity) intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch
        {
            try
            {
                var fallback = new global::Android.Content.Intent(
                    global::Android.Provider.Settings.ActionApplicationDetailsSettings,
                    global::Android.Net.Uri.Parse("package:" + context.PackageName));
                fallback.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                context.StartActivity(fallback);
            }
            catch (Exception ex)
            {
                AppLog.Error(nameof(BackgroundRun), "opening battery settings failed", ex);
            }
        }
#endif
    }

    /// <summary>On app start: if background running isn't allowed yet, explain and offer to open the setting — at most once every few days.</summary>
    public static async Task PromptIfNeededAsync(Page page)
    {
        if (!IsSupported || IsAllowed()) return;

        var prefs = Microsoft.Maui.Storage.Preferences.Default;
        var lastTicks = prefs.Get(LastPromptPreferenceKey, 0L);
        if (lastTicks > 0 && DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero) < PromptInterval) return;
        prefs.Set(LastPromptPreferenceKey, DateTimeOffset.UtcNow.UtcTicks);

        if (await page.DisplayAlertAsync(Title, Explanation, "Nastavit", "Později"))
            OpenSettings();
    }
}
