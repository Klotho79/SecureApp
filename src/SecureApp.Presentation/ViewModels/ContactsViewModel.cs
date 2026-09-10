using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Presentation.Contacts;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Telefonní seznam a rychlé kontakty (2026-09-10) — its own standalone tab, the user's own
/// explicit ask, separate from the Logbook: "doplň tel. seznam z logbook ale do samostatného menu
/// tel seznam a rychlé kontakty". Purely a read-only lookup over <see cref="ContactDirectoryData"/>
/// — no repository, no DI dependency beyond that static data, since nothing here is stored, edited,
/// or synced. <see cref="SearchQuery"/> filters the extension directory only; the "Rychlé kontakty"
/// list (who to call for a given situation) is short enough to show in full, unfiltered, always.
/// </summary>
public sealed partial class ContactsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<PhoneDirectoryEntry> PhoneDirectory { get; set; }

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    public IReadOnlyList<QuickContactEntry> QuickContacts { get; } = ContactDirectoryData.QuickContacts;

    public ContactsViewModel()
    {
        SearchQuery = string.Empty;
        PhoneDirectory = [];
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    /// <summary>Called from the page's OnAppearing for the same "refresh on every visit" symmetry every other page in this app has — a no-op in practice since the underlying data never changes at runtime, but keeps this page consistent with the rest rather than a special case.</summary>
    [RelayCommand]
    private void Load() => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchQuery?.Trim();
        var source = ContactDirectoryData.PhoneDirectory.AsEnumerable();
        if (!string.IsNullOrEmpty(query))
        {
            source = source.Where(e =>
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Section.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var sectionOrder = ContactDirectoryData.Sections.ToList();
        var results = source
            .OrderBy(e => sectionOrder.IndexOf(e.Section))
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PhoneDirectory = new ObservableCollection<PhoneDirectoryEntry>(results);
        HasNoResults = PhoneDirectory.Count == 0;
    }
}
