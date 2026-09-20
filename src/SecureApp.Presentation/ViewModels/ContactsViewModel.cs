using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Contacts;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Telefonní seznam a rychlé kontakty (2026-09-10) — its own standalone tab, the user's own
/// explicit ask, separate from the Logbook: "doplň tel. seznam z logbook ale do samostatného menu
/// tel seznam a rychlé kontakty". That part stays a read-only lookup over
/// <see cref="ContactDirectoryData"/> — no repository, nothing stored/edited/synced.
///
/// "Firemní kontakty" (2026-09-20) is new: an editable, company-wide, relay-synced phone/extension
/// directory via <see cref="ISharedContactService"/> — the user's own correction of an earlier
/// per-device design that linked contacts to chat sessions ("kontakty jsou společné pro všechny...
/// u kontaktů určitě neotevírat chaty, to jsou podnikové kontakty telefonní, klapky"). No chat
/// action anywhere here; see <see cref="SharedContact"/>'s own remarks for the full reasoning.
/// </summary>
public sealed partial class ContactsViewModel : ObservableObject
{
    private readonly ISharedContactService _sharedContactService;

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ContactSectionGroup> PhoneSections { get; set; }

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SharedContactItem> CompanyContacts { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingCompanyContacts { get; set; }

    [ObservableProperty]
    public partial bool HasNoCompanyContacts { get; set; }

    [ObservableProperty]
    public partial string? CompanyContactsErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasCompanyContactsError { get; set; }

    public IReadOnlyList<QuickContactEntry> QuickContacts { get; } = ContactDirectoryData.QuickContacts;

    /// <summary>Raised so the Page (which alone can push a MAUI navigation) opens the add-contact form — same MAUI-free-ViewModel split this codebase already established elsewhere.</summary>
    public event Action? RequestAddContact;

    public ContactsViewModel(ISharedContactService sharedContactService)
    {
        _sharedContactService = sharedContactService ?? throw new ArgumentNullException(nameof(sharedContactService));
        SearchQuery = string.Empty;
        PhoneSections = [];
        CompanyContacts = [];
        HasNoCompanyContacts = true;
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnCompanyContactsErrorMessageChanged(string? value) => HasCompanyContactsError = !string.IsNullOrEmpty(value);

    /// <summary>Called from the page's OnAppearing — refreshes both the static directory filter and (2026-09-20) the shared company contact list.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        ApplyFilter();
        await LoadCompanyContactsAsync();
    }

    private async Task LoadCompanyContactsAsync()
    {
        IsLoadingCompanyContacts = true;
        CompanyContactsErrorMessage = null;
        try
        {
            var contacts = await _sharedContactService.FetchAsync();
            CompanyContacts = new ObservableCollection<SharedContactItem>(
                contacts.OrderBy(c => c.SortOrder).Select(ToItem));
            HasNoCompanyContacts = CompanyContacts.Count == 0;
        }
        catch (Exception ex)
        {
            CompanyContactsErrorMessage = $"Kontakty se nepodařilo načíst: {ex.Message}";
        }
        finally
        {
            IsLoadingCompanyContacts = false;
        }
    }

    [RelayCommand]
    private void AddContact() => RequestAddContact?.Invoke();

    [RelayCommand]
    private async Task DeleteContactAsync(SharedContactItem? item)
    {
        if (item is null) return;
        try
        {
            var ok = await _sharedContactService.DeleteAsync(item.Id);
            if (!ok)
            {
                CompanyContactsErrorMessage = "Kontakt se nepodařilo smazat — zkuste to prosím znovu.";
                return;
            }
            CompanyContacts = new ObservableCollection<SharedContactItem>(CompanyContacts.Where(c => c.Id != item.Id));
            HasNoCompanyContacts = CompanyContacts.Count == 0;
        }
        catch (Exception ex)
        {
            CompanyContactsErrorMessage = $"Kontakt se nepodařilo smazat: {ex.Message}";
        }
    }

    /// <summary>
    /// Desktop drag-and-drop reorder (2026-09-20, user's own ask: "na pc možnost v kontaktech
    /// přetahováním měnit umístění kontaktu") — called from <c>ContactsPage</c>'s code-behind
    /// DragGestureRecognizer/DropGestureRecognizer handlers, which is where MAUI's drag/drop API
    /// itself lives (no plain XAML-bindable command shape for it). The new order is shared for
    /// everyone (republished to the relay), same as any other edit here.
    /// </summary>
    public async Task ReorderAsync(Guid draggedId, Guid targetId)
    {
        if (draggedId == targetId) return;
        var list = CompanyContacts.ToList();
        var draggedIndex = list.FindIndex(c => c.Id == draggedId);
        var targetIndex = list.FindIndex(c => c.Id == targetId);
        if (draggedIndex < 0 || targetIndex < 0) return;

        var dragged = list[draggedIndex];
        list.RemoveAt(draggedIndex);
        list.Insert(targetIndex, dragged);
        CompanyContacts = new ObservableCollection<SharedContactItem>(list);

        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].SortOrder == i) continue;
                var updated = new SharedContact(list[i].Id, list[i].DisplayName, list[i].Phone, list[i].Note, i, list[i].CreatedAtUtc);
                await _sharedContactService.PublishAsync(updated);
            }
            // Re-load so every row's own SortOrder (used for the next reorder's "already in place" check) is current.
            await LoadCompanyContactsAsync();
        }
        catch (Exception ex)
        {
            CompanyContactsErrorMessage = $"Pořadí se nepodařilo uložit: {ex.Message}";
        }
    }

    private SharedContactItem ToItem(SharedContact c)
    {
        var subtitleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.Phone)) subtitleParts.Add($"kl. {c.Phone}");
        if (!string.IsNullOrWhiteSpace(c.Note)) subtitleParts.Add(c.Note);
        return new SharedContactItem(c.Id, c.DisplayName, c.Phone, c.Note, c.SortOrder, c.CreatedAtUtc, string.Join(" · ", subtitleParts), DeleteContactCommand);
    }

    private void ApplyFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        var isSearching = query.Length > 0;

        IEnumerable<PhoneDirectoryEntry> source = ContactDirectoryData.PhoneDirectory;
        if (isSearching)
        {
            source = source.Where(e =>
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Section.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var bySection = source.ToLookup(e => e.Section);
        var groups = ContactDirectoryData.Sections
            .Select(section => new ContactSectionGroup(
                section,
                bySection[section].OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                initiallyExpanded: isSearching))
            .Where(g => g.Entries.Count > 0)
            .ToList();

        PhoneSections = new ObservableCollection<ContactSectionGroup>(groups);
        HasNoResults = PhoneSections.Count == 0;
    }
}

