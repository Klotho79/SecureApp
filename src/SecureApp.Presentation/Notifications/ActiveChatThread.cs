using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Notifications;

/// <summary>
/// Which chat thread is on screen right now (2026-09-26, user's ask: once a chat is opened, the
/// messages already seen must stop nagging). Opening a thread — or returning to the app while it's
/// open — marks its notifications read and clears them from the Android shade; a message arriving
/// into the open thread while the app is in the foreground raises no notification at all.
/// Driven from the chat ViewModels' StartListening/StopListening, which every host (ChatPage,
/// GroupChatPage, ChatListPage's split/narrow panes) already calls.
/// </summary>
public static class ActiveChatThread
{
    private static readonly object Gate = new();
    private static Guid? _chatSessionId;
    private static Guid? _groupChatId;
    private static string? _peerKeyHex;
    private static bool _isAppInForeground = true;

    public static void EnterDirect(Guid chatSessionId)
    {
        if (chatSessionId == Guid.Empty) return;
        lock (Gate)
        {
            _chatSessionId = chatSessionId;
            _groupChatId = null;
            _peerKeyHex = null;
        }
        _ = MarkActiveThreadReadAsync();
    }

    public static void EnterGroup(Guid groupChatId)
    {
        if (groupChatId == Guid.Empty) return;
        lock (Gate)
        {
            _chatSessionId = null;
            _groupChatId = groupChatId;
            _peerKeyHex = null;
        }
        _ = MarkActiveThreadReadAsync();
    }

    /// <summary>Clears any open 1:1 thread without comparing ids — auto-heal may have repointed the ViewModel to a fresh session since it entered.</summary>
    public static void LeaveDirect()
    {
        lock (Gate)
        {
            if (_chatSessionId is null) return;
            _chatSessionId = null;
            _peerKeyHex = null;
        }
    }

    public static void LeaveGroup(Guid groupChatId)
    {
        lock (Gate)
        {
            if (_groupChatId != groupChatId) return;
            _groupChatId = null;
        }
    }

    public static void OnAppStopped()
    {
        lock (Gate) _isAppInForeground = false;
    }

    public static void OnAppResumed()
    {
        lock (Gate) _isAppInForeground = true;
        _ = MarkActiveThreadReadAsync();
    }

    /// <summary>True when the user is looking at this exact thread right now — a 1:1 thread matches by peer key, since resync replaces the session id.</summary>
    public static bool IsOpen(Guid? groupChatId, byte[] peerIdentityPublicKey)
    {
        lock (Gate)
        {
            if (!_isAppInForeground) return false;
            if (groupChatId is { } groupId) return _groupChatId == groupId;
            return _peerKeyHex is not null && _peerKeyHex == Convert.ToHexStringLower(peerIdentityPublicKey);
        }
    }

    private static async Task MarkActiveThreadReadAsync()
    {
        try
        {
            Guid? chatSessionId, groupChatId;
            lock (Gate)
            {
                if (!_isAppInForeground) return;
                chatSessionId = _chatSessionId;
                groupChatId = _groupChatId;
            }
            if (chatSessionId is null && groupChatId is null) return;

            var services = IPlatformApplication.Current?.Services;
            if (services is null) return;
            using var scope = services.CreateScope();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var native = scope.ServiceProvider.GetService<INativeNotificationService>();

            IReadOnlyList<Guid> readIds;
            if (groupChatId is { } groupId)
            {
                readIds = await notifications.MarkThreadReadAsync([], groupId);
            }
            else
            {
                var sessions = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
                var current = await sessions.GetByIdAsync(chatSessionId!.Value);
                if (current is null) return;
                var peerKeyHex = Convert.ToHexStringLower(current.PeerIdentityPublicKey);
                lock (Gate)
                {
                    if (_chatSessionId == chatSessionId) _peerKeyHex = peerKeyHex;
                }

                var peerSessionIds = (await sessions.GetAllAsync())
                    .Where(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey) == peerKeyHex)
                    .Select(s => s.Id)
                    .ToList();
                readIds = await notifications.MarkThreadReadAsync(peerSessionIds, null);
            }

            foreach (var id in readIds)
                native?.CancelNotification(id);
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(ActiveChatThread), "marking chat notifications read failed", ex);
        }
    }
}
