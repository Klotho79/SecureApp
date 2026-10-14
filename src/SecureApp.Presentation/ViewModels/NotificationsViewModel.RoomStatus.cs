using CommunityToolkit.Mvvm.ComponentModel;
using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Gates the "Status" nav button on Nástěnka (2026-10-14, user's own ask). The full "Stav sálů"
/// board itself lives on its own page now (<see cref="RoomStatusViewModel"/>/
/// <see cref="Views.RoomStatusPage"/> — it had grown into "hodně řádků" to render inline on the
/// dashboard). Only staff actually tied to an operating-room-type workplace need the button at all,
/// so visibility is driven by today's own LOCAL rozpis entry — auto-synced from Opicentrum (see
/// <see cref="Domain.Interfaces.Services.IOpicentrumSyncService"/>'s own remarks), never admin-
/// assigned: if its <c>WorkplaceName</c> contains one of <see cref="QualifyingKeywords"/>
/// (case-insensitive substring), the button shows. Admin always sees it regardless of their own
/// rozpis, so they can reach the full cross-workplace overview ("admin bude mít možnost vidět
/// kompletní obsazení pracovišť").
///
/// <see cref="CurrentStatusSummaryText"/> (2026-10-14, same-day follow-up: "vedle ikony napis
/// aktualni status") shows the CURRENT status of the device's own qualifying sál(y) right next to
/// the button, so a glance at Nástěnka already answers "jak to u mě jde" without an extra tap into
/// the full board. Empty whenever there's nothing of the user's own to show (no qualifying
/// assignment today, or nobody's set a status for it yet) — including for Admin, who has no
/// personal sál by default.
/// </summary>
public sealed partial class NotificationsViewModel
{
    private static readonly string[] QualifyingKeywords = ["sál", "ostatní", "ODS", "oční"];

    [ObservableProperty]
    public partial bool CanSeeRoomStatus { get; set; }

    [ObservableProperty]
    public partial string? CurrentStatusSummaryText { get; set; }

    public bool HasCurrentStatusSummary => !string.IsNullOrEmpty(CurrentStatusSummaryText);

    partial void OnCurrentStatusSummaryTextChanged(string? value) => OnPropertyChanged(nameof(HasCurrentStatusSummary));

    private void InitRoomStatusState()
    {
        CanSeeRoomStatus = false;
        CurrentStatusSummaryText = null;
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
                CurrentStatusSummaryText = null;
                return;
            }

            var statuses = await _roomStatusService.ListAsync();
            var statusByWorkplace = statuses.ToDictionary(s => s.WorkplaceId);
            var parts = myQualifyingAssignments
                .Select(a => statusByWorkplace.TryGetValue(a.WorkplaceId!.Value, out var status) ? $"{a.WorkplaceName}: {status.StatusText}" : null)
                .Where(part => part is not null)
                .ToList();
            CurrentStatusSummaryText = parts.Count == 0 ? null : string.Join(" · ", parts);
        }
        catch
        {
            // Best-effort, same tolerance as LoadChatQuickAccessAsync right above — a relay/local-DB
            // hiccup here shouldn't block the rest of Nástěnka from loading; just hide the button.
            CanSeeRoomStatus = false;
            CurrentStatusSummaryText = null;
        }
    }
}
