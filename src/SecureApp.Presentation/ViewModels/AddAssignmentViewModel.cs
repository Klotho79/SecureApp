using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Notifications;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Add/edit one day's <see cref="WorkAssignment"/> (2026-09-20, NOTIFICATION_HUB_SPEC.md Phase 5) —
/// opened from <see cref="WorkplaceViewModel"/>'s Dnes card or a Week-strip day tap, always for one
/// specific date (route query attribute, never user-editable here — pick a different day on the
/// calling page to change it). An assignmentId being present means edit (+ Delete), absent means create.
///
/// <see cref="CanEditAssignment"/> (2026-09-21, RbacAction.EditWorkAssignment, the user's own rule:
/// "viewer nemuze menit pracovni zarazeni, muze psat poznamku") gates Type/Workplace/time/Delete —
/// Viewer sees them read-only but may still always edit <see cref="Note"/> and save that change alone.
/// Creating a brand-new assignment from an empty day inherently means setting its Type, so a Viewer
/// can't do that at all (see <see cref="CanSave"/>'s own remarks).
/// </summary>
public sealed partial class AddAssignmentViewModel : ObservableObject, IQueryAttributable
{
    private readonly IWorkAssignmentRepository _assignmentRepository;
    private readonly IWorkplaceCatalogService _workplaceCatalogService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOpicentrumSyncService _opicentrumSyncService;
    private readonly INotificationRepository _notificationRepository;

    private DateOnly _date;
    private Guid? _assignmentId;
    private WorkAssignment? _existing;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("date", out var dateValue) && DateOnly.TryParseExact(dateValue?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            _date = date;

        if (query.TryGetValue("assignmentId", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id))
            _assignmentId = id;
        else
            _assignmentId = null;
    }

