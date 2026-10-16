using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Live "Stav sálů" board (2026-10-13, user's own ask; moved to its own page 2026-10-14 — it had
/// grown into "hodně řádků" for the Nástěnka dashboard, so it now lives behind a dedicated "Status"
/// nav button there instead of rendering inline; see NotificationsViewModel.RoomStatus.cs's own
/// remarks for that button's visibility gate). Shows today's complete sál/ostatní/ODS/oční roster
/// pulled live from Opicentrum's pracoviste.php (2026-10-14, bugfix — see
/// <see cref="IOpicentrumSyncService.FetchWorkplaceNamesForDateAsync"/>'s own remarks: the
/// admin-curated shared Workplace catalog this used to read from was sparse/manually-added, not the
/// "kompletní rozvržení" the user actually asked for), the device's own today's sál(y) sorted first.
///
/// 2026-10-14, same-day follow-up: "je mozne ze jeden uzivatel je urceny na vice pracovist (op.
/// salu)" — a device's today's rozpis can carry MORE THAN ONE workplace assignment (e.g. covering two
/// sály in one day), so "my own sál" is now the full SET of today's assigned workplace ids, not just
/// the first one found. A plain Viewer may only change a row in that set; Admin/Modifier may change
/// any row — editing stays inline here, but CURATING the list of possible status/note VALUES is
/// managed from the Redakce tab (RedakceViewModel) alongside the other consolidated edit/post actions.
/// </summary>
public sealed partial class RoomStatusViewModel : ObservableObject
{
    private readonly IRoomStatusService _roomStatusService;
    private readonly IOpicentrumSyncService _opicentrumSyncService;
    private readonly IWorkAssignmentRepository _workAssignmentRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly RolePreviewService _rolePreviewService;

    [ObservableProperty]
    public partial ObservableCollection<RoomStatusRowItem> RoomStatusRows { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasNoRoomStatus { get; set; }

    /// <summary>Converter-free inverse of <see cref="HasNoRoomStatus"/> — same derived-bool pattern as the rest of this codebase's XAML bindings.</summary>
    public bool HasRoomStatus => !HasNoRoomStatus;

    partial void OnHasNoRoomStatusChanged(bool value) => OnPropertyChanged(nameof(HasRoomStatus));

    public RoomStatusViewModel(
        IRoomStatusService roomStatusService,
        IOpicentrumSyncService opicentrumSyncService,
        IWorkAssignmentRepository workAssignmentRepository,
        ICurrentUserService currentUserService,
        RolePreviewService rolePreviewService)
    {
        _roomStatusService = roomStatusService ?? throw new ArgumentNullException(nameof(roomStatusService));
        _opicentrumSyncService = opicentrumSyncService ?? throw new ArgumentNullException(nameof(opicentrumSyncService));
        _workAssignmentRepository = workAssignmentRepository ?? throw new ArgumentNullException(nameof(workAssignmentRepository));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _rolePreviewService = rolePreviewService ?? throw new ArgumentNullException(nameof(rolePreviewService));
        RoomStatusRows = [];
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _currentUserService.InitializeAsync();
            var canEditAnyRoom = _rolePreviewService.Resolve(_currentUserService.Current.Role) is Role.Admin or Role.Modifier;

            var statusOptionsTask = _roomStatusService.GetStatusOptionsAsync();
            var noteOptionsTask = _roomStatusService.GetNoteOptionsAsync();
            var statusesTask = _roomStatusService.ListAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var assignmentsTask = _workAssignmentRepository.GetByDateRangeAsync(today, today);
            await Task.WhenAll(statusOptionsTask, noteOptionsTask, statusesTask, assignmentsTask);

            var statusOptions = statusOptionsTask.Result;
            var noteOptions = noteOptionsTask.Result;
            // A device's today's rozpis can carry more than one qualifying workplace (see this
            // type's own remarks above) — every one of them counts as "my sál", not just the first
            // found. Opicentrum-synced WorkplaceName is never tied to a catalog Id (see
            // RoomWorkplaceKeywords's own remarks) — the Guid is derived from the name itself.
            var myWorkplaceIds = assignmentsTask.Result
                .Where(a => a.WorkplaceName is { } name && RoomWorkplaceKeywords.IsQualifying(name))
                .Select(a => RoomWorkplaceKeywords.DeterministicId(a.WorkplaceName!))
                .ToHashSet();
            var statusByWorkplace = statusesTask.Result.ToDictionary(s => s.WorkplaceId);

            IReadOnlyList<string> todaysWorkplaceNames;
            try
            {
                // 2026-10-16, bugfix: FetchWorkplaceNamesForDateAsync returns EVERY pracoviste entry
                // for the day (ambulances, ARIMs, everything), not just sály — the board must filter
                // it the same way the fallback path below already does.
                todaysWorkplaceNames = (await _opicentrumSyncService.FetchWorkplaceNamesForDateAsync(today))
                    .Where(RoomWorkplaceKeywords.IsQualifying)
                    .ToList();
            }
            catch
            {
                // Opicentrum unreachable right now (seen live: "Software caused connection abort") —
                // fall back to whatever this device's own already-synced local rozpis knows about
                // its own qualifying room(s), rather than an empty board.
                todaysWorkplaceNames = assignmentsTask.Result
                    .Where(a => a.WorkplaceName is { } name && RoomWorkplaceKeywords.IsQualifying(name))
                    .Select(a => a.WorkplaceName!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var rows = todaysWorkplaceNames
                .Select(name =>
                {
                    var id = RoomWorkplaceKeywords.DeterministicId(name);
                    var isMine = myWorkplaceIds.Contains(id);
                    statusByWorkplace.TryGetValue(id, out var status);
                    return new RoomStatusRowItem(
                        id, name, isMine, isMine || canEditAnyRoom, statusOptions, noteOptions,
                        status?.StatusText, status?.NoteText, status?.UpdatedByDisplayName, status?.UpdatedAtUtc);
                })
                .OrderByDescending(r => r.IsMine)
                .ThenBy(r => r.WorkplaceName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RoomStatusRows = new ObservableCollection<RoomStatusRowItem>(rows);
            HasNoRoomStatus = RoomStatusRows.Count == 0;
        }
        catch
        {
            // Best-effort board — a relay hiccup here shouldn't leave the page stuck loading.
            RoomStatusRows = [];
            HasNoRoomStatus = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SetRoomStatusAsync(RoomStatusRowItem? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.SelectedStatus)) return;
        await _roomStatusService.SetStatusAsync(row.WorkplaceId, row.WorkplaceName, row.SelectedStatus, row.SelectedNote);
        await LoadAsync();
    }
}

/// <summary>One row of the "Stav sálů" board. <see cref="StatusOptions"/>/<see cref="NoteOptions"/> are each row's own copy of the curated value lists fetched at load time (those lists themselves are managed from the Redakce tab, not here).</summary>
public sealed partial class RoomStatusRowItem : ObservableObject
{
    public Guid WorkplaceId { get; }
    public string WorkplaceName { get; }
    public bool IsMine { get; }
    public bool CanEdit { get; }

    /// <summary>Converter-free inverse of <see cref="CanEdit"/> for the read-only row layout.</summary>
    public bool IsReadOnly => !CanEdit;

    public ObservableCollection<string> StatusOptions { get; }

    /// <summary>2026-10-14, user's own ask: "poslední uzavření sálu, poslední výkon, sál pokračuje do služby atd" — a second, independent curated choice on the same row.</summary>
    public ObservableCollection<string> NoteOptions { get; }

    public string? UpdatedInfoText { get; }

    [ObservableProperty]
    public partial string? SelectedStatus { get; set; }

    [ObservableProperty]
    public partial string? SelectedNote { get; set; }

    public bool HasStatus => !string.IsNullOrWhiteSpace(SelectedStatus);
    public bool HasNote => !string.IsNullOrWhiteSpace(SelectedNote);

    public RoomStatusRowItem(
        Guid workplaceId, string workplaceName, bool isMine, bool canEdit,
        IReadOnlyList<string> statusOptions, IReadOnlyList<string> noteOptions,
        string? currentStatus, string? currentNote, string? updatedByDisplayName, DateTimeOffset? updatedAtUtc)
    {
        WorkplaceId = workplaceId;
        WorkplaceName = workplaceName;
        IsMine = isMine;
        CanEdit = canEdit;
        StatusOptions = new ObservableCollection<string>(statusOptions);
        NoteOptions = new ObservableCollection<string>(noteOptions);
        SelectedStatus = currentStatus;
        SelectedNote = currentNote;
        UpdatedInfoText = updatedAtUtc is { } utc ? $"{updatedByDisplayName}, {FormatRelative(utc)}" : null;
    }

    private static string FormatRelative(DateTimeOffset utc)
    {
        var delta = DateTimeOffset.UtcNow - utc;
        if (delta.TotalMinutes < 1) return "právě teď";
        if (delta.TotalMinutes < 60) return $"před {(int)delta.TotalMinutes} min";
        if (delta.TotalHours < 24) return $"před {(int)delta.TotalHours} h";
        return utc.ToLocalTime().ToString("d. M. HH:mm");
    }
}
