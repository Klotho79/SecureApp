using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The two <see cref="ChatListViewModel"/> commands that need <c>Shell</c> navigation — see the class-level remarks on the other partial for why these are split out.</summary>
public sealed partial class ChatListViewModel
{
    [RelayCommand]
    private async Task OpenSessionAsync(ChatSessionItem? session)
    {
        if (session is null) return;
        // animate:false (2026-09-12): the janky Shell slide is dropped for an instant cut (measured: the
        // open cost is synchronous page layout, not data). PendingKey (2026-09-13) lets the caching route
        // factory reuse this chat's already-built page on revisit — see CachedRouteFactory.
        AppShell.ChatPageFactory.PendingKey = session.Id.ToString();
        await Shell.Current.GoToAsync($"{nameof(ChatPage)}?chatSessionId={session.Id}", animate: false);
    }

    [RelayCommand]
    private async Task NewChatAsync()
    {
        await Shell.Current.GoToAsync(nameof(NewChatPage));
    }

    [RelayCommand]
    private async Task OpenGroupAsync(GroupChatListItem? group)
    {
        if (group is null) return;
        AppShell.GroupChatPageFactory.PendingKey = group.Id.ToString();
        await Shell.Current.GoToAsync($"{nameof(GroupChatPage)}?groupChatId={group.Id}", animate: false);
    }

    [RelayCommand]
    private async Task NewGroupAsync()
    {
        await Shell.Current.GoToAsync(nameof(NewGroupPage));
    }
}
