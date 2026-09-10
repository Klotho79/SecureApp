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
/// or synced.
///
/// The extension directory is grouped by section and collapsed by default (2026-09-10 follow-up —
/// "je to teda pěkně pomalé... zkus to rozdělit ať se toho tolik nenačítá": with ~130 rows always
/// live in one flat CollectionView, opening this page had real, noticeable lag). Same
/// small-ObservableObject-per-group pattern <c>LogbookStatGroupItem</c> already established for the
/// identical "collapsed header, tap to reveal its own rows" shape — only the tapped section's rows
/// actually render. Searching auto-expands every section with a match (otherwise a result would be
/// hidden behind its own collapsed header, defeating the point of searching).
/// </summary>
public sealed partial class ContactsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ContactSectionGroup> PhoneSections { get; set; }

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    public IReadOnlyList<QuickContactEntry> QuickContacts { get; } = ContactDirectoryData.QuickContacts;

    public ContactsViewModel()
    {
        SearchQuery = string.Empty;
        PhoneSections = [];
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    /// <summary>Called from the page's OnAppearing for the same "refresh on every visit" symmetry every other page in this app has — a no-op in practice since the underlying data never changes at runtime, but keeps this page consistent with the rest rather than a special case.</summary>
    [RelayCommand]
    private void Load() => ApplyFilter();

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

/// <summary>One collapsible section of the extension directory — see <see cref="ContactsViewModel"/>'s own remarks on why this exists as a small <c>ObservableObject</c> (mutable per-row <see cref="IsExpanded"/> state needs live notification) rather than a plain record.</summary>
public sealed partial class ContactSectionGroup : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<PhoneDirectoryEntry> Entries { get; }
    public int Count => Entries.Count;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ContactSectionGroup(string name, IReadOnlyList<PhoneDirectoryEntry> entries, bool initiallyExpanded)
    {
        Name = name;
        Entries = entries;
        IsExpanded = initiallyExpanded;
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
