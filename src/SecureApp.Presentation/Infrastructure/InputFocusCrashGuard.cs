using System.Runtime.CompilerServices;

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
    // Recurrence 2026-10-08 (1.54, and AGAIN on 1.55 at 18:42 after the NoInlining "fix" below):
    // same ObjectDisposedException, NO GuardIsFocused frame in the stack. The 10-08 theory (AOT
    // inlining swallowed the EH region) was wrong — NoInlining is harmless and kept, but didn't help.
    // Real cause, read off the stack (2026-10-09): MapIsFocused is called from
    // PropertyMapperExtensions.<AppendToMapping>b__0, i.e. MAUI Controls' own AppendToMapping, which
    // runs inside UseMauiApp. Register() used to run BEFORE UseMauiApp, so at that point the key
    // didn't exist yet (hence the null baseAction below) and Controls' append then wrapped THIS guard
    // and called MapIsFocused after it, outside the try. Register() now runs after UseMauiApp
    // (MauiProgram.cs), so this guard wraps the whole chain.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GuardIsFocused<THandler, TVirtualView>(THandler handler, TVirtualView view, Action<THandler, TVirtualView> baseAction)
    {
        try
        {
            // Null only if registered before MAUI Controls added its own IsFocused mapping (the
            // pre-2026-10-09 ordering bug above) — kept null-safe anyway.
            baseAction?.Invoke(handler, view);
        }
        catch (Exception)
        {
            // Widened from ObjectDisposedException-only (2026-10-08): by the time this fires the page
            // (and its handler's own DI scope) is already gone — nothing left to focus regardless of
            // which exception type a torn-down scope happens to throw.
        }
    }
#endif
}
