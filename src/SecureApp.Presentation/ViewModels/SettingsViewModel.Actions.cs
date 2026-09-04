using CommunityToolkit.Mvvm.Input;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The one <see cref="SettingsViewModel"/> command that needs a MAUI type (<c>Clipboard</c>) — see the class-level remarks on the other partial for why this is split out.</summary>
public sealed partial class SettingsViewModel
{
    [RelayCommand]
    private async Task CopyContactCardAsync()
    {
        if (string.IsNullOrEmpty(ContactCardText)) return;
        await Clipboard.Default.SetTextAsync(ContactCardText);
    }

    [RelayCommand]
    private async Task CopySharedLibraryKeyAsync()
    {
        if (string.IsNullOrEmpty(SharedLibraryKeyBlob)) return;
        await Clipboard.Default.SetTextAsync(SharedLibraryKeyBlob);
    }

    [RelayCommand]
    private async Task CopyGeneratedInviteCodeAsync()
    {
        if (string.IsNullOrEmpty(GeneratedInviteCodeText)) return;
        await Clipboard.Default.SetTextAsync(GeneratedInviteCodeText);
    }
}
