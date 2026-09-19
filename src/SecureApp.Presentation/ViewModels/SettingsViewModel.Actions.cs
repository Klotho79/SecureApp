using CommunityToolkit.Mvvm.Input;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The <see cref="SettingsViewModel"/> members that need a MAUI type (<c>Clipboard</c>) — see the class-level remarks on the other partial for why this is split out.</summary>
public sealed partial class SettingsViewModel
{
    [RelayCommand]
    private async Task CopySharedLibraryKeyAsync()
    {
        if (string.IsNullOrEmpty(SharedLibraryKeyBlob)) return;
        await Clipboard.Default.SetTextAsync(SharedLibraryKeyBlob);
    }
}
