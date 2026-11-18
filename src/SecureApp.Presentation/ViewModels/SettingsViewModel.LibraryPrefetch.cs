using CommunityToolkit.Mvvm.ComponentModel;
using SecureApp.Presentation.Library;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Systém → Knihovna: "Stáhnout vše dopředu" (2026-11-18, user: soubor se má stáhnout při prvním otevření,
/// stažení všeho předem jen jako volba v Nastavení) — per device, see <see cref="LibraryMirrorSync.PrefetchAll"/>.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial bool IsLibraryPrefetchAll { get; set; }

    private bool _isLoadingLibraryPrefetch;

    private void LoadLibraryPrefetch()
    {
        _isLoadingLibraryPrefetch = true;
        IsLibraryPrefetchAll = LibraryMirrorSync.PrefetchAll;
        _isLoadingLibraryPrefetch = false;
    }

    partial void OnIsLibraryPrefetchAllChanged(bool value)
    {
        if (_isLoadingLibraryPrefetch) return;

        LibraryMirrorSync.PrefetchAll = value;
        // Switched on: start fetching now rather than at the next sweep. Switched off: what is already on the
        // device stays — nothing to do.
        if (value)
            _ = IPlatformApplication.Current?.Services.GetService<LibraryMirrorSync>()?.SyncAsync(TimeSpan.Zero);
    }
}
