using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Screen-capture protection is a CHAT-only feature (user's decision 2026-10-18): documents and
/// learning materials are not protected. Called whenever the visible page may have changed; turns the
/// native block on only while a chat page is showing, off everywhere else. The admin debugging
/// override (<see cref="ScreenCaptureDebugOverride"/>) still disables it everywhere, chat included.
/// </summary>
public static class ScreenCaptureGuard
{
    public static void Refresh()
    {
        var dlp = IPlatformApplication.Current?.Services.GetService<INativeDlpService>();
        if (dlp is null) return;

        var protect = !ScreenCaptureDebugOverride.IsActive && IsChatPage(Shell.Current?.CurrentPage);
        if (protect) dlp.PreventScreenCapture();
        else dlp.AllowScreenCapture();
    }

    private static bool IsChatPage(Page? page) =>
        page is ChatListPage or ChatPage or GroupChatPage or NewChatPage or NewGroupPage;
}
