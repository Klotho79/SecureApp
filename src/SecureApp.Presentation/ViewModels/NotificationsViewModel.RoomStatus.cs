using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Gates the "Status" nav button on Nástěnka (2026-10-14, user's own ask). The full "Stav sálů"
/// board (every workplace, with notes) lives on its own page (<see cref="RoomStatusViewModel"/>/
/// <see cref="Views.RoomStatusPage"/> — opened by tapping the icon, "kompletní obsazení salů se
/// statusy a poznamkami"). Only staff actually tied to an operating-room-type workplace need the
/// button at all, so visibility is driven by today's own LOCAL rozpis entry — auto-synced from
/// Opicentrum (see <see cref="Domain.Interfaces.Services.IOpicentrumSyncService"/>'s own remarks),
/// never admin-assigned: if its <c>WorkplaceName</c> contains one of <see cref="QualifyingKeywords"/>
/// (case-insensitive substring), the button shows. Admin always sees it regardless of their own
/// rozpis, so they can reach the full cross-workplace overview.
///
/// <see cref="MyRoomStatusRows"/> (2026-10-14, user's own correction of an earlier read-only-text
/// attempt: "chci aby na nastence byl status s rolovacim menu" — a status WITH a dropdown menu
/// right on the dashboard, not just text) — one row per qualifying sál with a Picker the user can
/// change directly, no need to open the full board just to update their own status. Empty for
/// Admin by default (no personal sál), who still sees the button to reach the full board.
/// </summary>
public sealed partial class NotificationsViewModel
{
    private static readonly string[] QualifyingKeywords = ["sál", "ostatní", "ODS", "oční"];

    [ObservableProperty]
    public partial bool CanSeeRoomStatus { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<MyRoomStatusItem> MyRoomStatusRows { get; set; }

    private void InitRoomStatusState()
    {
        CanSeeRoomStatus = false;
        MyRoomStatusRows = [];
    }

    private async Task LoadRoomStatusAsync()
    {
        try
        {
            var isAdmin = _rolePreviewService.Resolve(_currentUserService.Current.Role) == Role.Admin;

            var today = DateOnly.FromDateTime(DateTime.Today);
            var assignments = await _workAssignmentRepository.GetByDateRangeAsync(today, today);
            // 2026-10-14: a device's today's rozpis can carry more than one workplace (see
            // RoomStatusViewModel's own remarks) — every qualifying one counts, not just whichever
            // assignment happened to come back first.
            var myQualifyingAssignments = assignments
                .Where(a => a.WorkplaceId is not null && a.WorkplaceName is { } name
                    && QualifyingKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            CanSeeRoomStatus = isAdmin || myQualifyingAssignments.Count > 0;

            if (myQualifyingAssignments.Count == 0)
            {
                MyRoomStatusRows = [];
                return;
            }

            var statusOptions = await _roomStatusService.GetStatusOptionsAsync();
            var statuses = await _roomStatusService.ListAsync();
            var statusByWorkplace = statuses.ToDictionary(s => s.WorkplaceId);
            MyRoomStatusRows = new ObservableCollection<MyRoomStatusItem>(myQualifyingAssignments
                .Select(a =>
                {
                    statusByWorkplace.TryGetValue(a.WorkplaceId!.Value, out var status);
                    return new MyRoomStatusItem(a.WorkplaceId!.Value, a.WorkplaceName!, statusOptions, status?.StatusText);
                }));
        }
        catch
        {
            // Best-effort, same tolerance as LoadChatQuickAccessAsync right above — a relay/local-DB
            // hiccup here shouldn't block the rest of Nástěnka from loading; just hide the button.
            CanSeeRoomStatus = false;
            MyRoomStatusRows = [];
        }
    }

    /// <summary>Saves straight from the dashboard's own Picker — preserves whatever note is already set (NoteText omitted, relay-side COALESCE keeps it), only the status itself changes here.</summary>
    [RelayCommand]
    private async Task SetMyRoomStatusAsync(MyRoomStatusItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.SelectedStatus)) return;
        await _roomStatusService.SetStatusAsync(item.WorkplaceId, item.WorkplaceName, item.SelectedStatus);
    }
}

/// <summary>One of the device's own qualifying sál(y), rendered on Nástěnka with its own status Picker — see this file's own remarks.</summary>
public sealed partial class MyRoomStatusItem : ObservableObject
{
    public Guid WorkplaceId { get; }
    public string WorkplaceName { get; }
    public ObservableCollection<string> StatusOptions { get; }

    [ObservableProperty]
    public partial string? SelectedStatus { get; set; }

    public MyRoomStatusItem(Guid workplaceId, string workplaceName, IReadOnlyList<string> statusOptions, string? currentStatus)
    {
        WorkplaceId = workplaceId;
        WorkplaceName = workplaceName;
        StatusOptions = new ObservableCollection<string>(statusOptions);
        SelectedStatus = currentStatus;
    }
}
