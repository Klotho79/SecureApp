using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Contacts;
using Contact = SecureApp.Domain.Entities.Contact;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Telefonní seznam a rychlé kontakty (2026-09-10) — its own standalone tab, the user's own
/// explicit ask, separate from the Logbook: "doplň tel. seznam z logbook ale do samostatného menu
/// tel seznam a rychlé kontakty". That part stays a read-only lookup over
/// <see cref="ContactDirectoryData"/> — no repository, nothing stored/edited/synced.
///
/// "Moje kontakty" (2026-09-20, user's own ask) is new and different: an editable, user-orderable
/// personal contact list, backed by <see cref="IContactRepository"/>. Auto-synced on every load from
/// this device's own paired peers (<see cref="IChatSessionRepository"/>/<see cref="IGroupMemberRepository"/>)
/// so every 1:1/group chat the user actually participates in shows up here with no manual step — see
/// <see cref="SyncFromChatsAsync"/> — alongside contacts added by hand (phone/email only, no chat
/// action, for someone not on SecureApp at all).
/// </summary>
public sealed partial class ContactsViewModel : ObservableObject
{
    private readonly IContactRepository _contactRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IGroupChatRepository _groupChatRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IMessagingService _messagingService;

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ContactSectionGroup> PhoneSections { get; set; }

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<PersonalContactItem> MyContacts { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMyContacts { get; set; }

    [ObservableProperty]
    public partial bool HasNoMyContacts { get; set; }

    [ObservableProperty]
    public partial string? MyContactsErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasMyContactsError { get; set; }

    public IReadOnlyList<QuickContactEntry> QuickContacts { get; } = ContactDirectoryData.QuickContacts;

    /// <summary>Raised so the Page (which alone can push a MAUI navigation/modal) opens the add-contact form or a chat route — same MAUI-free-ViewModel split this codebase already established elsewhere.</summary>
    public event Action<string>? RequestNavigate;
    public event Action? RequestAddContact;

    public ContactsViewModel(
        IContactRepository contactRepository,
        IChatSessionRepository chatSessionRepository,
        IGroupChatRepository groupChatRepository,
        IGroupMemberRepository groupMemberRepository,
        IMessagingService messagingService)
    {
        _contactRepository = contactRepository ?? throw new ArgumentNullException(nameof(contactRepository));
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _groupChatRepository = groupChatRepository ?? throw new ArgumentNullException(nameof(groupChatRepository));
        _groupMemberRepository = groupMemberRepository ?? throw new ArgumentNullException(nameof(groupMemberRepository));
        _messagingService = messagingService ?? throw new ArgumentNullException(nameof(messagingService));

        SearchQuery = string.Empty;
        PhoneSections = [];
        MyContacts = [];
        HasNoMyContacts = true;
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnMyContactsErrorMessageChanged(string? value) => HasMyContactsError = !string.IsNullOrEmpty(value);

    /// <summary>Called from the page's OnAppearing — refreshes both the static directory filter and (2026-09-20) the personal contact list/sync.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        ApplyFilter();
        await LoadMyContactsAsync();
    }

    private async Task LoadMyContactsAsync()
    {
        IsLoadingMyContacts = true;
        MyContactsErrorMessage = null;
        try
        {
            await SyncFromChatsAsync();
            var contacts = await _contactRepository.GetAllOrderedAsync();
            MyContacts = new ObservableCollection<PersonalContactItem>(contacts.Select(ToItem));
            HasNoMyContacts = MyContacts.Count == 0;
        }
        catch (Exception ex)
        {
            MyContactsErrorMessage = $"Kontakty se nepodařilo načíst: {ex.Message}";
        }
        finally
        {
            IsLoadingMyContacts = false;
        }
    }

    /// <summary>
    /// Auto-creates a <see cref="Contact"/> row for every paired 1:1 peer and every group member this
    /// device doesn't already have one for (matched by public key, so renaming/re-syncing never
    /// duplicates), and refreshes <see cref="Contact.LinkedRelayDeviceId"/> on an existing row whose
    /// peer's routing id has since changed (identity reset — see <c>PeerIdentityReconciler</c>). Never
    /// removes a Contact — a peer who's no longer paired just stops gaining "open chat" behavior
    /// implicitly (their row's link goes stale, same "outlives what it points at" reasoning as
    /// Notification's own related_* columns), the user's manual list is never silently pruned.
    /// </summary>
    private async Task SyncFromChatsAsync()
    {
        byte[] localPublicKey;
        try { localPublicKey = await _messagingService.GetLocalIdentityPublicKeyAsync(); }
        catch { return; }

        var existing = await _contactRepository.GetAllOrderedAsync();
        var existingByKeyHex = existing
            .Where(c => c.LinkedPublicKey is not null)
            .ToDictionary(c => Convert.ToHexStringLower(c.LinkedPublicKey!));
        var nextSortOrder = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1;

        var peers = new List<(string Name, byte[] PublicKey, Guid? RelayDeviceId)>();

        var sessions = await _chatSessionRepository.GetAllAsync();
        peers.AddRange(sessions
            .GroupBy(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey))
            .Select(g => g.OrderByDescending(s => s.ModifiedAtUtc).First())
            .Select(s => (s.PeerDisplayName, s.PeerIdentityPublicKey, s.PeerRelayDeviceId)));

        var groups = await _groupChatRepository.GetAllAsync();
        foreach (var group in groups)
        {
            var members = await _groupMemberRepository.GetByGroupAsync(group.Id);
            foreach (var member in members)
            {
                if (member.PublicKey.AsSpan().SequenceEqual(localPublicKey)) continue;
                peers.Add((member.DisplayName, member.PublicKey, member.RelayDeviceId));
            }
        }

        foreach (var (name, publicKey, relayDeviceId) in peers.DistinctBy(p => Convert.ToHexStringLower(p.PublicKey)))
        {
            var keyHex = Convert.ToHexStringLower(publicKey);
            if (existingByKeyHex.TryGetValue(keyHex, out var existingContact))
            {
                if (relayDeviceId is { } id && existingContact.LinkedRelayDeviceId != id)
                {
                    existingContact.UpdateLink(publicKey, id);
                    await _contactRepository.UpdateAsync(existingContact);
                }
                continue;
            }

            if (relayDeviceId is not { } newRelayDeviceId) continue; // need a routing id to be useful as a chat link
            var contact = new Contact(name, nextSortOrder++, linkedPublicKey: publicKey, linkedRelayDeviceId: newRelayDeviceId);
            await _contactRepository.AddAsync(contact);
        }
    }

