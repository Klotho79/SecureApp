using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Opicentrum/ARO portal login (2026-09-21, NOTIFICATION_HUB_SPEC.md Phase 5 follow-up, user's own
/// ask: "zdroj by mela byt stranka opicentrum.cz... nevim jak te pustit dovnitr bez abych ti dal login
/// a heslo") — a THIRD-PARTY credential, entered here once per device and stored in this device's own
/// encrypted vault (see <c>OpicentrumVaultKeys</c>), never routed through chat/dev tooling. The user's
/// own follow-up ask: "apka nesmí vědet login a heslo [ve smyslu: nemá se pořád ptát] — bude si
/// pamatovat co bylo vloženo, aby se uživatel nemusel pořád přihlašovat" — enter once, the app logs in
/// on the user's behalf on every sync from then on (see <c>OpicentrumSyncService.SyncAsync</c>).
/// Split into its own partial file — same "give a large ViewModel's concerns their own file" precedent
/// this codebase already established elsewhere.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial string OpicentrumUsernameInput { get; set; }

    [ObservableProperty]
    public partial string OpicentrumPasswordInput { get; set; }

    [ObservableProperty]
    public partial bool HasOpicentrumCredentials { get; set; }

    [ObservableProperty]
    public partial bool IsSavingOpicentrumCredentials { get; set; }

    [ObservableProperty]
    public partial bool CanSaveOpicentrumCredentials { get; set; }

    [ObservableProperty]
    public partial string? OpicentrumStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasOpicentrumStatus { get; set; }

    partial void OnIsSavingOpicentrumCredentialsChanged(bool value) => CanSaveOpicentrumCredentials = !value;
    partial void OnOpicentrumStatusTextChanged(string? value) => HasOpicentrumStatus = !string.IsNullOrEmpty(value);

    private async Task LoadOpicentrumStateAsync()
    {
        HasOpicentrumCredentials = await _opicentrumSyncService.HasCredentialsAsync();
        OpicentrumUsernameInput = string.Empty;
        OpicentrumPasswordInput = string.Empty;
        OpicentrumStatusText = null;
        CanSaveOpicentrumCredentials = true;
    }

    [RelayCommand]
    private async Task SaveOpicentrumCredentialsAsync()
    {
        OpicentrumStatusText = null;
        var username = OpicentrumUsernameInput?.Trim() ?? string.Empty;
        var password = OpicentrumPasswordInput ?? string.Empty;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            OpicentrumStatusText = "Zadejte uživatelské jméno i heslo.";
            return;
        }

        IsSavingOpicentrumCredentials = true;
        try
        {
            await _opicentrumSyncService.SetCredentialsAsync(username, password);
            HasOpicentrumCredentials = true;
            OpicentrumUsernameInput = string.Empty;
            OpicentrumPasswordInput = string.Empty;
            OpicentrumStatusText = "Uloženo — appka se od teď přihlašuje sama při každém otevření Rozpisu.";
        }
        catch (Exception ex)
        {
            OpicentrumStatusText = $"Nepodařilo se uložit: {ex.Message}";
        }
        finally
        {
            IsSavingOpicentrumCredentials = false;
        }
    }

    [RelayCommand]
    private async Task ClearOpicentrumCredentialsAsync()
    {
        try
        {
            await _opicentrumSyncService.ClearCredentialsAsync();
            HasOpicentrumCredentials = false;
            OpicentrumStatusText = "Přihlašovací údaje odstraněny.";
        }
        catch (Exception ex)
        {
            OpicentrumStatusText = $"Nepodařilo se odstranit: {ex.Message}";
        }
    }
}
