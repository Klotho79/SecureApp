using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Translation;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Admin-only local-AI PDF translation config (2026-10-05, user's own ask) — split into its own
/// partial the same way Updates/LibraryDocumentAudit already are, rather than growing the already-
/// large main SettingsViewModel.cs further. Base URL + model name are not secrets, so plain
/// <see cref="LocalAiTranslationSettings"/> (Preferences-backed), not the secure vault.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial string LocalAiBaseUrlText { get; set; } = LocalAiTranslationSettings.DefaultBaseUrl;

    [ObservableProperty]
    public partial string LocalAiModelNameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? LocalAiTranslationStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasLocalAiTranslationStatus { get; set; }

    [ObservableProperty]
    public partial bool IsTestingLocalAiConnection { get; set; }

    /// <summary>Derived, not a value converter — same established pattern this app already uses throughout instead of IValueConverter classes (see IUpdateService.Updates's CanDownloadUpdate).</summary>
    public bool CanTestLocalAiConnection => !IsTestingLocalAiConnection;

    partial void OnIsTestingLocalAiConnectionChanged(bool value) => OnPropertyChanged(nameof(CanTestLocalAiConnection));

    /// <summary>
    /// Admin-only AND Windows-only — direct role check (this codebase's RbacAction/RoleAccessPolicy
    /// matrix structurally can't express "Admin but not Modifier"; true admin-only features here,
    /// e.g. "Admin: Nasazení relay serveru", already bypass RbacAction the same way) plus a plain
    /// DeviceInfo.Current.Platform runtime check — not #if WINDOWS — same idiom as
    /// Profiles.ActiveProfile/TrustedAdminDevices.
    /// </summary>
    public bool CanConfigureLocalAiTranslation =>
        IsAdmin && Microsoft.Maui.Devices.DeviceInfo.Current.Platform == Microsoft.Maui.Devices.DevicePlatform.WinUI;

    partial void OnIsAdminChanged(bool value) => OnPropertyChanged(nameof(CanConfigureLocalAiTranslation));

    partial void OnLocalAiTranslationStatusTextChanged(string? value) => HasLocalAiTranslationStatus = !string.IsNullOrEmpty(value);

    private void InitializeLocalAiTranslationSection()
    {
        LocalAiBaseUrlText = LocalAiTranslationSettings.GetBaseUrl();
        LocalAiModelNameText = LocalAiTranslationSettings.GetModelName();
    }

    [RelayCommand]
    private void SaveLocalAiTranslationSettings()
    {
        LocalAiTranslationSettings.SetBaseUrl(string.IsNullOrWhiteSpace(LocalAiBaseUrlText) ? LocalAiTranslationSettings.DefaultBaseUrl : LocalAiBaseUrlText.Trim());
        LocalAiTranslationSettings.SetModelName(LocalAiModelNameText.Trim());
        LocalAiTranslationStatusText = "Uloženo.";
    }

    [RelayCommand]
    private async Task TestLocalAiConnectionAsync()
    {
        IsTestingLocalAiConnection = true;
        LocalAiTranslationStatusText = null;
        try
        {
            await _libraryTranslationService.PingAsync();
            LocalAiTranslationStatusText = "Místní AI model je dostupný.";
        }
        catch (Exception ex)
        {
            LocalAiTranslationStatusText = $"Nedostupné: {ex.Message}";
        }
        finally
        {
            IsTestingLocalAiConnection = false;
        }
    }
}
