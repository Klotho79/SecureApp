namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Navigation for page code-behind handlers that are <c>async void</c> (event handlers can't return a
/// Task): a failed <c>Shell.GoToAsync</c> there (double tap racing a running navigation, unknown
/// route, Shell not ready) would otherwise surface as an unhandled exception and take the app down.
/// Logged instead; the user simply stays on the current page and can tap again.
/// </summary>
internal static class ShellNavigation
{
    public static async Task GoToAsync(string route)
    {
        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            AppLog.Warning(nameof(ShellNavigation), $"Navigace na '{route}' se nezdařila.", ex);
        }
    }
}
