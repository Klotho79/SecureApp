using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Policies;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// "Pozice" sub-tab of <see cref="OpicentrumBoardViewModel"/> — the full weekly roster board
/// (every workplace, every day, including "Nezařazen"), with Admin/Modifier write-back to the
/// real opicentrum.cz schedule. See that type's own remarks for the overall cache/refresh design.
/// </summary>
public sealed partial class OpicentrumBoardViewModel
{
    private static readonly CultureInfo Czech = new("cs-CZ");

    private Dictionary<string, int> _workplaceIdByName = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial bool IsPositionsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsPositionsRefreshing { get; set; }

    /// <summary>Converter-free inverse of <see cref="IsPositionsRefreshing"/>, for the "Obnovit" button's IsEnabled.</summary>
    public bool CanRefreshPositions => !IsPositionsRefreshing;

    partial void OnIsPositionsRefreshingChanged(bool value) => OnPropertyChanged(nameof(CanRefreshPositions));

    [ObservableProperty]
    public partial bool HasPositionsData { get; set; }

    /// <summary>Converter-free inverse of <see cref="HasPositionsData"/>.</summary>
    public bool HasNoPositionsData => !HasPositionsData;

    partial void OnHasPositionsDataChanged(bool value) => OnPropertyChanged(nameof(HasNoPositionsData));

    [ObservableProperty]
    public partial string? PositionsLastUpdatedText { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> PositionsDayHeaders { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<OpicentrumRosterRowItem> PositionsRows { get; set; } = [];

    [ObservableProperty]
    public partial string? PositionsErrorText { get; set; }

    public bool HasPositionsError => !string.IsNullOrEmpty(PositionsErrorText);

    partial void OnPositionsErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasPositionsError));

    /// <summary>Cache-only — never scrapes opicentrum.cz itself. Called from <see cref="LoadAsync"/>.</summary>
    private async Task LoadPositionsAsync()
    {
        IsPositionsLoading = true;
        PositionsErrorText = null;
        try
        {
            ApplyPositionsSnapshot(await _rosterBoardCacheService.GetCachedAsync());
        }
        catch (Exception ex)
        {
            PositionsErrorText = $"Načtení z cache se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsPositionsLoading = false;
        }
    }

