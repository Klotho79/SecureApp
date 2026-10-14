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
/// </summary>
public sealed partial class NotificationsViewModel
{
    private static readonly string[] QualifyingKeywords = ["sál", "ostatní", "ODS", "oční"];

    [ObservableProperty]
    public partial bool CanSeeRoomStatus { get; set; }

    private void InitRoomStatusState() => CanSeeRoomStatus = false;

    private async Task LoadRoomStatusAsync()
    {
        try
        {
            if (_rolePreviewService.Resolve(_currentUserService.Current.Role) == Role.Admin)
            {
                CanSeeRoomStatus = true;
                return;
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var assignments = await _workAssignmentRepository.GetByDateRangeAsync(today, today);
            var myWorkplaceName = assignments.FirstOrDefault(a => a.WorkplaceName is not null)?.WorkplaceName;
            CanSeeRoomStatus = myWorkplaceName is not null
                && QualifyingKeywords.Any(keyword => myWorkplaceName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Best-effort, same tolerance as LoadChatQuickAccessAsync right above — a relay/local-DB
            // hiccup here shouldn't block the rest of Nástěnka from loading; just hide the button.
            CanSeeRoomStatus = false;
        }
    }
}
