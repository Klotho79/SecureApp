using System.Runtime.InteropServices;
using SecureApp.Domain.Interfaces.Services;
using WinRT.Interop;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="INativeDlpService"/>
/// <remarks>
/// Windows implementation: <c>SetWindowDisplayAffinity(WDA_MONITOR)</c> via P/Invoke — the
/// window still renders normally on the physical monitor, but is excluded from anything that
/// captures window contents off-screen (Print Screen, screen recording, remote desktop/screen
/// sharing). Verified: builds clean for net10.0-windows10.0.19041.0 and the app still launches
/// and runs after calling this (see DEVELOPMENT_PLAN.md's Milestone 4 verification note) — the
/// affinity's actual screenshot-blocking effect itself was not independently confirmed (would
/// need an external capture tool run against the live window, not attempted here).
/// </remarks>
public sealed class NativeDlpService : INativeDlpService
{
    private const uint WdaMonitor = 0x00000001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public void PreventScreenCapture()
    {
        var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window winUiWindow)
            return;

        var hwnd = WindowNative.GetWindowHandle(winUiWindow);
        SetWindowDisplayAffinity(hwnd, WdaMonitor);
    }
}
