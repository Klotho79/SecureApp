using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Contacts;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Global Smart Search (NOTIFICATION_HUB_SPEC.md §11–§12) — one query fanned out across every
/// SecureApp data source that's actually searchable today: Notifications (title/body), 1:1 chats and
/// groups (by peer/group display name, so "Novák" jumps straight to that thread), and the static
/// hospital phone directory (name/section/extension). Person/Workplace/CalendarEvent search (spec
/// §11's own "Novak" example also expects a Person match) isn't wired in — those entities don't exist
/// in this app yet (spec Phases 5/6); adding a source here later is additive; see NOTIFICATION_HUB_SPEC.md's Status.
///
/// Deliberately NOT "exact text match only" for chats/groups/contacts — a Contains match already
/// covers the spec's own examples ("Novak" finding a contact and any chat/group with that name) even
/// without the fuzzier "conceptually related" matching the spec's fuller wording gestures at (real
/// full-text/semantic search over notification content is a later refinement, not this first pass).
/// </summary>
public sealed partial class SmartSearchViewModel : ObservableObject
{
    private const int MaxResultsPerSection = 8;

    private readonly INotificationRepository _notificationRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IGroupChatRepository _groupChatRepository;

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial bool HasQuery { get; set; }

    [ObservableProperty]
    public partial bool HasNoResults { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SearchResultItem> NotificationResults { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SearchResultItem> ChatResults { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SearchResultItem> GroupResults { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SearchResultItem> ContactResults { get; set; }

    public bool HasNotificationResults => NotificationResults.Count > 0;
    public bool HasChatResults => ChatResults.Count > 0;
    public bool HasGroupResults => GroupResults.Count > 0;
    public bool HasContactResults => ContactResults.Count > 0;

    /// <summary>Raised with a route string ("ChatPage?chatSessionId=…", "NotificationDetailPage?notificationId=…", …) — the Page does the actual <c>GoToAsync</c>, same MAUI-free-ViewModel split this codebase already established.</summary>
    public event Action<string>? RequestNavigate;

    public SmartSearchViewModel(
        INotificationRepository notificationRepository,
        IChatSessionRepository chatSessionRepository,
        IGroupChatRepository groupChatRepository)
    {
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        _chatSessionRepository = chatSessionRepository ?? throw new ArgumentNullException(nameof(chatSessionRepository));
        _groupChatRepository = groupChatRepository ?? throw new ArgumentNullException(nameof(groupChatRepository));
        SearchQuery = string.Empty;
        NotificationResults = [];
        ChatResults = [];
        GroupResults = [];
        ContactResults = [];
    }

    partial void OnSearchQueryChanged(string value) => _ = SearchAsync();

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        HasQuery = query.Length > 0;
        if (!HasQuery)
        {
            NotificationResults = [];
            ChatResults = [];
            GroupResults = [];
            ContactResults = [];
            RaiseHasResultsChanged();
            HasNoResults = false;
            return;
        }

        IsSearching = true;
        try
        {
            var notifications = await _notificationRepository.GetPagedAsync(NotificationFilter.Default with { SearchText = query }, MaxResultsPerSection);
            NotificationResults = new ObservableCollection<SearchResultItem>(notifications.Select(n => new SearchResultItem(
                n.Title, n.Body, $"NotificationDetailPage?notificationId={n.Id}", OpenCommand)));

            var sessions = await _chatSessionRepository.GetAllAsync();
            ChatResults = new ObservableCollection<SearchResultItem>(sessions
                .Where(s => s.PeerDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(s => s.PeerIdentityPublicKey, ByteArrayEqualityComparer.Instance)
                .Take(MaxResultsPerSection)
                .Select(s => new SearchResultItem(s.PeerDisplayName, "1:1 chat", $"ChatPage?chatSessionId={s.Id}", OpenCommand)));

            var groups = await _groupChatRepository.GetAllAsync();
            GroupResults = new ObservableCollection<SearchResultItem>(groups
                .Where(g => g.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(MaxResultsPerSection)
                .Select(g => new SearchResultItem(g.Name, "Skupina", $"GroupChatPage?groupChatId={g.Id}", OpenCommand)));

            ContactResults = new ObservableCollection<SearchResultItem>(ContactDirectoryData.PhoneDirectory
                .Where(e =>
                    e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Number.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    e.Section.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(MaxResultsPerSection)
                .Select(e => new SearchResultItem(e.Name, $"{e.Section} · kl. {e.Number}", "//ContactsTab", OpenCommand)));

            RaiseHasResultsChanged();
            HasNoResults = !HasNotificationResults && !HasChatResults && !HasGroupResults && !HasContactResults;
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void Open(SearchResultItem? item)
    {
        if (item is null) return;
        RequestNavigate?.Invoke(item.Route);
    }

    private void RaiseHasResultsChanged()
    {
        OnPropertyChanged(nameof(HasNotificationResults));
        OnPropertyChanged(nameof(HasChatResults));
        OnPropertyChanged(nameof(HasGroupResults));
        OnPropertyChanged(nameof(HasContactResults));
    }

    private sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayEqualityComparer Instance = new();
        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.AsSpan().SequenceEqual(y);
        public int GetHashCode(byte[] obj) => Convert.ToHexStringLower(obj).GetHashCode();
    }
}

/// <summary>One search result row, already carrying the route it navigates to — built once per query rather than resolved at tap time, since by then the underlying list may have moved on.</summary>
public sealed record SearchResultItem(string Title, string Subtitle, string Route, ICommand OpenCommand);
