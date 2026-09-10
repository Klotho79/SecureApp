using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Chat;

namespace SecureApp.Presentation.ViewModels;

/// <summary>The <see cref="SettingsViewModel"/> members that need a MAUI type (<c>Clipboard</c>, <c>ImageSource</c>) — see the class-level remarks on the other partial for why this is split out.</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>
    /// Rendered from <see cref="SettingsViewModel.ContactCardQrValue"/> via <see cref="QrImageGenerator"/>
    /// rather than binding a <c>zxing:BarcodeGeneratorView</c> directly — that control renders
    /// nothing at all on Windows (confirmed live 2026-09-05: identical data shows fine on Android,
    /// blank on Windows, no exception either side) — see QrImageGenerator's own remarks.
    /// </summary>
    [ObservableProperty]
    public partial ImageSource? ContactCardQrImage { get; set; }

    partial void OnContactCardQrValueChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            ContactCardQrImage = null;
            return;
        }

        // A failure here (encoding or platform-specific rendering) must never take the whole app
        // down with it — worst case is just no QR image, same as if "Show QR" were never clicked.
        try
        {
            var png = QrImageGenerator.GeneratePng(value, errorCorrection: ZXing.QrCode.Internal.ErrorCorrectionLevel.Q);
            ContactCardQrImage = ImageSource.FromStream(() => new MemoryStream(png));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QR generation failed for contact card: {ex}");
            ContactCardQrImage = null;
        }
    }

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
        if (string.IsNullOrEmpty(GeneratedInviteCode)) return;
        await Clipboard.Default.SetTextAsync(GeneratedInviteCode);
    }
}
