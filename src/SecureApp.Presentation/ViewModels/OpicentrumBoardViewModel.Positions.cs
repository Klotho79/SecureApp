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

    partial void OnIsPositionsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefreshPositions));
        OnPropertyChanged(nameof(ShowNoPositionsData));
    }

    partial void OnIsPositionsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowNoPositionsData));

    [ObservableProperty]
    public partial bool HasPositionsData { get; set; }

    /// <summary>The "no data" hint — only once nothing is being loaded either (paging to a week this device has nothing for would otherwise flash it).</summary>
    public bool ShowNoPositionsData => !HasPositionsData && !IsPositionsLoading && !IsPositionsRefreshing;

    partial void OnHasPositionsDataChanged(bool value) => OnPropertyChanged(nameof(ShowNoPositionsData));

    // The week shown (a Monday). The relay keeps ONE shared roster snapshot — the week the board opens on
    // (WeekMath.DefaultViewWeek: from Saturday the next one); any other week the user pages to is kept on
    // this device only (OpicentrumRosterBoardLocalCache) and scraped from Opicentrum on demand.
    private DateOnly _positionsWeek = WeekMath.DefaultViewWeek(DateOnly.FromDateTime(DateTime.Today));
    private int _positionsLoadVersion;
    private int _positionsFetches;

    /// <summary>The week's date range, shown between the ‹ › buttons — the date alone (2026-10-25, user: "neni nutne vypisovat pristi tyden, staci datum").</summary>
    [ObservableProperty]
    public partial string PositionsWeekLabel { get; set; } = string.Empty;

    private static bool IsDefaultWeek(DateOnly week) => week == WeekMath.DefaultViewWeek(DateOnly.FromDateTime(DateTime.Today));

    private void UpdatePositionsWeekLabel() =>
        PositionsWeekLabel = WeekMath.FormatWeekLabel(_positionsWeek, _positionsWeek.AddDays(6));

    [RelayCommand]
    private Task PreviousPositionsWeekAsync() => ShowPositionsWeekAsync(_positionsWeek.AddDays(-7));

    [RelayCommand]
    private Task NextPositionsWeekAsync() => ShowPositionsWeekAsync(_positionsWeek.AddDays(7));

    private Task ShowPositionsWeekAsync(DateOnly week)
    {
        _positionsWeek = week;
        UpdatePositionsWeekLabel();
        return LoadPositionsAsync();
    }

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
    /// 2026-10-22 bugfix #1: the relay reconcile was unconditional on every open, and since the
    /// background publish job ticks every ~15 min, the relay's copy was almost always "newer" by the
    /// time the page was reopened — so the (non-virtualized, BindableLayout-based) board ended up
    /// doing a SECOND full layout pass right after the first one, which is what actually made
    /// "zobrazení" feel even slower than before this round's fix, not the fetch itself. Skip the
    /// relay round trip entirely while the local cache is still fresh.
    ///
    /// 2026-10-22 bugfix #2, found via real on-device profiling (metrics.log from the user's own
    /// S23+): when the cache WAS stale, the stale-reconcile's own <c>relay_fetch</c> round trip took
    /// 5.2s on that device/network — and <see cref="IsPositionsLoading"/> stayed true for the whole
    /// wait, so the busy indicator sat on top of the already-rendered board (bound to
    /// <see cref="HasPositionsData"/>, not this flag) for 5+ seconds even though the data itself was
    /// already on screen. Flip it false right after the local snapshot renders, not after the
    /// reconcile — the reconcile keeps running, silently, exactly as "zobraz uložené A pak kontroluj
    /// zda nejsou změny" asked for.
    ///
    /// 2026-10-24: the board pages between weeks, so this loads <see cref="_positionsWeek"/>. The default
    /// week reconciles with the relay's shared snapshot exactly as before (and scrapes only when neither
    /// this device nor the relay has it); any other week lives on this device only — see
    /// <see cref="_positionsWeek"/>. A load superseded by a newer one (another ‹ ›) drops out at its next await.
    /// </summary>
    private async Task LoadPositionsAsync()
    {
        // 2026-10-22, temporary profiling (user's own ask: "proměř na S23+ proč trvá zobrazení
        // rozpisu pracovišť") — every step timed and tagged with AppLog.Metric so the actual
        // breakdown (cache deserialize vs. row-building vs. relay round trip) can be read back from
        // this device's own uploaded metrics.log via GET /admin/applog/{deviceId}?kind=metrics,
        // instead of guessing. Remove once the real bottleneck is identified and fixed.
        var total = Stopwatch.StartNew();
        var version = ++_positionsLoadVersion;
        var week = _positionsWeek;
        UpdatePositionsWeekLabel();
        IsPositionsLoading = PositionsRows.Count == 0;
        PositionsErrorText = null;
        try
        {
            var loadSw = Stopwatch.StartNew();
            var local = OpicentrumRosterBoardLocalCache.Load(week);
            AppLog.Metric("opicentrum.positions.cache_load", loadSw.Elapsed.TotalMilliseconds, "ms", ("hit", local is not null));

            if (local is not null)
            {
                var applySw = Stopwatch.StartNew();
                ApplyPositionsSnapshot(local);
                AppLog.Metric("opicentrum.positions.apply", applySw.Elapsed.TotalMilliseconds, "ms",
                    ("source", "local"), ("rows", PositionsRows.Count), ("days", PositionsDayHeaders.Count));
                IsPositionsLoading = false; // data is on screen now — the reconcile below must not re-block the UI
            }
            else if (_currentPositionsSnapshot is not null)
            {
                ApplyPositionsSnapshot(null); // paged to a week this device has nothing for: the previous week's board must not stay under the new label
                IsPositionsLoading = true;
            }

            if (!IsDefaultWeek(week))
            {
                // An upcoming week can still change, a past one cannot — so only the former is re-scraped, and only once its copy is old.
                var refreshDue = local is null || (week >= WeekMath.StartOfWeek(DateOnly.FromDateTime(DateTime.Today)) && DateTimeOffset.UtcNow - local.UpdatedAtUtc > OtherWeekRefreshAfter);
                if (refreshDue) await ScrapePositionsWeekAsync(week);
                AppLog.Metric("opicentrum.positions.load_total", total.Elapsed.TotalMilliseconds, "ms", ("path", refreshDue ? "other_week_scraped" : "other_week_local"));
                return;
            }

            // 2026-10-24: also skip while the local copy is confirmed current — the relay pushes every
            // change (see OpicentrumBoardSync), so there is nothing to ask it.
            if (local is not null && (DateTimeOffset.UtcNow - local.UpdatedAtUtc < TimeSpan.FromMinutes(5) || OpicentrumBoardSync.IsCurrent(OpicentrumBoardSync.Roster)))
            {
                AppLog.Metric("opicentrum.positions.load_total", total.Elapsed.TotalMilliseconds, "ms", ("path", "local_only"));
                return;
            }

            var relaySw = Stopwatch.StartNew();
            var epoch = OpicentrumBoardSync.CurrentEpoch;
            // Task.Run: keep every synchronous slice of the fetch (vault, DB, JSON) off the UI thread.
            var fromRelay = await Task.Run(() => _rosterBoardCacheService.GetCachedAsync());
            if (version != _positionsLoadVersion) return;
            AppLog.Metric("opicentrum.positions.relay_fetch", relaySw.Elapsed.TotalMilliseconds, "ms", ("hit", fromRelay is not null));
            // The relay's single snapshot may still be the previous week's (the morning the default week flips, before any device refreshed it).
            if (fromRelay is not null && fromRelay.WeekOf != week) fromRelay = null;
            if (fromRelay is not null) OpicentrumBoardSync.MarkVerified(OpicentrumBoardSync.Roster, epoch);

            if (fromRelay is null && local is null)
            {
                await ScrapePositionsWeekAsync(week); // nobody has this week yet — fetch and share it
                AppLog.Metric("opicentrum.positions.load_total", total.Elapsed.TotalMilliseconds, "ms", ("path", "scraped"));
                return;
            }

            if (fromRelay is not null && (local is null || fromRelay.UpdatedAtUtc > local.UpdatedAtUtc))
            {
                if (local is not null && SameContent(local, fromRelay))
                {
                    // 2026-10-23: the relay's copy is "newer" almost every time only because the periodic
                    // background publish re-stamps it — the roster itself is unchanged. Rebuilding all rows
                    // for that froze the UI 0.15–0.8 s (apply.assign source=relay in the device metrics),
                    // right after the board had finished appearing. Just move the timestamp.
                    _currentPositionsSnapshot = fromRelay;
                    PositionsLastUpdatedText = FormatRelativeTime(fromRelay.UpdatedAtUtc);
                    AppLog.Event("opicentrum.positions.apply-skipped", ("reason", "same-content"));
                }
                else
                {
                    var applySw = Stopwatch.StartNew();
                    ApplyPositionsSnapshot(fromRelay);
                    AppLog.Metric("opicentrum.positions.apply", applySw.Elapsed.TotalMilliseconds, "ms",
                        ("source", "relay"), ("rows", PositionsRows.Count), ("days", PositionsDayHeaders.Count));
                }
                OpicentrumRosterBoardLocalCache.Save(fromRelay);
            }
            AppLog.Metric("opicentrum.positions.load_total", total.Elapsed.TotalMilliseconds, "ms", ("path", "reconciled"));
        }
        catch (Exception ex)
        {
            // A relay hiccup here isn't worth surfacing if the local cache already rendered something.
            AppLog.Error(nameof(OpicentrumBoardViewModel), $"loading the roster board for {week:yyyy-MM-dd} failed", ex);
            if (version == _positionsLoadVersion && _currentPositionsSnapshot is null)
                PositionsErrorText = $"Načtení se nezdařilo: {ex.Message}";
        }
        finally
        {
            if (version == _positionsLoadVersion) IsPositionsLoading = false; // a superseded load leaves the flag to the newer one
        }
    }

    private static readonly TimeSpan OtherWeekRefreshAfter = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Fetches <paramref name="week"/> live from Opicentrum, keeps it locally and shows it if the board is still
    /// on that week; the default week is then also published for the other devices (best-effort — the fetch itself
    /// already succeeded, so a relay hiccup must not turn it into an error).
    /// </summary>
    private async Task ScrapePositionsWeekAsync(DateOnly week)
    {
        // No credentials: Nastavení → Opicentrum opens (once per page visit for this automatic path — see OpicentrumCredentialsRequest).
        if (!await OpicentrumCredentialsRequest.EnsureAsync(_opicentrumSyncService, _credentialsPrompted))
        {
            _credentialsPrompted = true;
            PositionsErrorText = OpicentrumCredentialsRequest.MissingText;
            return;
        }

        _positionsFetches++;
        IsPositionsRefreshing = true;
        try
        {
            // Task.Run: the fetch is a login, a page and regex parsing — all its continuations would otherwise run on the UI thread.
            var snapshot = await Task.Run(() => _opicentrumSyncService.FetchFullRosterBoardAsync(week));
            OpicentrumRosterBoardLocalCache.Save(snapshot);
            if (week == _positionsWeek) ApplyPositionsSnapshot(snapshot);

            if (IsDefaultWeek(week))
            {
                try { await _rosterBoardCacheService.PublishAsync(snapshot); }
                catch (Exception ex) { AppLog.Error(nameof(OpicentrumBoardViewModel), "publishing the roster board to the relay failed", ex); }
            }
        }
        finally
        {
            if (--_positionsFetches == 0) IsPositionsRefreshing = false;
        }
    }

    /// <summary>Called on the UI thread by the page when the relay pushed a newer roster (already saved to the local cache): redraw only if it is the week on screen and something actually changed.</summary>
    public void ApplyPushedPositions(OpicentrumRosterBoardSnapshot pushed)
    {
        if (IsAccessDenied || pushed.WeekOf != _positionsWeek) return;
        if (_currentPositionsSnapshot is { } current && SameContent(current, pushed))
        {
            _currentPositionsSnapshot = pushed;
            PositionsLastUpdatedText = FormatRelativeTime(pushed.UpdatedAtUtc);
            return;
        }
        ApplyPositionsSnapshot(pushed);
    }

    /// <summary>The one user action that triggers a live Opicentrum scrape — see OpicentrumBoardViewModel's own remarks.</summary>
    [RelayCommand]
    private async Task RefreshPositionsAsync()
    {
        PositionsErrorText = null;
        _credentialsPrompted = false; // an explicit "Obnovit" always opens the credentials again
        try
        {
            await ScrapePositionsWeekAsync(_positionsWeek);
        }
        catch (Exception ex)
        {
            PositionsErrorText = $"Obnovení se nezdařilo: {ex.Message}";
        }
    }

    private void ApplyPositionsSnapshot(OpicentrumRosterBoardSnapshot? snapshot)
    {
        _currentPositionsSnapshot = snapshot;
        if (snapshot is null || snapshot.Days.Count == 0)
        {
            _positionsAllRows = [];
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

        // Every distinct workplace across the whole week in Opicentrum's own order, "Nezařazen"
        // pinned last — a workplace missing on one particular day just gets an empty cell for that
        // day, not a dropped row. Each day lists its workplaces in page order (a subsequence of the
        // page's row order), so a name first seen on a later day is slotted in right after the one
        // that precedes it on that day. OrderBy is stable, so the pin keeps the rest in that order.
        var orderedNames = new List<string>();
        foreach (var day in snapshot.Days)
        {
            var previous = -1;
            foreach (var wp in day.Workplaces)
            {
                var index = orderedNames.FindIndex(n => string.Equals(n, wp.WorkplaceName, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    index = previous + 1;
                    orderedNames.Insert(index, wp.WorkplaceName);
                }
                previous = index;
            }
        }
        var allWorkplaceNames = orderedNames
            .OrderBy(n => string.Equals(n, "Nezařazen", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToList();

        var buildSw = Stopwatch.StartNew();
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
        AppLog.Metric("opicentrum.positions.apply.build", buildSw.Elapsed.TotalMilliseconds, "ms", ("rows", rows.Count));

        // All rows at once (2026-10-24): the board is drawn on a canvas now (PositionsBoardView), so there is no
        // per-row view creation left to spread over time — the old incremental fill existed only for that
        // (all 29 rows of BindableLayout views at once froze the UI thread ~0.7 s on the S23+).
        var assignSw = Stopwatch.StartNew();
        _positionsAllRows = rows;
        PositionsRows = new ObservableCollection<OpicentrumRosterRowItem>(rows);
        AppLog.Metric("opicentrum.positions.apply.assign", assignSw.Elapsed.TotalMilliseconds, "ms", ("shown", PositionsRows.Count));
    }

    /// <summary>True when two snapshots show exactly the same board (same days, workplaces and people in the same order) — everything except <see cref="OpicentrumRosterBoardSnapshot.UpdatedAtUtc"/>.</summary>
    private static bool SameContent(OpicentrumRosterBoardSnapshot a, OpicentrumRosterBoardSnapshot b)
    {
        if (a.WeekOf != b.WeekOf || a.Days.Count != b.Days.Count) return false;
        for (var d = 0; d < a.Days.Count; d++)
        {
            var da = a.Days[d];
            var db = b.Days[d];
            if (da.Date != db.Date || da.Workplaces.Count != db.Workplaces.Count) return false;
            for (var w = 0; w < da.Workplaces.Count; w++)
            {
                var wa = da.Workplaces[w];
                var wb = db.Workplaces[w];
                if (wa.WorkplaceId != wb.WorkplaceId || wa.WorkplaceName != wb.WorkplaceName || wa.People.Count != wb.People.Count) return false;
                for (var p = 0; p < wa.People.Count; p++)
                    if (wa.People[p] != wb.People[p]) return false; // record equality: id + name
            }
        }
        return true;
    }

    private IReadOnlyList<OpicentrumRosterRowItem> _positionsAllRows = [];

    /// <summary>Every other workplace name on the board (including rows still being filled in), for the page's code-behind to build its "move to…" action sheet from.</summary>
    public IReadOnlyList<string> GetTargetWorkplaceNames(int excludeWorkplaceId) =>
        _positionsAllRows.Where(r => r.WorkplaceId != excludeWorkplaceId && r.WorkplaceId >= 0).Select(r => r.WorkplaceName).ToList();

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
        // (the user may have paged to another week while the write ran — then this snapshot isn't the chip's week)
        if (_currentPositionsSnapshot is { } current && current.WeekOf == WeekMath.StartOfWeek(chip.Date))
        {
            var patched = PatchMove(current, chip, targetId, targetWorkplaceName, keepSourceAssignment);
            ApplyPositionsSnapshot(patched);
            OpicentrumRosterBoardLocalCache.Save(patched);
            // The relay keeps one snapshot (the default week) — another week's edit stays on this device.
            if (IsDefaultWeek(patched.WeekOf))
            {
                try { await _rosterBoardCacheService.PublishAsync(patched); }
                catch { /* best-effort — the real write already succeeded; other devices catch up via the background job */ }
            }
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
