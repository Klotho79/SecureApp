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
        await Shell.Current.GoToAsync($"{nameof(ChatPage)}?chatSessionId={session.Id}");
    }

    [RelayCommand]
    private async Task NewChatAsync()
    {
        await Shell.Current.GoToAsync(nameof(NewChatPage));
    }
}
