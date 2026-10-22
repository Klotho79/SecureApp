using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// "Volno" sub-tab of <see cref="OpicentrumBoardViewModel"/> — every staff member's leave for the
/// current month, read-only this round. Writing another person's entry needs spravavolna.php's
/// real edit mechanism reverse-engineered first (unlike pracoviste.php's drag-and-drop, which was
/// captured live 2026-10-21 — see <c>OpicentrumSyncService.MoveRosterPersonAsync</c>'s own remarks
/// for how); until then this is a genuine, useful overview on its own, not a stub.
/// </summary>
public sealed partial class OpicentrumBoardViewModel
{
    [ObservableProperty]
    public partial bool IsVolnoLoading { get; set; }

    [ObservableProperty]
    public partial bool IsVolnoRefreshing { get; set; }

    /// <summary>Converter-free inverse of <see cref="IsVolnoRefreshing"/>, for the "Obnovit" button's IsEnabled.</summary>
    public bool CanRefreshVolno => !IsVolnoRefreshing;

    partial void OnIsVolnoRefreshingChanged(bool value) => OnPropertyChanged(nameof(CanRefreshVolno));

    [ObservableProperty]
    public partial bool HasVolnoData { get; set; }

    /// <summary>Converter-free inverse of <see cref="HasVolnoData"/>.</summary>
    public bool HasNoVolnoData => !HasVolnoData;

    partial void OnHasVolnoDataChanged(bool value) => OnPropertyChanged(nameof(HasNoVolnoData));

    [ObservableProperty]
    public partial string? VolnoLastUpdatedText { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> VolnoDayHeaders { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<OpicentrumVolnoRowItem> VolnoRows { get; set; } = [];

    [ObservableProperty]
    public partial string? VolnoErrorText { get; set; }

    public bool HasVolnoError => !string.IsNullOrEmpty(VolnoErrorText);

    partial void OnVolnoErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasVolnoError));

    /// <summary>
    /// Local-first, same reasoning as <c>OpicentrumBoardViewModel.Positions.cs</c>'s
    /// <c>LoadPositionsAsync</c> — including its 2026-10-22 fixes: skip the relay reconcile (and the
    /// full board re-render it causes) while the local cache is still fresh, and don't let
    /// <see cref="IsVolnoLoading"/> stay true for the reconcile's own round trip once local data is
    /// already on screen (confirmed live on the user's S23+: that round trip alone took 5+ seconds).
    /// </summary>
    private async Task LoadVolnoAsync()
    {
        IsVolnoLoading = VolnoRows.Count == 0;
        VolnoErrorText = null;
        try
        {
            var local = OpicentrumVolnoBoardLocalCache.Load();
            if (local is not null)
            {
                ApplyVolnoSnapshot(local);
                IsVolnoLoading = false;
            }

            if (local is not null && DateTimeOffset.UtcNow - local.UpdatedAtUtc < TimeSpan.FromMinutes(10))
                return;

            var fromRelay = await _volnoBoardCacheService.GetCachedAsync();
            if (fromRelay is not null && (local is null || fromRelay.UpdatedAtUtc > local.UpdatedAtUtc))
            {
                ApplyVolnoSnapshot(fromRelay);
                OpicentrumVolnoBoardLocalCache.Save(fromRelay);
            }
        }
        catch (Exception ex)
        {
            if (VolnoRows.Count == 0)
                VolnoErrorText = $"Načtení z cache se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsVolnoLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshVolnoAsync()
    {
        IsVolnoRefreshing = true;
        VolnoErrorText = null;
        try
        {
            var month = DateOnly.FromDateTime(DateTime.Today);
            var snapshot = await _opicentrumSyncService.FetchVolnoForMonthAsync(month);
            await _volnoBoardCacheService.PublishAsync(snapshot);
            OpicentrumVolnoBoardLocalCache.Save(snapshot);
            ApplyVolnoSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            VolnoErrorText = $"Obnovení se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsVolnoRefreshing = false;
        }
    }

    private void ApplyVolnoSnapshot(OpicentrumVolnoSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.People.Count == 0)
        {
            _volnoFillVersion++;
            HasVolnoData = false;
            VolnoDayHeaders = [];
            VolnoRows = [];
            VolnoLastUpdatedText = null;
            return;
        }

        HasVolnoData = true;
        VolnoLastUpdatedText = FormatRelativeTime(snapshot.UpdatedAtUtc);

        var daysInMonth = DateTime.DaysInMonth(snapshot.Year, snapshot.Month);
        var days = Enumerable.Range(1, daysInMonth).Select(d => new DateOnly(snapshot.Year, snapshot.Month, d)).ToList();
        VolnoDayHeaders = new ObservableCollection<string>(days.Select(d => d.ToString("d.", Czech)));

        var rows = snapshot.People
            .OrderBy(p => OpicentrumParsing.SurnameSortKey(p.PersonName), StringComparer.Create(Czech, ignoreCase: true))
            .Select(p =>
            {
                var entryByDate = p.Entries.ToDictionary(e => e.Date);
                var cells = days.Select(d => entryByDate.TryGetValue(d, out var e) ? e.RawCode : string.Empty).ToList();
                return new OpicentrumVolnoRowItem(p.PersonId, p.PersonName, cells);
            })
            .ToList();

        // Same incremental fill as ApplyPositionsSnapshot (empty board only). The first batch covers
        // a full screen (~25 rows) so nothing visibly pops in; the rest lands below the fold.
        var version = ++_volnoFillVersion;
        VolnoRows = VolnoRows.Count == 0
            ? IncrementalFill.Start(rows, firstBatch: 28, nextBatch: 10, delayMs: 120, isCurrent: () => version == _volnoFillVersion)
            : new ObservableCollection<OpicentrumVolnoRowItem>(rows);
    }

    private int _volnoFillVersion;
}

/// <summary>One person's row — <see cref="DayCodes"/> is in the same day order as <see cref="OpicentrumBoardViewModel.VolnoDayHeaders"/>, empty string for a day with nothing recorded.</summary>
public sealed record OpicentrumVolnoRowItem(int PersonId, string PersonName, IReadOnlyList<string> DayCodes);
