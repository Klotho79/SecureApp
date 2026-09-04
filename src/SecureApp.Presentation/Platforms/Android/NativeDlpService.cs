using Android.Views;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="INativeDlpService"/>
/// <remarks>
/// Android implementation: <c>WindowManager.LayoutParams.FLAG_SECURE</c> on the current
/// Activity's window — blocks screenshots and screen recording of this app at the OS level
/// (the captured frame shows black instead), and hides the app's content from the recent-apps
/// thumbnail. UNVERIFIED: no Android emulator/device build has been run in this project at all
/// yet (Windows-only dev environment so far) — only confirmed this compiles against the
/// Android API surface (Android.Views.WindowManagerFlags.Secure,
/// Microsoft.Maui.ApplicationModel.Platform.CurrentActivity), not that it runs.
/// </remarks>
public sealed class NativeDlpService : INativeDlpService
{
    public void PreventScreenCapture()
    {
        Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.AddFlags(WindowManagerFlags.Secure);
    }
}
