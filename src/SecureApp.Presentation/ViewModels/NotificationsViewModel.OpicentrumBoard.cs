using CommunityToolkit.Mvvm.ComponentModel;
using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Gates the "Rozpis pracovišť" nav button on Nástěnka (2026-10-21) — opens the Opicentrum
/// roster/volno board (see <see cref="OpicentrumBoardViewModel"/>/<see cref="Views.OpicentrumBoardPage"/>).
/// Admin+Modifier only, per the user's own explicit scope decision for this board — unlike Stav
/// sálů (visible to everyone, editable by some), this one isn't shown to a plain Viewer at all.
/// </summary>
public sealed partial class NotificationsViewModel
{
    [ObservableProperty]
    public partial bool CanSeeOpicentrumBoard { get; set; }

    private void UpdateOpicentrumBoardVisibility()
    {
        CanSeeOpicentrumBoard = _rolePreviewService.Resolve(_currentUserService.Current.Role) is Role.Admin or Role.Modifier;
    }
}
