using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Live "stav sálu" board on Nástěnka (2026-10-13, user's own ask — see IRoomStatusService's own
/// remarks for the relay-side shape). Shows every <c>Workplace</c> from the shared catalog, the
/// device's own today's sál (from the LOCAL rozpis) sorted first. A plain Viewer may only change the
/// row matching their own today's assignment; Admin/Modifier may change any row — editing stays
/// inline here (user's own explicit call), but CURATING the list of possible status VALUES moved to
/// the Redakce tab (RedakceViewModel) alongside the other consolidated edit/post actions.
/// </summary>
public sealed partial class NotificationsViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<RoomStatusRowItem> RoomStatusRows { get; set; }

    [ObservableProperty]
    public partial bool HasNoRoomStatus { get; set; }

    /// <summary>Converter-free inverse of <see cref="HasNoRoomStatus"/> — same derived-bool pattern as the rest of this codebase's XAML bindings.</summary>
    public bool HasRoomStatus => !HasNoRoomStatus;

    partial void OnHasNoRoomStatusChanged(bool value) => OnPropertyChanged(nameof(HasRoomStatus));

    private void InitRoomStatusState() => RoomStatusRows = [];

    private async Task LoadRoomStatusAsync()
    {
        try
        {
            var canEditAnyRoom = _rolePreviewService.Resolve(_currentUserService.Current.Role) is Role.Admin or Role.Modifier;

            var optionsTask = _roomStatusService.GetStatusOptionsAsync();
            var statusesTask = _roomStatusService.ListAsync();
            var workplacesTask = _workplaceCatalogService.FetchAsync();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var assignmentsTask = _workAssignmentRepository.GetByDateRangeAsync(today, today);
            await Task.WhenAll(optionsTask, statusesTask, workplacesTask, assignmentsTask);

            var options = optionsTask.Result;
            var myWorkplaceId = assignmentsTask.Result.FirstOrDefault(a => a.WorkplaceId is not null)?.WorkplaceId;
            var statusByWorkplace = statusesTask.Result.ToDictionary(s => s.WorkplaceId);

            var rows = workplacesTask.Result
                .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .Select(w =>
                {
                    var isMine = myWorkplaceId == w.Id;
                    statusByWorkplace.TryGetValue(w.Id, out var status);
                    return new RoomStatusRowItem(
                        w.Id, w.Name, isMine, isMine || canEditAnyRoom, options,
                        status?.StatusText, status?.UpdatedByDisplayName, status?.UpdatedAtUtc);
                })
                .OrderByDescending(r => r.IsMine)
                .ThenBy(r => r.WorkplaceName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RoomStatusRows = new ObservableCollection<RoomStatusRowItem>(rows);
            HasNoRoomStatus = RoomStatusRows.Count == 0;
        }
        catch
        {
            // Best-effort board, same tolerance as LoadChatQuickAccessAsync right above — a relay
            // hiccup here shouldn't block the rest of Nástěnka from loading.
            RoomStatusRows = [];
            HasNoRoomStatus = true;
        }
    }

    [RelayCommand]
    private async Task SetRoomStatusAsync(RoomStatusRowItem? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.SelectedStatus)) return;
        await _roomStatusService.SetStatusAsync(row.WorkplaceId, row.WorkplaceName, row.SelectedStatus);
        await LoadRoomStatusAsync();
    }
}

/// <summary>One row of the "Stav sálů" board. <see cref="Options"/> is each row's own copy of the curated status-value list fetched at load time (that list itself is now managed from the Redakce tab, not here).</summary>
public sealed partial class RoomStatusRowItem : ObservableObject
{
    public Guid WorkplaceId { get; }
    public string WorkplaceName { get; }
    public bool IsMine { get; }
    public bool CanEdit { get; }

    /// <summary>Converter-free inverse of <see cref="CanEdit"/> for the read-only row layout.</summary>
    public bool IsReadOnly => !CanEdit;

    public ObservableCollection<string> Options { get; }
    public string? UpdatedInfoText { get; }

    [ObservableProperty]
    public partial string? SelectedStatus { get; set; }

    public bool HasStatus => !string.IsNullOrWhiteSpace(SelectedStatus);

    public RoomStatusRowItem(
        Guid workplaceId, string workplaceName, bool isMine, bool canEdit, IReadOnlyList<string> options,
        string? currentStatus, string? updatedByDisplayName, DateTimeOffset? updatedAtUtc)
    {
        WorkplaceId = workplaceId;
        WorkplaceName = workplaceName;
        IsMine = isMine;
        CanEdit = canEdit;
        Options = new ObservableCollection<string>(options);
        SelectedStatus = currentStatus;
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
