using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Workplace;

/// <summary>
/// No Opicentrum credentials → straight to Nastavení → Systém → Opicentrum, with a message and the cursor in the
/// username field (2026-10-26, user: "nevytvářej, už je v nastavení, jen ho otevři a nastav kurzor s hláškou").
/// <see cref="EnsureAsync"/> leaves a pending request and switches to the Settings tab; <c>SettingsPage</c> picks
/// it up on appearing (<see cref="TryConsume"/>). The page that asked keeps its place in its own tab, so going back
/// after saving returns to it and its next load finds the credentials.
/// </summary>
public static class OpicentrumCredentialsRequest
{
    public const string MissingText = "Pro tuto funkci je třeba nastavit přihlašovací údaje do Opicentra.";

    private static int _pending;

    /// <summary>
    /// True when this device has Opicentrum credentials. Otherwise opens the Settings section and returns false —
    /// unless <paramref name="alreadyPrompted"/> (an automatic check on a page the user came back to without saving
    /// must not bounce them to Settings again); an explicit user action always passes false.
    /// </summary>
    public static async Task<bool> EnsureAsync(IOpicentrumSyncService opicentrumSyncService, bool alreadyPrompted = false)
    {
        if (await opicentrumSyncService.HasCredentialsAsync()) return true;
        if (!alreadyPrompted)
        {
            Interlocked.Exchange(ref _pending, 1);
            await MainThread.InvokeOnMainThreadAsync(() => ShellNavigation.GoToAsync("//SettingsTab"));
        }
        return false;
    }

    public static bool TryConsume() => Interlocked.Exchange(ref _pending, 0) == 1;
}
