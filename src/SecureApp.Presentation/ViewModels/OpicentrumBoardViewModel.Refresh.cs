using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// The single "Obnovit" button in the title bar next to the Pozice / Volno tabs (2026-11-07, user: "obnovit dej
/// vedle rozpisu a volno, kdy bylo aktualizovano pridej na tlacitko, barva kdy je mozne aktualizovat a znemozni
/// aktualizace prilis casto"). It refreshes whichever tab is open, shows when that board was last updated, is
/// green while a refresh is allowed, and stays disabled for <see cref="MinRefreshInterval"/> after the last update
/// of that board — whoever made it (the relay shares one snapshot, so another device's refresh counts too), because
/// every refresh is a live scrape of opicentrum.cz.
/// </summary>
public sealed partial class OpicentrumBoardViewModel
{
    /// <summary>How soon after the board's last update it may be refreshed again.</summary>
    public static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(5);

    private OpicentrumVolnoSnapshot? _currentVolnoSnapshot;

    private bool IsRefreshing => IsPositionsSubTab ? IsPositionsRefreshing : IsVolnoRefreshing;

    private DateTimeOffset? CurrentUpdatedAt => IsPositionsSubTab ? _currentPositionsSnapshot?.UpdatedAtUtc : _currentVolnoSnapshot?.UpdatedAtUtc;

    /// <summary>True when the open board may be refreshed now: nothing running and the last update is old enough (or there is no data yet).</summary>
    public bool CanRefresh => !IsRefreshing && (CurrentUpdatedAt is not { } updated || DateTimeOffset.UtcNow - updated >= MinRefreshInterval);

    /// <summary>Two lines: the action, then when the board was last updated.</summary>
    public string RefreshButtonText
    {
        get
        {
            if (IsRefreshing) return "Obnovuji…";
            return CurrentUpdatedAt is { } updated ? $"Obnovit\n{FormatRelativeTime(updated)}" : "Obnovit";
        }
    }

    /// <summary>Called by the page's timer (and on every state change) so the age text and the allowed/blocked colour stay current.</summary>
    public void NotifyRefreshState()
    {
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(RefreshButtonText));
    }

    [RelayCommand]
    private Task RefreshCurrentAsync()
    {
        if (!CanRefresh) return Task.CompletedTask; // also guards a stale tap just as the button flips
        return IsPositionsSubTab ? RefreshPositionsAsync() : RefreshVolnoAsync();
    }
}
