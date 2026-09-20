using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Add/edit one day's <see cref="WorkAssignment"/> (2026-09-20, NOTIFICATION_HUB_SPEC.md Phase 5) —
/// opened from <see cref="WorkplaceViewModel"/>'s Dnes card or a Week-strip day tap, always for one
/// specific date (route query attribute, never user-editable here — pick a different day on the
/// calling page to change it). An assignmentId being present means edit (+ Delete), absent means create.
/// </summary>
public sealed partial class AddAssignmentViewModel : ObservableObject, IQueryAttributable
{
    private readonly IWorkAssignmentRepository _assignmentRepository;
    private readonly IWorkplaceCatalogService _workplaceCatalogService;

    private DateOnly _date;
    private Guid? _assignmentId;

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

    public AddAssignmentViewModel(IWorkAssignmentRepository assignmentRepository, IWorkplaceCatalogService workplaceCatalogService)
    {
        _assignmentRepository = assignmentRepository ?? throw new ArgumentNullException(nameof(assignmentRepository));
        _workplaceCatalogService = workplaceCatalogService ?? throw new ArgumentNullException(nameof(workplaceCatalogService));
        TypeOptions = AssignmentTypeCatalog.All.Select(t => new AssignmentTypeOption(t, AssignmentTypeCatalog.Label(t))).ToList();
        SelectedTypeOption = TypeOptions[0];
        DateText = string.Empty;
        WorkplaceName = string.Empty;
        WorkplaceSuggestions = [];
        Note = string.Empty;
        StartTime = new TimeSpan(7, 0, 0);
        EndTime = new TimeSpan(15, 30, 0);
        CanSave = true;
    }

    partial void OnErrorMessageChanged(string? value) => HasErrorMessage = !string.IsNullOrEmpty(value);
    partial void OnIsSavingChanged(bool value) => CanSave = !value;

    [RelayCommand]
    private async Task LoadAsync()
    {
        DateText = FormatDate(_date);

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
        ErrorMessage = null;
        IsSaving = true;
        try
        {
            var workplaceName = string.IsNullOrWhiteSpace(WorkplaceName) ? null : WorkplaceName.Trim();
            var startTime = HasSpecificTime ? TimeOnly.FromTimeSpan(StartTime) : (TimeOnly?)null;
            var endTime = HasSpecificTime ? TimeOnly.FromTimeSpan(EndTime) : (TimeOnly?)null;
            var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();

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
                existing.Update(SelectedTypeOption.Type, startTime, endTime, workplaceId, workplaceName, note);
                await _assignmentRepository.UpdateAsync(existing);
            }
            else
            {
                var assignment = new WorkAssignment(_date, SelectedTypeOption.Type, startTime, endTime, workplaceId, workplaceName, note);
                await _assignmentRepository.AddAsync(assignment);
            }

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
