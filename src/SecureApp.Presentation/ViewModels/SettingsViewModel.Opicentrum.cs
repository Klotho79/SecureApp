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

    // Nurses' roster login (2026-10-28, the unstaffed-position alerts) — a separate shared viewer account for
    // opicentrum.cz/AROS, only meaningful on the Admin/Modifier devices that run the check (StaffingGapChecker).
    [ObservableProperty]
    public partial bool CanSetNurseCredentials { get; set; }

    [ObservableProperty]
    public partial bool HasNurseCredentials { get; set; }

    [ObservableProperty]
    public partial string NurseUsernameInput { get; set; }

    [ObservableProperty]
    public partial string NursePasswordInput { get; set; }

    [ObservableProperty]
    public partial string? NurseStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasNurseStatus { get; set; }

    partial void OnNurseStatusTextChanged(string? value) => HasNurseStatus = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task SaveNurseCredentialsAsync()
    {
        NurseStatusText = null;
        var username = NurseUsernameInput?.Trim() ?? string.Empty;
        var password = NursePasswordInput ?? string.Empty;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            NurseStatusText = "Zadejte jméno i heslo.";
            return;
        }

        try
        {
            await _opicentrumSyncService.SetNurseCredentialsAsync(username, password);
            HasNurseCredentials = true;
            NurseUsernameInput = string.Empty;
            NursePasswordInput = string.Empty;
            NurseStatusText = "Uloženo — kontrola obsazení pracovišť poběží sama na pozadí.";
        }
        catch (Exception ex)
        {
            NurseStatusText = $"Nepodařilo se uložit: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearNurseCredentialsAsync()
    {
        try
        {
            await _opicentrumSyncService.ClearNurseCredentialsAsync();
            HasNurseCredentials = false;
            NurseStatusText = "Údaje odstraněny.";
        }
        catch (Exception ex)
        {
            NurseStatusText = $"Nepodařilo se odstranit: {ex.Message}";
        }
    }

    private async Task LoadOpicentrumStateAsync()
    {
        CanSetNurseCredentials = _currentUserService.Current.Role is SecureApp.Domain.Enums.Role.Admin or SecureApp.Domain.Enums.Role.Modifier;
        HasNurseCredentials = await _opicentrumSyncService.HasNurseCredentialsAsync();
        NurseUsernameInput = string.Empty;
        NursePasswordInput = string.Empty;
        HasOpicentrumCredentials = await _opicentrumSyncService.HasCredentialsAsync();
        OpicentrumUsernameInput = string.Empty;
        OpicentrumPasswordInput = string.Empty;
        OpicentrumStatusText = _opicentrumCredentialsRequested && !HasOpicentrumCredentials ? Workplace.OpicentrumCredentialsRequest.MissingText : null;
        _opicentrumCredentialsRequested = false;
        CanSaveOpicentrumCredentials = true;
    }

    private bool _opicentrumCredentialsRequested;

    /// <summary>
    /// Called by the page on appearing, before Load (2026-10-26): when an Opicentrum action sent the user here for
    /// missing credentials (<see cref="Workplace.OpicentrumCredentialsRequest"/>), switch to Systém → Opicentrum at
    /// once and keep the message through the load. True = the page should put the cursor in the username field.
    /// </summary>
    public bool ApplyPendingOpicentrumCredentialsRequest()
    {
        if (!Workplace.OpicentrumCredentialsRequest.TryConsume()) return false;
        _opicentrumCredentialsRequested = true;
        SettingsTab = 1;
        SystemSubTab = 2;
        OpicentrumStatusText = Workplace.OpicentrumCredentialsRequest.MissingText;
        return true;
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

    [ObservableProperty]
    public partial string? OpicentrumNameCheckReport { get; set; }

    [ObservableProperty]
    public partial bool HasOpicentrumNameCheckReport { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingOpicentrumNames { get; set; }

    public bool CanCheckOpicentrumNames => !IsCheckingOpicentrumNames;

    partial void OnIsCheckingOpicentrumNamesChanged(bool value) => OnPropertyChanged(nameof(CanCheckOpicentrumNames));
    partial void OnOpicentrumNameCheckReportChanged(string? value) => HasOpicentrumNameCheckReport = !string.IsNullOrEmpty(value);

    /// <summary>ARIM contact list vs Opicentrum names (2026-10-10) — logs in with THIS device's stored credentials, so nothing about the login ever leaves it. See <see cref="Workplace.OpicentrumNameCheck"/>.</summary>
    [RelayCommand]
    private async Task CheckOpicentrumNamesAsync()
    {
        IsCheckingOpicentrumNames = true;
        OpicentrumNameCheckReport = "Přihlašuji se do Opicentra a porovnávám…";
        try
        {
            var opicentrumNames = await _opicentrumSyncService.FetchStaffNamesAsync(DateOnly.FromDateTime(DateTime.Today));
            var contacts = await _sharedContactService.FetchAsync();
            OpicentrumNameCheckReport = opicentrumNames.Count == 0
                ? "Z Opicentra se nepodařilo načíst žádná jména (stránky se možná změnily)."
                : Workplace.OpicentrumNameCheck.BuildReport(contacts.Select(c => c.DisplayName), opicentrumNames);
        }
        catch (Exception ex)
        {
            OpicentrumNameCheckReport = $"Porovnání se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsCheckingOpicentrumNames = false;
        }
    }

    [RelayCommand]
    private async Task CopyOpicentrumNameCheckReportAsync()
    {
        if (OpicentrumNameCheckReport is { Length: > 0 } report)
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(report);
    }

    [ObservableProperty]
    public partial string? OpicentrumWorkplaceCheckReport { get; set; }

    [ObservableProperty]
    public partial bool HasOpicentrumWorkplaceCheckReport { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingOpicentrumWorkplaces { get; set; }

    public bool CanCheckOpicentrumWorkplaces => !IsCheckingOpicentrumWorkplaces;

    partial void OnIsCheckingOpicentrumWorkplacesChanged(bool value) => OnPropertyChanged(nameof(CanCheckOpicentrumWorkplaces));
    partial void OnOpicentrumWorkplaceCheckReportChanged(string? value) => HasOpicentrumWorkplaceCheckReport = !string.IsNullOrEmpty(value);

    /// <summary>
    /// 2026-10-14, user's own ask: "stáhni kompletní sálové rozlišení, všechny pozice které obsahují
    /// ve jméně sál nebo oční nebo ODS nebo ostatní" — this week's pracoviste.php roster, EVERY
    /// distinct workplace label across every rostered person (not just this device's own), filtered
    /// to the same keyword set the Nástěnka "Status" gate itself uses (<see cref="Workplace.RoomWorkplaceKeywords"/>,
    /// reused directly — not duplicated — so the two can never drift apart again; narrowed 2026-10-15
    /// to just "sál" per the user's own correction, see that type's remarks).
    /// </summary>
    [RelayCommand]
    private async Task CheckOpicentrumWorkplacesAsync()
    {
        IsCheckingOpicentrumWorkplaces = true;
        OpicentrumWorkplaceCheckReport = "Přihlašuji se do Opicentra a stahuji rozvržení…";
        try
        {
            var allNames = await _opicentrumSyncService.FetchWorkplaceNamesAsync(DateOnly.FromDateTime(DateTime.Today));
            var matching = allNames.Where(Workplace.RoomWorkplaceKeywords.IsQualifying).ToList();

            OpicentrumWorkplaceCheckReport = matching.Count == 0
                ? $"Žádná pozice z tohoto týdne neobsahuje sál (celkem {allNames.Count} pozic nalezeno)."
                : $"Pozice obsahující sál ({matching.Count} z {allNames.Count} celkem):\n" + string.Join("\n", matching.Select(n => "• " + n));
        }
        catch (Exception ex)
        {
            OpicentrumWorkplaceCheckReport = $"Stažení se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsCheckingOpicentrumWorkplaces = false;
        }
    }

    [RelayCommand]
    private async Task CopyOpicentrumWorkplaceCheckReportAsync()
    {
        if (OpicentrumWorkplaceCheckReport is { Length: > 0 } report)
            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.SetTextAsync(report);
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
