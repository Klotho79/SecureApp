using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Works through one <see cref="LogbookChecklistTemplate"/> — see that entity's own remarks for why
/// which items are ticked is transient UI state here, never persisted: a checklist is meant to be
/// worked through fresh every time (a new anesthesia, a new shift), not accumulate history.
/// </summary>
public sealed partial class LogbookChecklistViewModel : ObservableObject, IQueryAttributable
{
    private readonly ILogbookChecklistRepository _checklistRepository;

    private Guid _checklistId;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<LogbookChecklistRowItem> Items { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    public LogbookChecklistViewModel(ILogbookChecklistRepository checklistRepository)
    {
        _checklistRepository = checklistRepository ?? throw new ArgumentNullException(nameof(checklistRepository));
        Title = "Check-list";
        Items = [];
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("checklistId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _checklistId = id;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            var template = await _checklistRepository.GetByIdAsync(_checklistId);
            if (template is null)
            {
                StatusErrorMessage = "Tento check-list se nepodařilo najít.";
                return;
            }

            Title = template.Name;
            Items = new ObservableCollection<LogbookChecklistRowItem>(template.Items.Select(text => new LogbookChecklistRowItem(text)));
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se načíst check-list: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        foreach (var item in Items)
            item.IsChecked = false;
    }
}

/// <summary>One line of a checklist, with its transient checked state — see the class-level remarks on why this is never persisted.</summary>
public sealed partial class LogbookChecklistRowItem : ObservableObject
{
    public string Text { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    public LogbookChecklistRowItem(string text)
    {
        Text = text;
    }
}