    /// <summary>The one user action that triggers a live Opicentrum scrape — see OpicentrumBoardViewModel's own remarks.</summary>
    [RelayCommand]
    private async Task RefreshPositionsAsync()
    {
        IsPositionsRefreshing = true;
        PositionsErrorText = null;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var snapshot = await _opicentrumSyncService.FetchFullRosterBoardAsync(today);
            await _rosterBoardCacheService.PublishAsync(snapshot);
            ApplyPositionsSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            PositionsErrorText = $"Obnovení se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsPositionsRefreshing = false;
        }
    }

    private void ApplyPositionsSnapshot(OpicentrumRosterBoardSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Days.Count == 0)
        {
            HasPositionsData = false;
            PositionsDayHeaders = [];
            PositionsRows = [];
            PositionsLastUpdatedText = null;
            return;
        }

        HasPositionsData = true;
        PositionsLastUpdatedText = FormatRelativeTime(snapshot.UpdatedAtUtc);
        PositionsDayHeaders = new ObservableCollection<string>(snapshot.Days.Select(d => d.Date.ToString("ddd d.M.", Czech)));

        _workplaceIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in snapshot.Days)
            foreach (var wp in day.Workplaces)
                _workplaceIdByName.TryAdd(wp.WorkplaceName, wp.WorkplaceId);

        // Every distinct workplace across the whole week, "Nezařazen" pinned last — a workplace
        // missing on one particular day just gets an empty cell for that day, not a dropped row.
        var allWorkplaceNames = snapshot.Days
            .SelectMany(d => d.Workplaces)
            .Select(w => w.WorkplaceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => string.Equals(n, "Nezařazen", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(n => n, StringComparer.Create(Czech, ignoreCase: true))
            .ToList();

        var rows = allWorkplaceNames.Select(name =>
        {
            var workplaceId = _workplaceIdByName.GetValueOrDefault(name, -1);
            var cells = snapshot.Days.Select(day =>
            {
                var wp = day.Workplaces.FirstOrDefault(w => string.Equals(w.WorkplaceName, name, StringComparison.OrdinalIgnoreCase));
                var people = (wp?.People ?? []).Select(p => new OpicentrumRosterPersonChip(p.PersonId, p.PersonName, workplaceId, day.Date)).ToList();
                return new OpicentrumRosterCellItem(day.Date, people);
            }).ToList();
            return new OpicentrumRosterRowItem(workplaceId, name, cells);
        }).ToList();

        PositionsRows = new ObservableCollection<OpicentrumRosterRowItem>(rows);
    }

    /// <summary>Every other workplace name on the board, for the page's code-behind to build its "move to…" action sheet from.</summary>
    public IReadOnlyList<string> GetTargetWorkplaceNames(int excludeWorkplaceId) =>
        PositionsRows.Where(r => r.WorkplaceId != excludeWorkplaceId && r.WorkplaceId >= 0).Select(r => r.WorkplaceName).ToList();

    private int GetWorkplaceId(string name) => _workplaceIdByName.GetValueOrDefault(name, -1);

    /// <summary>
    /// Called from the page's code-behind once both action-sheet answers (which chip, where to,
    /// move-vs-also-assign) are known. Defense-in-depth role check here even though the whole page
    /// is already Admin/Modifier-gated (see OpicentrumBoardViewModel's own remarks) — matches this
    /// action's real write to the real hospital schedule.
    /// </summary>
    public async Task<bool> MovePersonAsync(OpicentrumRosterPersonChip chip, string targetWorkplaceName, bool keepSourceAssignment)
    {
        if (!RoleAccessPolicy.IsAllowed(_rolePreviewService.Resolve(_currentUserService.Current.Role), RbacAction.EditOpicentrumRoster))
        {
            PositionsErrorText = "K přesunu nemáte oprávnění.";
            return false;
        }

        var targetId = GetWorkplaceId(targetWorkplaceName);
        if (targetId < 0 || chip.WorkplaceId < 0)
        {
            PositionsErrorText = "Neznámé ID pracoviště — zkuste nejprve Obnovit.";
            return false;
        }

        PositionsErrorText = null;
        var result = await _opicentrumSyncService.MoveRosterPersonAsync(chip.Date, chip.PersonId, chip.WorkplaceId, targetId, keepSourceAssignment);
        if (result.Status != OpicentrumMoveStatus.Success)
        {
            PositionsErrorText = result.ErrorMessage ?? "Přesun se nezdařil.";
            return false;
        }

        // Re-fetch + re-publish so the move is reflected immediately on this device AND every
        // other device's relay cache, instead of waiting for the next background cycle.
        await RefreshPositionsAsync();
        return true;
    }

    private static string FormatRelativeTime(DateTimeOffset utc)
    {
        var delta = DateTimeOffset.UtcNow - utc;
        if (delta.TotalMinutes < 1) return "právě teď";
        if (delta.TotalMinutes < 60) return $"před {(int)delta.TotalMinutes} min";
        if (delta.TotalHours < 24) return $"před {(int)delta.TotalHours} h";
        return utc.ToLocalTime().ToString("d. M. HH:mm");
    }
}

/// <summary>One workplace row on the board — <see cref="Cells"/> is in the same day order as <see cref="OpicentrumBoardViewModel.PositionsDayHeaders"/>.</summary>
public sealed record OpicentrumRosterRowItem(int WorkplaceId, string WorkplaceName, IReadOnlyList<OpicentrumRosterCellItem> Cells);

public sealed record OpicentrumRosterCellItem(DateOnly Date, IReadOnlyList<OpicentrumRosterPersonChip> People)
{
    public bool HasPeople => People.Count > 0;
}

/// <summary>One person's chip in one cell — carries everything <see cref="OpicentrumBoardViewModel.MovePersonAsync"/> needs, so the page's code-behind doesn't have to look anything else up.</summary>
public sealed record OpicentrumRosterPersonChip(int PersonId, string PersonName, int WorkplaceId, DateOnly Date);
