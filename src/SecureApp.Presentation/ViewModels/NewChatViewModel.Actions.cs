using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The <see cref="NewChatViewModel"/> members that need MAUI types (<c>Shell</c>) — see the class-level remarks on the other partial for why these are split out.</summary>
public sealed partial class NewChatViewModel
{
    [RelayCommand]
    private async Task OpenCreatedChatAsync()
    {
        if (CreatedSession is null) return;
        AppShell.ChatPageFactory.PendingKey = CreatedSession.Id.ToString();
        await Shell.Current.GoToAsync($"{nameof(ChatPage)}?chatSessionId={CreatedSession.Id}", animate: false); // see ChatListViewModel.OpenSessionAsync
    }
}
