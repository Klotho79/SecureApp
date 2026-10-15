using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Gates the "Status" nav button on Nástěnka (2026-10-14, user's own ask). The full "Stav sálů"
/// board (every workplace, with notes) lives on its own page (<see cref="RoomStatusViewModel"/>/
/// <see cref="Views.RoomStatusPage"/> — opened by tapping the icon, "kompletní obsazení salů se
/// statusy a poznamkami"). Only staff actually tied to an operating-room-type workplace need the
/// button at all, so visibility is driven by today's own LOCAL rozpis entry — auto-synced from
/// Opicentrum (see <see cref="Domain.Interfaces.Services.IOpicentrumSyncService"/>'s own remarks),
/// never admin-assigned: if its <c>WorkplaceName</c> qualifies per <see cref="RoomWorkplaceKeywords"/>
/// (case-insensitive substring), the button shows. Admin always sees it regardless of their own
/// rozpis, so they can reach the full cross-workplace overview.
///
/// <see cref="MyRoomStatusRows"/> (2026-10-14, user's own correction of an earlier read-only-text
/// attempt: "chci aby na nastence byl status s rolovacim menu" — a status WITH a dropdown menu
/// right on the dashboard, not just text) — one row per qualifying sál, no need to open the full
/// board just to update their own status. Empty for Admin by default (no personal sál), who still
/// sees the button to reach the full board.
///
/// BUGFIX (same day, "chybi rolovaci menu"): this used to also require <c>a.WorkplaceId is not
/// null</c>, but Opicentrum-synced assignments NEVER have one (see
/// <see cref="RoomWorkplaceKeywords"/>'s own remarks) — the picker could never appear for anyone.
/// Now matches on <c>WorkplaceName</c> alone and derives the Guid deterministically from it.
///
/// 2026-10-15 design pass ("tohle je strasne osklive", then "zkus lepsi pouzitelnejsi navrh" — see
/// the Design-canvas mockups worked out with the user, matched against the app's own already-chosen
/// visual direction): the Picker dropdown is replaced by <see cref="RoomStatusOptionTile"/> —
/// a colour-coded grid of every option, tapping one saves immediately (no separate confirm step),
/// plus a small badge showing the current status at a glance. Colours come from
/// <see cref="RoomStatusPalette"/> (same Success/Info/Warn/Danger tokens as the rest of the app).
/// </summary>
public sealed partial class NotificationsViewModel
{
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
                .Where(a => a.WorkplaceName is { } name && RoomWorkplaceKeywords.IsQualifying(name))
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
                    var id = RoomWorkplaceKeywords.DeterministicId(a.WorkplaceName!);
                    statusByWorkplace.TryGetValue(id, out var status);
                    return new MyRoomStatusItem(id, a.WorkplaceName!, statusOptions, status?.StatusText, status?.UpdatedByDisplayName, status?.UpdatedAtUtc);
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

    /// <summary>Tapping a tile in the grid saves immediately — preserves whatever note is already set (NoteText omitted, relay-side COALESCE keeps it), only the status itself changes here.</summary>
    [RelayCommand]
    private async Task PickMyRoomStatusAsync(RoomStatusOptionTile? tile)
    {
        if (tile is null) return;
        var room = MyRoomStatusRows.FirstOrDefault(r => r.WorkplaceId == tile.WorkplaceId);
        room?.MarkSelected(tile.Text);
        await _roomStatusService.SetStatusAsync(tile.WorkplaceId, tile.WorkplaceName, tile.Text);
    }
}

/// <summary>One of the device's own qualifying sál(y), rendered on Nástěnka as a badge (current status) plus a grid of <see cref="RoomStatusOptionTile"/> — see this file's own remarks.</summary>
public sealed partial class MyRoomStatusItem : ObservableObject
{
    public Guid WorkplaceId { get; }
    public string WorkplaceName { get; }
    public ObservableCollection<RoomStatusOptionTile> Options { get; }
    public string? UpdatedInfoText { get; }

    [ObservableProperty]
    public partial string? CurrentStatus { get; set; }

    public RoomStatusCategory CurrentCategory => CurrentStatus is { Length: > 0 } s ? RoomStatusPalette.CategoryFor(s) : RoomStatusCategory.Neutral;
    public bool HasCurrentStatus => !string.IsNullOrWhiteSpace(CurrentStatus);

    partial void OnCurrentStatusChanged(string? value)
    {
        OnPropertyChanged(nameof(CurrentCategory));
        OnPropertyChanged(nameof(HasCurrentStatus));
    }

    public MyRoomStatusItem(Guid workplaceId, string workplaceName, IReadOnlyList<string> statusOptions, string? currentStatus, string? updatedByDisplayName, DateTimeOffset? updatedAtUtc)
    {
        WorkplaceId = workplaceId;
        WorkplaceName = workplaceName;
        CurrentStatus = currentStatus;
        UpdatedInfoText = updatedAtUtc is { } utc ? $"{updatedByDisplayName} · {FormatRelative(utc)}" : null;
        Options = new ObservableCollection<RoomStatusOptionTile>(
            statusOptions.Select(text => new RoomStatusOptionTile(workplaceId, workplaceName, text, isSelected: text == currentStatus)));
    }

    /// <summary>Called right after a tile is tapped, before the relay round-trip finishes — same optimistic-then-persist shape <see cref="NotificationsViewModel"/>'s other commands use.</summary>
    public void MarkSelected(string status)
    {
        CurrentStatus = status;
        foreach (var option in Options) option.IsSelected = option.Text == status;
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

/// <summary>One selectable status value in <see cref="MyRoomStatusItem.Options"/>'s grid. <see cref="DisplayCategory"/> is <see cref="RoomStatusCategory.Neutral"/> unless this tile is the selected one — so the color only shows up on whichever tile is active, not on every tile of that meaning.</summary>
public sealed partial class RoomStatusOptionTile : ObservableObject
{
    public Guid WorkplaceId { get; }
    public string WorkplaceName { get; }
    public string Text { get; }
    public RoomStatusCategory Category { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public RoomStatusCategory DisplayCategory => IsSelected ? Category : RoomStatusCategory.Neutral;

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(DisplayCategory));

    public RoomStatusOptionTile(Guid workplaceId, string workplaceName, string text, bool isSelected)
    {
        WorkplaceId = workplaceId;
        WorkplaceName = workplaceName;
        Text = text;
        Category = RoomStatusPalette.CategoryFor(text);
        IsSelected = isSelected;
    }
}
