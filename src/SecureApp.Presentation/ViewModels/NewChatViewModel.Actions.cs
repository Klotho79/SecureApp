using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The <see cref="NewChatViewModel"/> commands that need MAUI types (<c>Clipboard</c>, <c>Shell</c>) — see the class-level remarks on the other partial for why these are split out.</summary>
public sealed partial class NewChatViewModel
{
    [RelayCommand]
    private async Task CopyInviteAsync()
    {
        if (string.IsNullOrEmpty(GeneratedInviteText)) return;
        await Clipboard.Default.SetTextAsync(GeneratedInviteText);
    }

    [RelayCommand]
    private async Task OpenCreatedChatAsync()
    {
        if (CreatedSession is null) return;
        await Shell.Current.GoToAsync($"{nameof(ChatPage)}?chatSessionId={CreatedSession.Id}");
    }

    [RelayCommand]
    private async Task OpenAcceptedChatAsync()
    {
        if (AcceptedSession is null) return;
        await Shell.Current.GoToAsync($"{nameof(ChatPage)}?chatSessionId={AcceptedSession.Id}");
    }
}
