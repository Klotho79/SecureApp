using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
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

    private readonly INotificationRepository _notificationRepository;

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

    public NotificationsViewModel(INotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        Sections = [];
        HasNoNotifications = true;

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

            var activeFilter = FilterChips.FirstOrDefault(c => c.IsSelected)?.Filter ?? NotificationFilter.Default;
            var notifications = await _notificationRepository.GetPagedAsync(activeFilter, PageSize);
            BuildSections(notifications);
        }
        finally
        {
            IsLoading = false;
        }
    }

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
