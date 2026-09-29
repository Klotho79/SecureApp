using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Identity backup/restore (2026-09-29, disaster recovery — see <see cref="IIdentityBackupService"/>'s
/// own remarks for the incident that prompted this). Two halves of the same "Relay" card in
/// SettingsPage.xaml: "Zálohovat identitu" shows once registered (there's an identity worth backing
/// up), "Obnovit identitu ze zálohy" shows instead of it while unregistered — restoring identity FIRST
/// means the activation request that follows carries the RESTORED identity's key fingerprint, so
/// existing peers see the same person reconnecting, not a stranger.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial string IdentityBackupEmailText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IdentityBackupPassphraseText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IdentityBackupPassphraseConfirmText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBackingUpIdentity { get; set; }

    [ObservableProperty]
    public partial bool CanBackupIdentity { get; set; } = true;

    [ObservableProperty]
    public partial string? IdentityBackupStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasIdentityBackupStatus { get; set; }

    [ObservableProperty]
    public partial string RestoreEmailText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RestorePassphraseText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRestoringIdentity { get; set; }

    [ObservableProperty]
    public partial bool CanRestoreIdentity { get; set; } = true;

    [ObservableProperty]
    public partial string? RestoreStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasRestoreStatus { get; set; }

    partial void OnIdentityBackupStatusTextChanged(string? value) => HasIdentityBackupStatus = !string.IsNullOrEmpty(value);
    partial void OnRestoreStatusTextChanged(string? value) => HasRestoreStatus = !string.IsNullOrEmpty(value);
    partial void OnIsBackingUpIdentityChanged(bool value) => CanBackupIdentity = !value;
    partial void OnIsRestoringIdentityChanged(bool value) => CanRestoreIdentity = !value;

    [RelayCommand]
    private async Task BackupIdentityAsync()
    {
        IdentityBackupStatusText = null;
        if (string.IsNullOrWhiteSpace(IdentityBackupEmailText))
        {
            IdentityBackupStatusText = "Zadejte e-mail — pomocí něj (a hesla) záloha najdete na relay serveru.";
            return;
        }
        if (IdentityBackupPassphraseText.Length < 8)
        {
            IdentityBackupStatusText = "Heslo musí mít aspoň 8 znaků — jím je záloha zašifrovaná, ať ji nikdo jiný nepřečte.";
            return;
        }
        if (IdentityBackupPassphraseText != IdentityBackupPassphraseConfirmText)
        {
            IdentityBackupStatusText = "Hesla se neshodují.";
            return;
        }

        IsBackingUpIdentity = true;
        try
        {
            var path = await _identityBackupService.BackupAsync(IdentityBackupEmailText.Trim(), IdentityBackupPassphraseText);
            IdentityBackupStatusText = $"Hotovo — soubor uložte mimo appku (nabídlo se sdílení), a zapamatujte si e-mail a heslo pro obnovu. Soubor: {path}";
            IdentityBackupPassphraseText = string.Empty;
            IdentityBackupPassphraseConfirmText = string.Empty;
        }
        catch (Exception ex)
        {
            IdentityBackupStatusText = $"Zálohu se nepodařilo vytvořit: {ex.Message}";
        }
        finally
        {
            IsBackingUpIdentity = false;
        }
    }

    [RelayCommand]
    private async Task RestoreIdentityFromRelayAsync()
    {
        RestoreStatusText = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            RestoreStatusText = "Zadejte nejdřív platnou adresu relay serveru výše.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RestoreEmailText) || RestorePassphraseText.Length == 0)
        {
            RestoreStatusText = "Zadejte e-mail a heslo, které jste použili při zálohování.";
            return;
        }

        IsRestoringIdentity = true;
        try
        {
            await _identityBackupService.RestoreFromRelayAsync(endpoint, RestoreEmailText.Trim(), RestorePassphraseText);
            await AfterIdentityRestoredAsync(RestoreEmailText.Trim());
        }
        catch (Exception ex)
        {
            RestoreStatusText = $"Obnova se nezdařila: {ex.Message}";
        }
        finally
        {
            IsRestoringIdentity = false;
        }
    }

    [RelayCommand]
    private async Task RestoreIdentityFromFileAsync()
    {
        RestoreStatusText = null;
        if (RestorePassphraseText.Length == 0)
        {
            RestoreStatusText = "Zadejte nejdřív heslo, kterým je soubor zálohy zašifrovaný.";
            return;
        }

        IsRestoringIdentity = true;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Vyberte soubor zálohy identity" });
            if (pick is null) return;

            using var stream = await pick.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            await _identityBackupService.RestoreFromFileAsync(buffer.ToArray(), RestorePassphraseText);
            await AfterIdentityRestoredAsync(RestoreEmailText.Trim());
        }
        catch (Exception ex)
        {
            RestoreStatusText = $"Obnova se nezdařila: {ex.Message}";
        }
        finally
        {
            IsRestoringIdentity = false;
        }
    }

    /// <summary>
    /// The restored identity's key fingerprint must reach the relay's activation request, so this
    /// chains straight into the existing (now-instant, auto-approved — see Program.cs's own remarks)
    /// activation flow rather than leaving the user to separately notice and press "Aktivovat" next.
    /// </summary>
    private async Task AfterIdentityRestoredAsync(string email)
    {
        RestoreStatusText = "Identita obnovena — registruji zařízení u relay serveru…";
        RestorePassphraseText = string.Empty;
        if (!string.IsNullOrWhiteSpace(email))
            ActivationEmailText = email;
        await RequestActivationAsync();
    }
}