    [RelayCommand]
    private void AddContact() => RequestAddContact?.Invoke();

    /// <summary>Finds this contact's active 1:1 session first (the more direct thread), falling back to the first group they're a member of — the user's own ask: "chaty ve kterých uživatel participuje s možností je otevřít a psát zprávy".</summary>
    [RelayCommand]
    private async Task OpenChatAsync(PersonalContactItem? item)
    {
        if (item?.PublicKey is not { } publicKey) return;
        try
        {
            var session = await _chatSessionRepository.GetByPeerPublicKeyAsync(publicKey);
            if (session is not null)
            {
                RequestNavigate?.Invoke($"ChatPage?chatSessionId={session.Id}");
                return;
            }

            var groups = await _groupChatRepository.GetAllAsync();
            foreach (var group in groups)
            {
                var members = await _groupMemberRepository.GetByGroupAsync(group.Id);
                if (members.Any(m => m.PublicKey.AsSpan().SequenceEqual(publicKey)))
                {
                    RequestNavigate?.Invoke($"GroupChatPage?groupChatId={group.Id}");
                    return;
                }
            }

            MyContactsErrorMessage = $"S {item.DisplayName} zatím není žádný aktivní chat.";
        }
        catch (Exception ex)
        {
            MyContactsErrorMessage = $"Chat se nepodařilo otevřít: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteContactAsync(PersonalContactItem? item)
    {
        if (item is null) return;
        try
        {
            await _contactRepository.DeleteAsync(item.Id);
            MyContacts = new ObservableCollection<PersonalContactItem>(MyContacts.Where(c => c.Id != item.Id));
            HasNoMyContacts = MyContacts.Count == 0;
        }
        catch (Exception ex)
        {
            MyContactsErrorMessage = $"Kontakt se nepodařilo smazat: {ex.Message}";
        }
    }

    /// <summary>
    /// Desktop drag-and-drop reorder (2026-09-20, user's own ask: "na pc možnost v kontaktech
    /// přetahováním měnit umístění kontaktu") — called from <c>ContactsPage</c>'s code-behind
    /// DragGestureRecognizer/DropGestureRecognizer handlers, which is where MAUI's drag/drop API
    /// itself lives (no plain XAML-bindable command shape for it), same "MAUI-touching glue in the
    /// Page, everything else here" split this ViewModel already keeps for navigation.
    /// </summary>
    public async Task ReorderAsync(Guid draggedId, Guid targetId)
    {
        if (draggedId == targetId) return;
        var list = MyContacts.ToList();
        var draggedIndex = list.FindIndex(c => c.Id == draggedId);
        var targetIndex = list.FindIndex(c => c.Id == targetId);
        if (draggedIndex < 0 || targetIndex < 0) return;

        var dragged = list[draggedIndex];
        list.RemoveAt(draggedIndex);
        list.Insert(targetIndex, dragged);
        MyContacts = new ObservableCollection<PersonalContactItem>(list);

        try
        {
            var contacts = (await _contactRepository.GetAllOrderedAsync()).ToDictionary(c => c.Id);
            for (var i = 0; i < list.Count; i++)
            {
                if (!contacts.TryGetValue(list[i].Id, out var contact) || contact.SortOrder == i) continue;
                contact.SetSortOrder(i);
                await _contactRepository.UpdateAsync(contact);
            }
        }
        catch (Exception ex)
        {
            MyContactsErrorMessage = $"Pořadí se nepodařilo uložit: {ex.Message}";
        }
    }

    private PersonalContactItem ToItem(Contact c)
    {
        var subtitleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.Phone)) subtitleParts.Add(c.Phone);
        if (!string.IsNullOrWhiteSpace(c.Email)) subtitleParts.Add(c.Email);
        if (c.LinkedPublicKey is not null && subtitleParts.Count == 0) subtitleParts.Add("Kontakt ze SecureApp");

        return new PersonalContactItem(
            c.Id, c.DisplayName, string.Join(" · ", subtitleParts), c.LinkedPublicKey,
            c.LinkedPublicKey is not null, OpenChatCommand, DeleteContactCommand);
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

/// <summary>One row in "Moje kontakty" (2026-09-20) — <see cref="Subtitle"/> is pre-joined (phone · email, or "Kontakt ze SecureApp" for a linked contact with neither) so the DataTemplate needs no visibility triggers per field, this codebase's established "no converters" convention. <see cref="PublicKey"/> is carried for lookup only (never shown), <see cref="HasChat"/> gates whether the 💬 action renders at all.</summary>
public sealed record PersonalContactItem(Guid Id, string DisplayName, string Subtitle, byte[]? PublicKey, bool HasChat, ICommand OpenChatCommand, ICommand DeleteCommand);