/// <summary>
/// One collapsible section of the extension directory — see <see cref="ContactsViewModel"/>'s own
/// remarks on why this exists as a small <c>ObservableObject</c> (mutable per-row
/// <see cref="IsExpanded"/> state needs live notification) rather than a plain record.
///
/// 2026-09-16 (user: "kontakty se načítají strašně dlouho zjisti proc a oprav to") — the 2026-09-10
/// "collapse by default" fix never actually worked the way it looked like it should: the page binds
/// each section's rows via <c>BindableLayout.ItemsSource</c>, and BindableLayout does NOT virtualize
/// — it eagerly builds a real native view for every bound item the moment the template materializes,
/// regardless of the wrapping layout's own <c>IsVisible</c>. Binding straight to <see cref="Entries"/>
/// meant every one of the ~130 phone-directory rows across EVERY section got built immediately on
/// page load, collapsed or not — the "collapse" only ever hid them after they'd already been built, so
/// it saved nothing. <see cref="VisibleEntries"/> is the actual fix: empty until a section is expanded,
/// so BindableLayout has nothing to build for a still-collapsed section — the expensive work now
/// happens once, on tapping that section open, not upfront for all of them.
/// </summary>
public sealed partial class ContactSectionGroup : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<PhoneDirectoryEntry> Entries { get; }
    public int Count => Entries.Count;

    /// <summary>What <c>ContactsPage.xaml</c>'s BindableLayout actually binds to — see this class's own remarks on why this, and not <see cref="Entries"/> directly, is what makes the collapse cheap.</summary>
    public IReadOnlyList<PhoneDirectoryEntry> VisibleEntries => IsExpanded ? Entries : [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ContactSectionGroup(string name, IReadOnlyList<PhoneDirectoryEntry> entries, bool initiallyExpanded)
    {
        Name = name;
        Entries = entries;
        IsExpanded = initiallyExpanded;
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(VisibleEntries));

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>One row in "Firemní kontakty" (2026-09-20) — no chat action anywhere (see class-level remarks on <see cref="ContactsViewModel"/>); <see cref="Subtitle"/> is pre-joined (extension · note) so the DataTemplate needs no visibility triggers per field, this codebase's established "no converters" convention.</summary>
public sealed record SharedContactItem(Guid Id, string DisplayName, string? Phone, string? Note, int SortOrder, DateTimeOffset CreatedAtUtc, string Subtitle, ICommand DeleteCommand);
