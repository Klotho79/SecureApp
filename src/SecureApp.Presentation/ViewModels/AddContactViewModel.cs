using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Repositories;
using Contact = SecureApp.Domain.Entities.Contact;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Manual "Add Contact" form (2026-09-20, user's own ask: "možnost přidání kontaktu") — for someone
/// NOT on SecureApp at all (a plain phone/email entry, see <see cref="Contact"/>'s own remarks on the
/// linked-vs-unlinked distinction). Paired peers/group members never need this form; they appear in
/// "Moje kontakty" automatically (see <c>ContactsViewModel.SyncFromChatsAsync</c>).
/// </summary>
public sealed partial class AddContactViewModel : ObservableObject
{
    private readonly IContactRepository _contactRepository;

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial string Phone { get; set; }

    [ObservableProperty]
    public partial string Email { get; set; }

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

    public AddContactViewModel(IContactRepository contactRepository)
    {
        _contactRepository = contactRepository ?? throw new ArgumentNullException(nameof(contactRepository));
        DisplayName = string.Empty;
        Phone = string.Empty;
        Email = string.Empty;
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
            var existing = await _contactRepository.GetAllOrderedAsync();
            var nextSortOrder = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1;
            var contact = new Contact(
                name,
                nextSortOrder,
                phone: string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
                email: string.IsNullOrWhiteSpace(Email) ? null : Email.Trim(),
                note: string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());
            await _contactRepository.AddAsync(contact);
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
