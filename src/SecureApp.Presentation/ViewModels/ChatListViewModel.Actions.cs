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
        // animate:false (2026-09-12): the Shell push slide competed with the fresh page's ~66 ms XAML
        // inflation on the one UI thread and stuttered ("trhalo"). Measurement showed the jank is that
        // synchronous build, not data/cell work, and it happens on every (transient) open — so we drop
        // the slide for an instant, clean cut instead. The chat VM populates immediately (no settle wait).
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
        await Shell.Current.GoToAsync($"{nameof(GroupChatPage)}?groupChatId={group.Id}", animate: false);
    }

    [RelayCommand]
    private async Task NewGroupAsync()
    {
        await Shell.Current.GoToAsync(nameof(NewGroupPage));
    }
}
