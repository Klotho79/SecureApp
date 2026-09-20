using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Manual "Add Contact" form (2026-09-20, user's own ask: "možnost přidání kontaktu") — adds a new
/// entry to the shared company phone/extension directory (see <see cref="SharedContact"/>'s own
/// remarks). Published straight to the relay so it's immediately visible to everyone, not just this
/// device.
/// </summary>
public sealed partial class AddContactViewModel : ObservableObject
{
    private readonly ISharedContactService _sharedContactService;

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial string Phone { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial bool CanSave { get; set; }

    /// <summary>Raised once the contact is actually persisted — the Page navigates back on this, not on the command simply completing (an error stays on the form).</summary>
    public event Action? Saved;

    public AddContactViewModel(ISharedContactService sharedContactService)
    {
        _sharedContactService = sharedContactService ?? throw new ArgumentNullException(nameof(sharedContactService));
        DisplayName = string.Empty;
        Phone = string.Empty;
        Note = string.Empty;
        CanSave = true;
    }

    partial void OnErrorMessageChanged(string? value) => HasErrorMessage = !string.IsNullOrEmpty(value);
    partial void OnIsSavingChanged(bool value) => CanSave = !value;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        var name = DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorMessage = "Zadejte jméno.";
            return;
        }

        IsSaving = true;
        try
        {
            var existing = await _sharedContactService.FetchAsync();
            var nextSortOrder = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1;
            var contact = new SharedContact(
                Guid.NewGuid(),
                name,
                string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
                string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
                nextSortOrder,
                DateTimeOffset.UtcNow);

            var ok = await _sharedContactService.PublishAsync(contact);
            if (!ok)
            {
                ErrorMessage = "Kontakt se nepodařilo uložit — zkuste to prosím znovu.";
                return;
            }
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Kontakt se nepodařilo uložit: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}
