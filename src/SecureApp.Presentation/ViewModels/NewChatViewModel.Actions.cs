using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The <see cref="NewChatViewModel"/> members that need MAUI types (<c>Clipboard</c>, <c>Shell</c>, <c>ImageSource</c>) — see the class-level remarks on the other partial for why these are split out.</summary>
public sealed partial class NewChatViewModel
{
    /// <summary>Rendered from <see cref="GeneratedInviteQrValue"/> via <see cref="QrImageGenerator"/> — see SettingsViewModel.ContactCardQrImage's own remarks for why (BarcodeGeneratorView renders nothing on Windows).</summary>
    [ObservableProperty]
    public partial ImageSource? GeneratedInviteQrImage { get; set; }

    partial void OnGeneratedInviteQrValueChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            GeneratedInviteQrImage = null;
            return;
        }

        // See SettingsViewModel.OnContactCardQrValueChanged's identical remark — must never crash
        // the app just because QR rendering failed.
        try
        {
            var png = QrImageGenerator.GeneratePng(value);
            GeneratedInviteQrImage = ImageSource.FromStream(() => new MemoryStream(png));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QR generation failed for invite: {ex}");
            GeneratedInviteQrImage = null;
        }
    }

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
