using System.Collections.ObjectModel;
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

    /// <summary>Models found by <see cref="FindLocalAiModelsCommand"/> across Ollama/LM Studio — best-for-translation first.</summary>
    public ObservableCollection<LocalAiModelOption> LocalAiModels { get; } = new();

    [ObservableProperty]
    public partial LocalAiModelOption? SelectedLocalAiModel { get; set; }

    [ObservableProperty]
    public partial bool HasLocalAiModels { get; set; }

    /// <summary>Picking a model fills in AND saves both the server address and the model id — the whole point is that the admin doesn't have to know either (2026-10-09).</summary>
    partial void OnSelectedLocalAiModelChanged(LocalAiModelOption? value)
    {
        if (value is null) return;
        LocalAiBaseUrlText = value.BaseUrl;
        LocalAiModelNameText = value.ModelId;
        LocalAiTranslationSettings.SetBaseUrl(value.BaseUrl);
        LocalAiTranslationSettings.SetModelName(value.ModelId);
        LocalAiTranslationStatusText = $"Uloženo: {value.ModelId} ({value.ServerName}).";
    }

    [RelayCommand]
    private async Task FindLocalAiModelsAsync()
    {
        IsTestingLocalAiConnection = true;
        LocalAiTranslationStatusText = "Hledám stažené modely (Ollama, LM Studio)…";
        try
        {
            var found = await LocalAiModelDiscovery.DiscoverAsync(LocalAiBaseUrlText);
            LocalAiModels.Clear();
            foreach (var model in found) LocalAiModels.Add(model);
            HasLocalAiModels = found.Count > 0;

            if (found.Count == 0)
            {
                LocalAiTranslationStatusText = "Nenašel jsem žádný model. Spusťte Ollamu nebo LM Studio (v LM Studiu: Developer → Start Server) a zkuste to znovu.";
                return;
            }

            var servers = string.Join(", ", found.GroupBy(m => m.ServerName).Select(g => $"{g.Key}: {g.Count()}"));
            // Keep the already-saved choice if it's still there; otherwise only SUGGEST the ⭐ one —
            // selecting it would silently overwrite a working setup the admin may have typed by hand.
            var current = found.FirstOrDefault(m =>
                string.Equals(m.ModelId, LocalAiModelNameText.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(m.BaseUrl, LocalAiBaseUrlText.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            if (current is not null)
            {
                SelectedLocalAiModel = current;
                LocalAiTranslationStatusText = $"Nalezeno {found.Count} modelů ({servers}). Používá se: {current.ModelId}.";
            }
            else
            {
                LocalAiTranslationStatusText = $"Nalezeno {found.Count} modelů ({servers}). Vyberte model ze seznamu — ⭐ je doporučený pro překlad.";
            }
        }
        finally
        {
            IsTestingLocalAiConnection = false;
        }
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
