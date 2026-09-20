using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// The Notification Hub's main list (NOTIFICATION_HUB_SPEC.md §3–§5, §13, §16) — one screen, filter
/// chips across the top narrowing a single query, results grouped by <see cref="NotificationCategory"/>
/// into collapsible sections (same <c>ContactSectionGroup</c>/<c>VisibleEntries</c> pattern
/// <c>ContactsViewModel</c> already established for a large collapsible list). Counters at the top
/// (Important/Unread/Today) answer spec §13's "what needs attention / what's new" before the user
/// even scrolls.
///
/// No MAUI dependency — page-count is fixed (<see cref="PageSize"/>) rather than true incremental
/// "load older" for this first pass; see NOTIFICATION_HUB_SPEC.md's Status section for what's
/// deliberately deferred (full Archive search/pagination is its own later phase, §4 of the spec's
/// own phase list).
/// </summary>
public sealed partial class NotificationsViewModel : ObservableObject
{
    private const int PageSize = 100;

    private const int LibraryQuickAccessLimit = 20;

    private readonly INotificationRepository _notificationRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IGroupChatRepository _groupChatRepository;
    private readonly ISharedLibraryService _sharedLibraryService;
    private readonly IDocumentRepository _documentRepository;

    [ObservableProperty]
    public partial ObservableCollection<NotificationSectionGroup> Sections { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasNoNotifications { get; set; }

    [ObservableProperty]
    public partial int ImportantCount { get; set; }

    [ObservableProperty]
    public partial int UnreadCount { get; set; }

    [ObservableProperty]
    public partial int TodayCount { get; set; }

    public ObservableCollection<NotificationFilterChip> FilterChips { get; }

    [ObservableProperty]
    public partial ObservableCollection<ChatQuickAccessItem> ChatQuickAccess { get; set; }

    [ObservableProperty]
    public partial bool HasChatQuickAccess { get; set; }

    [ObservableProperty]
    public partial bool HasNoChatQuickAccess { get; set; }

    /// <summary>True while the "Chat" filter chip is the active one — gates whether the quick-access chat/group list (below) renders at all, since it only makes sense for that one chip.</summary>
    [ObservableProperty]
    public partial bool IsChatFilterActive { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<LibraryQuickAccessItem> LibraryQuickAccess { get; set; }

    [ObservableProperty]
    public partial bool HasLibraryQuickAccess { get; set; }

    [ObservableProperty]
    public partial bool HasNoLibraryQuickAccess { get; set; }

    /// <summary>True while the "Knihovna" filter chip is the active one — gates the quick-access file list (both the shared community library and this device's own local Dokumenty — 2026-09-20, user's own ask: "do knihovny pridej co je v knihovne a pridej do knihovny i zalozku dokumenty... to propoj").</summary>
    [ObservableProperty]
    public partial bool IsLibraryFilterActive { get; set; }

    /// <summary>Raised so the Page (which alone can push a MAUI navigation) opens the tapped chat/group/file or jumps to a full browsing tab — same MAUI-free-ViewModel split this codebase already established elsewhere.</summary>
    public event Action<string>? RequestNavigate;

    partial void OnHasChatQuickAccessChanged(bool value) => HasNoChatQuickAccess = !value;
    partial void OnHasLibraryQuickAccessChanged(bool value) => HasNoLibraryQuickAccess = !value;

    public NotificationsViewModel(
        INotificationRepository notificationRepository,
        IChatSessionRepository chatSessionRepository,
        IGroupChatRepository groupChatRepository,
        ISharedLibraryService sharedLibraryService,
        IDocumentRepository documentRepository)
    {
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _groupChatRepository = groupChatRepository ?? throw new ArgumentNullException(nameof(groupChatRepository));
        _sharedLibraryService = sharedLibraryService ?? throw new ArgumentNullException(nameof(sharedLibraryService));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        Sections = [];
        HasNoNotifications = true;
        ChatQuickAccess = [];
        HasNoChatQuickAccess = true;
        LibraryQuickAccess = [];
        HasNoLibraryQuickAccess = true;

        // Built once, with THIS instance's own SelectCommand baked in per chip (same "shared command
        // instance, no x:Reference back to the page" pattern this codebase already uses for
        // DirectoryMemberItem/PendingActivationItem-style rows), so the very first chip ("Vše") can
        // be marked selected before LoadAsync's first real query even runs.
        FilterChips =
        [
            new NotificationFilterChip("Vše", NotificationFilter.Default, SelectFilterCommand) { IsSelected = true },
            new NotificationFilterChip("Důležité", NotificationFilter.Default with { OnlyImportant = true }, SelectFilterCommand),
            new NotificationFilterChip("Nepřečtené", NotificationFilter.Default with { OnlyUnread = true }, SelectFilterCommand),
            new NotificationFilterChip("Chat", NotificationFilter.Default with { Category = NotificationCategory.Chat }, SelectFilterCommand),
            new NotificationFilterChip("Knihovna", NotificationFilter.Default with { Category = NotificationCategory.Library }, SelectFilterCommand),
            new NotificationFilterChip("Systém", NotificationFilter.Default with { Category = NotificationCategory.System }, SelectFilterCommand),
            new NotificationFilterChip("Archiv", NotificationFilter.Default with { ArchivedOnly = true }, SelectFilterCommand),
        ];
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var counts = await _notificationRepository.GetCountsAsync();
            ImportantCount = counts.ImportantCount;
            UnreadCount = counts.UnreadCount;
            TodayCount = counts.TodayCount;

            var activeChip = FilterChips.FirstOrDefault(c => c.IsSelected);
            var activeFilter = activeChip?.Filter ?? NotificationFilter.Default;
            var notifications = await _notificationRepository.GetPagedAsync(activeFilter, PageSize);
            BuildSections(notifications);

            // The "Chat" chip only ever filters Notification rows by Category — i.e. past
            // new-message ALERTS, which is empty until a message has actually arrived since this
            // feature shipped (2026-09-20, user caught this live: "v záložce chat na nástěnce se
            // chaty nezobrazují"). What's actually wanted here is quick access to the chats/groups
            // themselves, so this chip additionally loads a live list of them, same "open + write"
            // ask from the very first Notification Hub request.
            IsChatFilterActive = activeChip?.Filter.Category == NotificationCategory.Chat;
            if (IsChatFilterActive)
                await LoadChatQuickAccessAsync();
            else
            {
                ChatQuickAccess = [];
                HasChatQuickAccess = false;
            }

            // Same fix, same reasoning, for "Knihovna" (2026-09-20, user's own follow-up ask: "do
            // knihovny pridej co je v knihovne a pridej do knihovny i zalozku dokumenty zkratka to
            // propoj") — the chip alone only ever filtered past library-upload ALERTS. This loads
            // what's actually IN the shared community library, plus (the user's explicit ask) this
            // device's own local Dokumenty, connected into the same list.
            IsLibraryFilterActive = activeChip?.Filter.Category == NotificationCategory.Library;
            if (IsLibraryFilterActive)
                await LoadLibraryQuickAccessAsync();
            else
            {
                LibraryQuickAccess = [];
                HasLibraryQuickAccess = false;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadChatQuickAccessAsync()
    {
        try
        {
            var items = new List<ChatQuickAccessItem>();

            var sessions = await _chatSessionRepository.GetAllAsync();
            items.AddRange(sessions
                .Where(s => s.State != ChatSessionState.Closed)
                .GroupBy(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey))
                .Select(g => g.OrderByDescending(s => s.ModifiedAtUtc).First())
                .Select(s => new ChatQuickAccessItem(s.PeerDisplayName, "💬", $"ChatPage?chatSessionId={s.Id}", OpenChatCommand)));

            var groups = await _groupChatRepository.GetAllAsync();
            items.AddRange(groups.Select(g => new ChatQuickAccessItem(g.Name, "👥", $"GroupChatPage?groupChatId={g.Id}", OpenChatCommand)));

            ChatQuickAccess = new ObservableCollection<ChatQuickAccessItem>(items.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase));
            HasChatQuickAccess = ChatQuickAccess.Count > 0;
        }
        catch
        {
            ChatQuickAccess = [];
            HasChatQuickAccess = false;
        }
    }

    [RelayCommand]
    private void OpenChat(ChatQuickAccessItem? item)
    {
        if (item is null) return;
        RequestNavigate?.Invoke(item.Route);
    }

    /// <summary>
    /// Combines this device's own local Dokumenty (each opens straight into DocumentViewerPage —
    /// full deep link, since a local Document's id is always locally resolvable) with the shared
    /// community Knihovna (falls back to the browsing tab itself — no per-file deep-link route exists
    /// yet, same limitation NotificationDetailViewModel.OpenRelated already has for a library-file
    /// relation). Capped at <see cref="LibraryQuickAccessLimit"/> combined — this is a quick-access
    /// jump list, not a replacement for the real Knihovna/Dokumenty browsers (both stay one tap away
    /// via the two "open the full tab" rows always shown first).
    /// </summary>
    private async Task LoadLibraryQuickAccessAsync()
    {
        try
        {
            var items = new List<LibraryQuickAccessItem>();

            var documents = await _documentRepository.GetAllAsync();
            items.AddRange(documents
                .OrderByDescending(d => d.ModifiedAtUtc)
                .Take(LibraryQuickAccessLimit)
                .Select(d => new LibraryQuickAccessItem(d.Title, "📄 Dokumenty (toto zařízení)", $"DocumentViewerPage?documentId={d.Id}", OpenLibraryItemCommand)));

            var libraryFiles = await _sharedLibraryService.SearchAsync();
            items.AddRange(libraryFiles
                .OrderByDescending(f => f.UploadedAtUtc)
                .Take(LibraryQuickAccessLimit)
                .Select(f => new LibraryQuickAccessItem(f.FileName, "📚 Sdílená knihovna", "//LibraryTab", OpenLibraryItemCommand)));

            LibraryQuickAccess = new ObservableCollection<LibraryQuickAccessItem>(items);
            HasLibraryQuickAccess = LibraryQuickAccess.Count > 0;
        }
        catch
        {
            LibraryQuickAccess = [];
            HasLibraryQuickAccess = false;
        }
    }

    [RelayCommand]
    private void OpenLibraryItem(LibraryQuickAccessItem? item)
    {
        if (item is null) return;
        RequestNavigate?.Invoke(item.Route);
    }

    [RelayCommand]
    private void OpenLibraryTab() => RequestNavigate?.Invoke("//LibraryTab");

    [RelayCommand]
    private void OpenDocumentsTab() => RequestNavigate?.Invoke("//DocumentBrowser");

    [RelayCommand]
    private async Task SelectFilterAsync(NotificationFilterChip? chip)
    {
        if (chip is null) return;
        foreach (var c in FilterChips)
            c.IsSelected = c == chip;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenNotificationAsync(NotificationListItem? item)
    {
        if (item is null) return;
        try
        {
            var notification = await _notificationRepository.GetByIdAsync(item.Id);
            if (notification is null) return;
            if (!notification.IsRead)
            {
                notification.MarkRead();
                await _notificationRepository.UpdateAsync(notification);
            }
        }
        finally
        {
            // Navigation itself lives in the Page's code-behind (needs Shell.Current) — mirrors the
            // DocumentBrowserViewModel/GroupChatViewModel split of "pure logic here, MAUI-touching
            // navigation at the call site" this codebase already established.
            RequestOpenDetail?.Invoke(item.Id);
        }
        await LoadAsync();
    }

    /// <summary>Raised after a tapped notification is marked read — the Page subscribes and does the actual <c>GoToAsync</c>, since Shell navigation needs a MAUI type this ViewModel deliberately doesn't reference.</summary>
    public event Action<Guid>? RequestOpenDetail;

    private void BuildSections(IReadOnlyList<Notification> notifications)
    {
        var groups = notifications
            .GroupBy(n => n.Category)
            .OrderBy(g => CategorySortOrder(g.Key))
            .Select(g => new NotificationSectionGroup(
                CategoryDisplayName(g.Key),
                g.Select(ToListItem).ToList(),
                // Today's/Yesterday's-equivalent default: the two highest-signal groups (Důležité via
                // priority-mixed categories aren't a single group here, so this just means "small
                // groups start open, large ones start collapsed" — spec §28's own suggested default,
                // adapted since this groups by category, not by day, for this first pass.
                initiallyExpanded: g.Count() <= 5))
            .ToList();

        Sections = new ObservableCollection<NotificationSectionGroup>(groups);
        HasNoNotifications = Sections.Count == 0;
    }

    private NotificationListItem ToListItem(Notification n) => new(
        n.Id,
        n.Title,
        n.Body,
        n.CreatedAtUtc.ToLocalTime().ToString("d. M. HH:mm", CultureInfo.CurrentCulture),
        n.IsRead,
        n.Priority.ToString(),
        OpenNotificationCommand);

    private static string CategoryDisplayName(NotificationCategory category) => category switch
    {
        NotificationCategory.Chat => "Zprávy",
        NotificationCategory.Group => "Skupiny",
        NotificationCategory.Library => "Knihovna",
        NotificationCategory.Logbook => "Logbook",
        NotificationCategory.System => "Systém",
        _ => "Ostatní"
    };

    private static int CategorySortOrder(NotificationCategory category) => category switch
    {
        NotificationCategory.System => 0,
        NotificationCategory.Chat => 1,
        NotificationCategory.Group => 2,
        NotificationCategory.Library => 3,
        NotificationCategory.Logbook => 4,
        _ => 5
    };
}

/// <summary>One collapsible category section — same <c>VisibleEntries</c>-gated-BindableLayout pattern as <c>ContactsViewModel.ContactSectionGroup</c>: empty until expanded, so a still-collapsed section costs nothing to render.</summary>
public sealed partial class NotificationSectionGroup : ObservableObject
{
    public string Name { get; }
    public IReadOnlyList<NotificationListItem> Entries { get; }
    public int Count => Entries.Count;

    public IReadOnlyList<NotificationListItem> VisibleEntries => IsExpanded ? Entries : [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public NotificationSectionGroup(string name, IReadOnlyList<NotificationListItem> entries, bool initiallyExpanded)
    {
        Name = name;
        Entries = entries;
        IsExpanded = initiallyExpanded;
    }

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(VisibleEntries));

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;
}

/// <summary>One notification row. <see cref="PriorityText"/> stays the English enum name (the XAML template colors it via DataTrigger, doesn't translate it) — same "wire-level concept stays English" convention already established for <c>DiagnosticLogItem.Level</c>/<c>TransportConnectionState</c>.</summary>
public sealed record NotificationListItem(Guid Id, string Title, string Preview, string TimeText, bool IsRead, string PriorityText, ICommand OpenCommand);

/// <summary>One row in the "Chat" chip's quick-access list (2026-09-20) — a live chat/group the user can jump straight into and write, not a notification. <see cref="Glyph"/> distinguishes a 1:1 (💬) from a group (👥) without a converter.</summary>
public sealed record ChatQuickAccessItem(string DisplayName, string Glyph, string Route, ICommand OpenCommand);

/// <summary>One row in the "Knihovna" chip's quick-access list (2026-09-20) — a real file (local Dokumenty or the shared community Knihovna), not a notification. <see cref="SourceLabel"/> ("📄 Dokumenty (toto zařízení)" / "📚 Sdílená knihovna") is pre-formatted so the two sources are visually told apart with no converter.</summary>
public sealed record LibraryQuickAccessItem(string DisplayName, string SourceLabel, string Route, ICommand OpenCommand);

/// <summary>One filter chip — carries the shared <see cref="SelectCommand"/> instance, same per-item-shared-command pattern as <see cref="NotificationListItem.OpenCommand"/>.</summary>
public sealed partial class NotificationFilterChip : ObservableObject
{
    public string Label { get; }
    public NotificationFilter Filter { get; }
    public ICommand SelectCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public NotificationFilterChip(string label, NotificationFilter filter, ICommand selectCommand)
    {
        Label = label;
        Filter = filter;
        SelectCommand = selectCommand;
    }
}
