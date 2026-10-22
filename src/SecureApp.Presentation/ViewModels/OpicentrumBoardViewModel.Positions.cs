using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Policies;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Workplace;

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
    private OpicentrumRosterBoardSnapshot? _currentPositionsSnapshot;

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

    /// <summary>
    /// Local-first (2026-10-21, user's own ask — opening "Pozice" was "neuvěřitelně pomalé" because
    /// it waited on a relay round trip every single time): render whatever this device already has
    /// cached IMMEDIATELY, then reconcile with the relay's own cache in the background — only
    /// re-renders if the relay's copy is actually newer (another device's edit, or the periodic
    /// background publish job — see <c>App.RunBackgroundOpicentrumRosterBoardPublishAsync</c>).
    /// Never scrapes opicentrum.cz itself; that only ever happens from <see cref="RefreshPositionsAsync"/>.
    ///
    /// 2026-10-22 bugfix: the relay reconcile was unconditional on every open, and since the
    /// background publish job ticks every ~15 min, the relay's copy was almost always "newer" by the
    /// time the page was reopened — so the (non-virtualized, BindableLayout-based) board ended up
    /// doing a SECOND full layout pass right after the first one, which is what actually made
    /// "zobrazení" feel even slower than before this round's fix, not the fetch itself. Skip the
    /// relay round trip entirely while the local cache is still fresh.
    /// </summary>
    private async Task LoadPositionsAsync()
    {
        // 2026-10-22, temporary profiling (user's own ask: "proměř na S23+ proč trvá zobrazení
        // rozpisu pracovišť") — every step timed and tagged with AppLog.Metric so the actual
        // breakdown (cache deserialize vs. row-building vs. relay round trip) can be read back from
        // this device's own uploaded metrics.log via GET /admin/applog/{deviceId}?kind=metrics,
        // instead of guessing. Remove once the real bottleneck is identified and fixed.
        var total = Stopwatch.StartNew();
        IsPositionsLoading = true;
        PositionsErrorText = null;
        try
        {
            var loadSw = Stopwatch.StartNew();
            var local = OpicentrumRosterBoardLocalCache.Load();
            AppLog.Metric("opicentrum.positions.cache_load", loadSw.Elapsed.TotalMilliseconds, "ms", ("hit", local is not null));

            if (local is not null)
            {
                var applySw = Stopwatch.StartNew();
                ApplyPositionsSnapshot(local);
                AppLog.Metric("opicentrum.positions.apply", applySw.Elapsed.TotalMilliseconds, "ms",
                    ("source", "local"), ("rows", PositionsRows.Count), ("days", PositionsDayHeaders.Count));
            }

            if (local is not null && DateTimeOffset.UtcNow - local.UpdatedAtUtc < TimeSpan.FromMinutes(5))
            {
                AppLog.Metric("opicentrum.positions.load_total", total.Elapsed.TotalMilliseconds, "ms", ("path", "local_only"));
                return;
            }

            var relaySw = Stopwatch.StartNew();
            var fromRelay = await _rosterBoardCacheService.GetCachedAsync();
            AppLog.Metric("opicentrum.positions.relay_fetch", relaySw.Elapsed.TotalMilliseconds, "ms", ("hit", fromRelay is not null));

            if (fromRelay is not null && (local is null || fromRelay.UpdatedAtUtc > local.UpdatedAtUtc))
            {
                var applySw = Stopwatch.StartNew();
                ApplyPositionsSnapshot(fromRelay);
                AppLog.Metric("opicentrum.positions.apply", applySw.Elapsed.TotalMilliseconds, "ms",
                    ("source", "relay"), ("rows", PositionsRows.Count), ("days", PositionsDayHeaders.Count));
                OpicentrumRosterBoardLocalCache.Save(fromRelay);
            }
            AppLog.Metric("opicentrum.positions.load_total", total.Elapsed.TotalMilliseconds, "ms", ("path", "reconciled"));
        }
        catch (Exception ex)
        {
            // A relay hiccup here isn't worth surfacing if the local cache already rendered something.
            if (_currentPositionsSnapshot is null)
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
            OpicentrumRosterBoardLocalCache.Save(snapshot);
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
        _currentPositionsSnapshot = snapshot;
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

        // Work with the already-loaded snapshot, patched locally (2026-10-21, user's own ask: "u
        // uprav ale pracuj s ulozenou aktualizovanou verzi") — a second full live re-scrape here,
        // just to refresh the UI after a write whose own success is already known, was the real
        // reason the board felt "neuveritelne pomale". The actual write to opicentrum.cz above is
        // still live and unavoidable; everything after it is now local-only.
        if (_currentPositionsSnapshot is { } current)
        {
            var patched = PatchMove(current, chip, targetId, targetWorkplaceName, keepSourceAssignment);
            ApplyPositionsSnapshot(patched);
            OpicentrumRosterBoardLocalCache.Save(patched);
            try { await _rosterBoardCacheService.PublishAsync(patched); }
            catch { /* best-effort — the real write already succeeded; other devices catch up via the background job */ }
        }
        return true;
    }

    /// <summary>Applies one move/also-assign to an in-memory snapshot without touching the network — see <see cref="MovePersonAsync"/>'s own remarks.</summary>
    private static OpicentrumRosterBoardSnapshot PatchMove(OpicentrumRosterBoardSnapshot snapshot, OpicentrumRosterPersonChip chip, int targetWorkplaceId, string targetWorkplaceName, bool keepSourceAssignment)
    {
        var days = snapshot.Days.Select(day =>
        {
            if (day.Date != chip.Date) return day;

            var workplaces = day.Workplaces.ToList();

            if (!keepSourceAssignment)
            {
                var sourceIndex = workplaces.FindIndex(w => w.WorkplaceId == chip.WorkplaceId);
                if (sourceIndex >= 0)
                {
                    var source = workplaces[sourceIndex];
                    workplaces[sourceIndex] = source with { People = source.People.Where(p => p.PersonId != chip.PersonId).ToList() };
                }
            }

            var targetIndex = workplaces.FindIndex(w => w.WorkplaceId == targetWorkplaceId);
            if (targetIndex >= 0)
            {
                var target = workplaces[targetIndex];
                if (!target.People.Any(p => p.PersonId == chip.PersonId))
                    workplaces[targetIndex] = target with { People = [..target.People, new OpicentrumRosterPerson(chip.PersonId, chip.PersonName)] };
            }
            else
            {
                workplaces.Add(new OpicentrumRosterWorkplace(targetWorkplaceId, targetWorkplaceName, [new OpicentrumRosterPerson(chip.PersonId, chip.PersonName)]));
            }

            return day with { Workplaces = workplaces };
        }).ToList();

        return snapshot with { Days = days, UpdatedAtUtc = DateTimeOffset.UtcNow };
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