    [ObservableProperty]
    public partial string DateText { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<AssignmentTypeOption> TypeOptions { get; set; }

    [ObservableProperty]
    public partial AssignmentTypeOption SelectedTypeOption { get; set; }

    [ObservableProperty]
    public partial bool HasSpecificTime { get; set; }

    [ObservableProperty]
    public partial TimeSpan StartTime { get; set; }

    [ObservableProperty]
    public partial TimeSpan EndTime { get; set; }

    [ObservableProperty]
    public partial string WorkplaceName { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> WorkplaceSuggestions { get; set; }

    [ObservableProperty]
    public partial bool HasWorkplaceSuggestions { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial bool IsExistingAssignment { get; set; }

    /// <summary>Gates Type/Workplace/time/Delete — see this class's own remarks. Note is never gated by this.</summary>
    [ObservableProperty]
    public partial bool CanEditAssignment { get; set; }

    /// <summary>Mirrors <see cref="CanEditAssignment"/> so XAML never needs an inverse-boolean converter (this codebase's established convention — see <c>NotificationsViewModel.HasNoX</c>'s own remarks).</summary>
    public bool IsReadOnlyForViewer => !CanEditAssignment;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool CanSave { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasErrorMessage { get; set; }

    /// <summary>Raised once the assignment is actually saved/deleted — the Page navigates back on this, not on the command simply completing (an error stays on the form).</summary>
    public event Action? Saved;

    public AddAssignmentViewModel(
        IWorkAssignmentRepository assignmentRepository,
        IWorkplaceCatalogService workplaceCatalogService,
        ICurrentUserService currentUserService,
        IOpicentrumSyncService opicentrumSyncService,
        INotificationRepository notificationRepository)
    {
        _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
        _workplaceCatalogService = workplaceCatalogService ?? throw new ArgumentNullException(nameof(workplaceCatalogService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _opicentrumSyncService = opicentrumSyncService ?? throw new ArgumentNullException(nameof(opicentrumSyncService));
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        TypeOptions = AssignmentTypeCatalog.All.Select(t => new AssignmentTypeOption(t, AssignmentTypeCatalog.Label(t))).ToList();
        SelectedTypeOption = TypeOptions[0];
        DateText = string.Empty;
        WorkplaceName = string.Empty;
        WorkplaceSuggestions = [];
        Note = string.Empty;
        StartTime = new TimeSpan(7, 0, 0);
        EndTime = new TimeSpan(15, 30, 0);
        CanEditAssignment = true;
        CanSave = true;
    }

    partial void OnErrorMessageChanged(string? value) => HasErrorMessage = !string.IsNullOrEmpty(value);
    partial void OnIsSavingChanged(bool value) => RecomputeCanSave();
    partial void OnCanEditAssignmentChanged(bool value)
    {
        RecomputeCanSave();
        OnPropertyChanged(nameof(IsReadOnlyForViewer));
    }
    partial void OnIsExistingAssignmentChanged(bool value) => RecomputeCanSave();

    /// <summary>
    /// A Viewer (see <see cref="CanEditAssignment"/>) may save only when editing an EXISTING
    /// assignment — that path only ever touches <see cref="Note"/> (see <see cref="SaveAsync"/>).
    /// Creating a brand-new one from an empty day inherently means picking its Type, which a Viewer
    /// isn't allowed to do at all, so there's nothing for them to save there.
    /// </summary>
    private void RecomputeCanSave() => CanSave = !IsSaving && (CanEditAssignment || IsExistingAssignment);

    [RelayCommand]
    private async Task LoadAsync()
    {
        DateText = FormatDate(_date);
        await _currentUserService.InitializeAsync();
        CanEditAssignment = RoleAccessPolicy.IsAllowed(_currentUserService.Current.Role, RbacAction.EditWorkAssignment);

        try
        {
            var catalog = await _workplaceCatalogService.FetchAsync();
            WorkplaceSuggestions = new ObservableCollection<string>(catalog.Select(w => w.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            HasWorkplaceSuggestions = WorkplaceSuggestions.Count > 0;
        }
        catch
        {
            // Best-effort — an empty suggestion list just means free typing, never blocks the form.
        }

        if (_assignmentId is not { } id)
            return;

        var existing = await _assignmentRepository.GetByIdAsync(id);
        if (existing is null)
            return;

        _existing = existing;
        IsExistingAssignment = true;
        SelectedTypeOption = TypeOptions.First(o => o.Type == existing.Type);
        HasSpecificTime = existing.StartTime is not null && existing.EndTime is not null;
        if (existing.StartTime is { } start) StartTime = start.ToTimeSpan();
        if (existing.EndTime is { } end) EndTime = end.ToTimeSpan();
        WorkplaceName = existing.WorkplaceName ?? string.Empty;
        Note = existing.Note ?? string.Empty;
    }

    [RelayCommand]
    private void PickSuggestion(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            WorkplaceName = name;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!CanSave) return; // belt-and-braces — IsEnabled already keeps this uncallable, see RecomputeCanSave
        ErrorMessage = null;
        IsSaving = true;
        try
        {
            var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();

            // Viewer (see CanEditAssignment): note-only edit of an EXISTING assignment — every other
            // field is left exactly as it already was, never taken from this form's own bound values
            // (which a Viewer could still technically have stale/mismatched, since those inputs are
            // merely IsEnabled=false, not unbound).
            if (!CanEditAssignment)
            {
                if (_existing is null) return; // shouldn't happen — CanSave is false for this case too
                _existing.Update(_existing.Type, _existing.StartTime, _existing.EndTime, _existing.WorkplaceId, _existing.WorkplaceName, note, _existing.OnCallWorkplaceName);
                await _assignmentRepository.UpdateAsync(_existing);
                _ = PushNoteInBackgroundAsync(_date, note);
                Saved?.Invoke();
                return;
            }

            var workplaceName = string.IsNullOrWhiteSpace(WorkplaceName) ? null : WorkplaceName.Trim();
            var startTime = HasSpecificTime ? TimeOnly.FromTimeSpan(StartTime) : (TimeOnly?)null;
            var endTime = HasSpecificTime ? TimeOnly.FromTimeSpan(EndTime) : (TimeOnly?)null;

            Guid? workplaceId = null;
            if (workplaceName is not null)
                workplaceId = await EnsureWorkplaceCatalogEntryAsync(workplaceName);

            if (_assignmentId is { } id)
            {
                var existing = await _assignmentRepository.GetByIdAsync(id);
                if (existing is null)
                {
                    ErrorMessage = "Směna už neexistuje — mohla být mezitím smazána.";
                    return;
                }
                // OnCallWorkplaceName is preserved as-is, not editable from this form (2026-09-21,
                // scope decision — it's Opicentrum-sync-only for now, see WorkAssignment's own
                // remarks); an Admin/Modifier edit here must not silently wipe a synced duty overlay.
                existing.Update(SelectedTypeOption.Type, startTime, endTime, workplaceId, workplaceName, note, existing.OnCallWorkplaceName);
                await _assignmentRepository.UpdateAsync(existing);
            }
            else
            {
                var assignment = new WorkAssignment(_date, SelectedTypeOption.Type, startTime, endTime, workplaceId, workplaceName, note);
                await _assignmentRepository.AddAsync(assignment);
            }

            _ = PushNoteInBackgroundAsync(_date, note);
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nepodařilo se uložit: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanEditAssignment) return; // belt-and-braces — the button is IsVisible/IsEnabled-gated too
        if (_assignmentId is not { } id) return;
        IsSaving = true;
        try
        {
            await _assignmentRepository.DeleteAsync(id);
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nepodařilo se smazat: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Fire-and-forget after a local save (2026-09-22, user's own ask: "chci aby se poznamka propsala
    /// do webu") — pushes <paramref name="note"/> to the real Opicentrum "Požadavky" form (a genuine
    /// write to the user's actual hospital scheduling system, see <c>OpicentrumSyncService.PushNoteAsync</c>'s
    /// own remarks for how it avoids disturbing anything else on that day's row). Doesn't block the
    /// local Save/navigate-back — this is a network round-trip to a third-party site, same "never let
    /// Opicentrum reachability gate the local UI" principle <c>WorkplaceViewModel</c>'s own sync
    /// already established. Silent on success (matches every other Opicentrum status surfaced only via
    /// Notification, not a toast); a real failure or a day with no editable web row publishes a
    /// Notification so the user finds out even after already navigating away — NotConfigured (no
    /// Opicentrum credentials set up at all) stays silent, same as the read-sync's own
    /// DescribeSyncResult, since that's an expected "not set up" state, not a failure.
    /// </summary>
    private async Task PushNoteInBackgroundAsync(DateOnly date, string? note)
    {
        try
        {
            var result = await _opicentrumSyncService.PushNoteAsync(date, note);
            if (result.Status is OpicentrumNotePushStatus.Success or OpicentrumNotePushStatus.NotConfigured)
                return;

            var reason = result.Status == OpicentrumNotePushStatus.DayNotEditable
                ? result.ErrorMessage ?? "Tento den nemá na webu editovatelné políčko poznámky."
                : $"Nepodařilo se zapsat poznámku na web: {result.ErrorMessage}";
            await NotificationPublisher.PublishSystemWarningAsync(_notificationRepository, "Poznámka se nepropsala do Opicentra", $"{date:d. M. yyyy}: {reason}");
        }
        catch
        {
            // Best-effort — a failed push must never surface as a crash; the Notification path above
            // already covers the "user should know" case for every result PushNoteAsync itself returns.
        }
    }

    /// <summary>Best-effort: a new free-typed workplace name is published to the shared catalog so it's suggested next time, for everyone — never blocks saving the assignment itself if the relay is unreachable.</summary>
    private async Task<Guid?> EnsureWorkplaceCatalogEntryAsync(string workplaceName)
    {
        try
        {
            var catalog = await _workplaceCatalogService.FetchAsync();
            var existing = catalog.FirstOrDefault(w => string.Equals(w.Name, workplaceName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return existing.Id;

            var created = new Domain.ValueObjects.Workplace(Guid.NewGuid(), workplaceName, null, DateTimeOffset.UtcNow);
            var ok = await _workplaceCatalogService.PublishAsync(created);
            return ok ? created.Id : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatDate(DateOnly date)
    {
        string[] dayNames = ["neděle", "pondělí", "úterý", "středa", "čtvrtek", "pátek", "sobota"];
        string[] monthNames = ["ledna", "února", "března", "dubna", "května", "června", "července", "srpna", "září", "října", "listopadu", "prosince"];
        return $"{dayNames[(int)date.DayOfWeek]} {date.Day}. {monthNames[date.Month - 1]} {date.Year}";
    }
}

public sealed record AssignmentTypeOption(AssignmentType Type, string Label);
