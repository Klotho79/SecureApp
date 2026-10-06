namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Real crash found live (KNOWN_ISSUES.md #4, open since 2026-09-29, confirmed again on Vilém's
/// S23+ 2026-10-06): a native Android focus-change callback (<c>View.OnFocusChange</c>) can land on
/// the UI thread AFTER Shell has already navigated away from the page and torn down that page's
/// handler/DI scope. <c>InputView.MapIsFocused</c> then calls
/// <c>handler.GetService&lt;HideSoftInputOnTappedChangedManager&gt;()</c>, which throws
/// <see cref="ObjectDisposedException"/> on the already-disposed <see cref="IServiceProvider"/> —
/// terminates the whole process, not a catchable .NET exception at the call site, since it comes in
/// through a JNI callback. Every occurrence so far (device_app_logs, relay.db3) has the identical
/// stack through InputView.MapIsFocused, confirming this is a MAUI/Android framework timing race,
/// not anything about this app's own search/text content. Fixed the only place it CAN be fixed —
/// wrapping the handler's own property mapping — since by the time this fires, the page is already
/// gone and there is nothing left to focus anyway. Same <c>Mapper.AppendToMapping</c>-based handler
/// customization already used by <see cref="FontScaling"/>, just <c>ModifyMapping</c> here since the
/// base mapping itself is what throws (an append runs only AFTER it, too late to catch it).
/// </summary>
public static class InputFocusCrashGuard
{
    public static void Register()
    {
#if ANDROID
        const string key = nameof(Microsoft.Maui.IView.IsFocused);
        Microsoft.Maui.Handlers.EntryHandler.Mapper.ModifyMapping(key, GuardIsFocused);
        Microsoft.Maui.Handlers.EditorHandler.Mapper.ModifyMapping(key, GuardIsFocused);
        Microsoft.Maui.Handlers.SearchBarHandler.Mapper.ModifyMapping(key, GuardIsFocused);
#endif
    }

#if ANDROID
    private static void GuardIsFocused<THandler, TVirtualView>(THandler handler, TVirtualView view, Action<THandler, TVirtualView> baseAction)
    {
        try
        {
            baseAction(handler, view);
        }
        catch (ObjectDisposedException)
        {
            // The page (and its handler's own DI scope) is already gone — nothing left to focus.
        }
    }
#endif
}
