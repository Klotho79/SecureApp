using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
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
    /// <summary>
    /// 2026-09-29, user's own correction: a bulk import ("Telefonní seznam ARIM.xlsx", the WhatsApp
    /// group's own membership) was first dropped straight into "Firemní kontakty" tagged via
    /// <see cref="SharedContact.Note"/> — the user explicitly wanted it as its OWN collapsible section
    /// instead ("udelej založku stejou jako je Firemní kontakty ale nazvy ji Soukromé kontakty ARIM").
    /// Reusing <see cref="SharedContact.Note"/> as the split key (rather than a schema/relay change)
    /// costs nothing extra: the 71 rows already carry it from that import.
    /// </summary>
    private const string ArimNoteTag = "ARIM";

    /// <summary>Proper Czech alphabetical order (2026-09-30, user's own ask) — plain OrdinalIgnoreCase (what <see cref="ApplyFilter"/>'s static phone directory already uses) sorts "Ch" after "H", not between "H" and "I" the way Czech readers expect. Requires full ICU globalization (no InvariantGlobalization in the csproj) to actually apply cs-CZ collation rules, not just diacritic-insensitive ordinal.</summary>
    private static readonly StringComparer CzechNameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("cs-CZ"), ignoreCase: true);

    private readonly ISharedContactService _sharedContactService;
    private readonly ICurrentUserService _currentUserService;
    private List<SharedContact> _allArimContacts = [];

    /// <summary>
    /// 2026-10-08, real gap found live: there was no RBAC gate on Contacts at all — even Viewer
    /// could add/delete/reorder. New <see cref="RbacAction.EditContact"/>, same
    /// CanModifyContent-style derived property DocumentBrowserViewModel already established, gates
    /// Add/Delete/Reorder on BOTH "Firemní kontakty" and "Soukromé kontakty ARIM". Deliberately does
    /// NOT gate the self-service profile-vs-ARIM reconciliation in Settings — that's every role's
    /// own identity, a separate code path (see SettingsViewModel's own remarks).
    /// </summary>
    [ObservableProperty]
    public partial bool CanEditContacts { get; set; }

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

    /// <summary>"Soukromé kontakty ARIM" — same shape/behavior (call, delete, PC drag-reorder) as "Firemní kontakty", just its own collapsed-by-default section (2026-09-29: 71 rows is too long to show open by default). Collapsed via <see cref="SharedContactSectionGroup.VisibleEntries"/>, same BindableLayout-doesn't-virtualize reasoning as <see cref="ContactSectionGroup"/>.</summary>
    public SharedContactSectionGroup ArimContactsGroup { get; }

    /// <summary>
    /// 2026-10-08, real crash-looking bug found live: the ARIM card's own <c>IsVisible</c> used to
    /// bind directly to <see cref="SharedContactSectionGroup.HasEntries"/> — fine for "no ARIM data
    /// loaded at all yet", but <see cref="ApplyArimFilter"/> also calls <c>SetEntries</c> with an
    /// EMPTY list whenever a search query matches nobody (e.g. "r" - genuinely no surname in this
    /// list starts with R), which made the SAME property go false and hide the ENTIRE card —
    /// including its own search box — making the search bar itself look like it had "disappeared"
    /// (user's own report: "ono skutecne zmizi to pole pro vyhledavani"). This property tracks
    /// whether ANY ARIM contact was ever loaded from the relay, independent of the current filter, so
    /// the card (and its search box) stays visible through a zero-match search; <see cref="SharedContactSectionGroup.ShowNoResults"/>
    /// is what tells the user the search itself came up empty.
    /// </summary>
    [ObservableProperty]
    public partial bool HasAnyArimContacts { get; set; }

    /// <summary>2026-09-30, user's own ask — searching by name or number over the 71-row ARIM list. Auto-expands the section on a non-empty query (same "search reveals its own results" convention <see cref="ApplyFilter"/>'s static phone directory already follows), but doesn't force it back closed when the query is cleared — the user's own manual toggle wins at that point.</summary>
    [ObservableProperty]
    public partial string ArimSearchQuery { get; set; } = string.Empty;

    public IReadOnlyList<QuickContactEntry> QuickContacts { get; } = ContactDirectoryData.QuickContacts;

    /// <summary>Raised so the Page (which alone can push a MAUI navigation) opens the add-contact form — same MAUI-free-ViewModel split this codebase already established elsewhere.</summary>
    public event Action? RequestAddContact;

    public ContactsViewModel(ISharedContactService sharedContactService, ICurrentUserService currentUserService)
    {
        _sharedContactService = sharedContactService ?? throw new ArgumentNullException(nameof(sharedContactService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        CanEditContacts = true;
        SearchQuery = string.Empty;
        PhoneSections = [];
        CompanyContacts = [];
        ArimContactsGroup = new SharedContactSectionGroup("Soukromé kontakty ARIM", [], initiallyExpanded: false);
        HasNoCompanyContacts = true;
        ApplyFilter();
    }

    // 2026-10-05, user's own report: typing into either search box could make the whole window
    // disappear after a few characters, before ever pressing anything. Root cause: ApplyFilter/
    // ApplyArimFilter ran synchronously on EVERY keystroke, and each one that matched anything
    // auto-expanded the matching section(s) (initiallyExpanded: isSearching) — which, combined with
    // BindableLayout never virtualizing (see ContactSectionGroup's own remarks on the exact same
    // eager-build cost, fixed once already for the collapsed-by-default case but reintroduced here
    // by the search auto-expand), meant fast typing could fire several full native-row rebuilds a
    // second, overlapping each other. Debouncing means the expensive rebuild only happens once
    // typing actually pauses, not once per character.
    private CancellationTokenSource? _searchDebounceCts;
    private CancellationTokenSource? _arimSearchDebounceCts;

    partial void OnSearchQueryChanged(string value) => _ = DebounceAsync(_searchDebounceCts = Renew(_searchDebounceCts), ApplyFilter);
    partial void OnArimSearchQueryChanged(string value) => _ = DebounceAsync(_arimSearchDebounceCts = Renew(_arimSearchDebounceCts), ApplyArimFilter);

    private static CancellationTokenSource Renew(CancellationTokenSource? previous)
    {
        previous?.Cancel();
        return new CancellationTokenSource();
    }

    // 2026-10-05 — 250ms (the original value) made a single keystroke followed by a pause visibly
    // feel broken/unresponsive (the user's own report: "works on 2 letters but not 1" — in reality
    // every keystroke waited the same 250ms, it's just that typing a SECOND character usually takes
    // longer than that, so the delay was already over by the time anyone looked; one character and
    // stopping makes the same delay obvious). 120ms is short enough to feel instant for a single
    // keystroke while still collapsing genuinely fast multi-character typing into one rebuild.
    private const int SearchDebounceMilliseconds = 120;

    private static async Task DebounceAsync(CancellationTokenSource cts, Action action)
    {
        try
        {
            await Task.Delay(SearchDebounceMilliseconds, cts.Token);
            action();
        }
        catch (TaskCanceledException)
        {
            // Superseded by a later keystroke — the newer debounce call is the one that matters.
        }
    }
    partial void OnCompanyContactsErrorMessageChanged(string? value) => HasCompanyContactsError = !string.IsNullOrEmpty(value);

    /// <summary>Called from the page's OnAppearing — refreshes both the static directory filter and (2026-09-20) the shared company contact list.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        CanEditContacts = RoleAccessPolicy.IsAllowed(_currentUserService.Current.Role, RbacAction.EditContact);
        ApplyFilter();
        await LoadCompanyContactsAsync();
    }

    private async Task LoadCompanyContactsAsync()
    {
        IsLoadingCompanyContacts = true;
        CompanyContactsErrorMessage = null;
        try
        {
            var ordered = (await _sharedContactService.FetchAsync()).OrderBy(c => c.SortOrder).ToList();

            CompanyContacts = new ObservableCollection<SharedContactItem>(
                ordered.Where(c => !string.Equals(c.Note, ArimNoteTag, StringComparison.Ordinal))
                    .Select(c => ToItem(c, includeNoteInSubtitle: true)));
            HasNoCompanyContacts = CompanyContacts.Count == 0;

            _allArimContacts = ordered.Where(c => string.Equals(c.Note, ArimNoteTag, StringComparison.Ordinal)).ToList();
            HasAnyArimContacts = _allArimContacts.Count > 0;
            ApplyArimFilter();
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

    /// <summary>Czech-alphabetical + search over <see cref="_allArimContacts"/> (2026-09-30, user's own ask) — re-run on every load AND on every <see cref="ArimSearchQuery"/> change, so a search survives the next background refresh instead of being wiped by it.</summary>
    private void ApplyArimFilter()
    {
        var query = ArimSearchQuery.Trim();
        IEnumerable<SharedContact> source = _allArimContacts;
        if (query.Length > 0)
        {
            // 2026-10-05, user's own ask: narrow by name STARTING WITH what's typed so far (each
            // further letter narrows the same list), not a substring match anywhere in the name —
            // phone numbers still match anywhere, since you don't necessarily dial from the first digit.
            source = source.Where(c =>
                c.DisplayName.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                (c.Phone?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            if (!ArimContactsGroup.IsExpanded) ArimContactsGroup.IsExpanded = true;
        }

        ArimContactsGroup.SetEntries(
            source.OrderBy(c => c.DisplayName, CzechNameComparer)
                // Note IS the section header here ("Soukromé kontakty ARIM") — repeating "ARIM" as
                // every single row's own subtitle too would just be noise.
                .Select(c => ToItem(c, includeNoteInSubtitle: false))
                .ToList());
    }

    [RelayCommand]
    private void AddContact()
    {
        if (!CanEditContacts) return;
        RequestAddContact?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteContactAsync(SharedContactItem? item)
    {
        if (item is null || !CanEditContacts) return;
        try
        {
            var ok = await _sharedContactService.DeleteAsync(item.Id);
            if (!ok)
            {
                CompanyContactsErrorMessage = "Kontakt se nepodařilo smazat — zkuste to prosím znovu.";
                return;
            }
            if (CompanyContacts.Any(c => c.Id == item.Id))
            {
                CompanyContacts = new ObservableCollection<SharedContactItem>(CompanyContacts.Where(c => c.Id != item.Id));
                HasNoCompanyContacts = CompanyContacts.Count == 0;
            }
            else
            {
                // Keep _allArimContacts in sync too — ApplyArimFilter re-derives from it on every
                // search keystroke, and would otherwise resurrect a just-deleted contact.
                _allArimContacts = _allArimContacts.Where(c => c.Id != item.Id).ToList();
                HasAnyArimContacts = _allArimContacts.Count > 0;
                ApplyArimFilter();
            }
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
        if (draggedId == targetId || !CanEditContacts) return;

        // Dragged and target must be in the SAME section — reordering only ever happens within
        // "Firemní kontakty" or within "Soukromé kontakty ARIM", never between them.
        // "Soukromé kontakty ARIM" has no drag handle at all any more (2026-09-30) — it's always
        // Czech-alphabetically sorted now (see ApplyArimFilter), so a manual SortOrder would just get
        // silently overwritten by the very next filter/search re-run. Only Firemní kontakty keeps a
        // meaningful manual order.
        if (Reordered(CompanyContacts, draggedId, targetId) is not { } list) return;
        CompanyContacts = new ObservableCollection<SharedContactItem>(list);
        await PersistReorderAsync(list);
        await LoadCompanyContactsAsync();
    }

    private static List<SharedContactItem>? Reordered(IReadOnlyList<SharedContactItem> source, Guid draggedId, Guid targetId)
    {
        var list = source.ToList();
        var draggedIndex = list.FindIndex(c => c.Id == draggedId);
        var targetIndex = list.FindIndex(c => c.Id == targetId);
        if (draggedIndex < 0 || targetIndex < 0) return null;

        var dragged = list[draggedIndex];
        list.RemoveAt(draggedIndex);
        list.Insert(targetIndex, dragged);
        return list;
    }

    private async Task PersistReorderAsync(List<SharedContactItem> list)
    {
        try
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].SortOrder == i) continue;
                var updated = new SharedContact(list[i].Id, list[i].DisplayName, list[i].Phone, list[i].Note, i, list[i].CreatedAtUtc);
                await _sharedContactService.PublishAsync(updated);
            }
        }
        catch (Exception ex)
        {
            CompanyContactsErrorMessage = $"Pořadí se nepodařilo uložit: {ex.Message}";
        }
    }

    private SharedContactItem ToItem(SharedContact c, bool includeNoteInSubtitle)
    {
        var subtitleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.Phone)) subtitleParts.Add(PhoneNumberFormat.Describe(c.Phone));
        if (includeNoteInSubtitle && !string.IsNullOrWhiteSpace(c.Note)) subtitleParts.Add(c.Note);
        return new SharedContactItem(c.Id, c.DisplayName, c.Phone, c.Note, c.SortOrder, c.CreatedAtUtc, string.Join(" · ", subtitleParts), DeleteContactCommand,
            PhoneNumberFormat.FirstDialable(c.Phone) is not null, CallCommand);
    }

    /// <summary>Opens the phone's dialer pre-filled with the first complete number in <paramref name="raw"/> — never places the call by itself.</summary>
    [RelayCommand]
    private async Task CallAsync(string? raw)
    {
        if (PhoneNumberFormat.FirstDialable(raw) is not { } dial) return;
        try
        {
            await Launcher.Default.OpenAsync(new Uri("tel:" + dial));
        }
        catch (Exception ex)
        {
            CompanyContactsErrorMessage = $"Vytáčení se nepodařilo otevřít: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        var isSearching = query.Length > 0;

        IEnumerable<PhoneDirectoryEntry> source = ContactDirectoryData.PhoneDirectory;
        if (isSearching)
        {
            // 2026-10-05, user's own ask: narrow by name STARTING WITH what's typed so far (each
            // further letter narrows the same list), not a substring match anywhere in the name —
            // number/section still match anywhere, since those aren't typed the same way names are.
            source = source.Where(e =>
                e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
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

/// <summary>One row in "Firemní kontakty" or "Soukromé kontakty ARIM" (2026-09-20) — no chat action anywhere (see class-level remarks on <see cref="ContactsViewModel"/>); <see cref="Subtitle"/> is pre-joined (extension · note) so the DataTemplate needs no visibility triggers per field, this codebase's established "no converters" convention.</summary>
public sealed record SharedContactItem(Guid Id, string DisplayName, string? Phone, string? Note, int SortOrder, DateTimeOffset CreatedAtUtc, string Subtitle, ICommand DeleteCommand, bool CanCall, ICommand CallCommand);

/// <summary>
/// One collapsible section of <see cref="SharedContactItem"/>s — "Soukromé kontakty ARIM" (2026-09-29),
/// same collapsed-by-default/lazy-<see cref="VisibleEntries"/> pattern as <see cref="ContactSectionGroup"/>
/// (see that class's own remarks: BindableLayout does not virtualize, so a still-collapsed section must
/// bind to an EMPTY list, not just be visually hidden, or all ~71 heavy per-row Borders/DragGestureRecognizers
/// get built on page load regardless). Unlike <see cref="ContactSectionGroup"/> this refetches from the
/// relay on every <c>LoadAsync</c>, so <see cref="Entries"/> has a setter (<see cref="SetEntries"/>)
/// instead of being fixed at construction.
/// </summary>
public sealed partial class SharedContactSectionGroup : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<SharedContactItem> Entries { get; private set; }
    public int Count => Entries.Count;
    public bool HasEntries => Entries.Count > 0;
    public IReadOnlyList<SharedContactItem> VisibleEntries => IsExpanded ? Entries : [];

    /// <summary>
    /// 2026-10-08, real user report ("vyhledavani zmizi" - the search seems to disappear): a non-empty
    /// <see cref="ContactsViewModel.ArimSearchQuery"/> that matches nobody (e.g. "r" - genuinely no
    /// surname in this 71-row list starts with R) auto-expands this section (see ApplyArimFilter) into
    /// an empty list with zero visual feedback, unlike the static "Telefonní seznam" card's own
    /// "Nic nenalezeno." label - looked exactly like a crash/disappearance from the outside, even
    /// though nothing was actually broken. Only true once actually expanded, so the normal
    /// collapsed-and-untouched state never shows it.
    /// </summary>
    public bool ShowNoResults => IsExpanded && !HasEntries;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public SharedContactSectionGroup(string name, IReadOnlyList<SharedContactItem> entries, bool initiallyExpanded)
    {
        Name = name;
        Entries = entries;
        IsExpanded = initiallyExpanded;
    }

    public void SetEntries(IReadOnlyList<SharedContactItem> entries)
    {
        Entries = entries;
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(VisibleEntries));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(VisibleEntries));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
