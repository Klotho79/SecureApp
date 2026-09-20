using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// One notification's detail (NOTIFICATION_HUB_SPEC.md §15) — title/source/time/content/priority/
/// status plus the four actions the spec calls for (mark read is already applied the moment the row
/// is tapped, in <see cref="NotificationsViewModel.OpenNotificationCommand"/>, so this page's own
/// actions are Mark Important / Archive / Open Related). "Share" is deliberately not offered — this
/// app's notifications are derived from E2EE chat content or internal state, and sharing that outside
/// the app would be a real data-handling decision the spec's generic wording doesn't actually call
/// for here.
/// </summary>
public sealed partial class NotificationDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly INotificationRepository _notificationRepository;
    private Guid _notificationId;
    private Guid? _relatedChatSessionId;
    private Guid? _relatedGroupChatId;
    private bool _hasRelatedLibraryFile;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasErrorMessage { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Body { get; set; }

    [ObservableProperty]
    public partial string CategoryText { get; set; }

    [ObservableProperty]
    public partial string PriorityText { get; set; }

    [ObservableProperty]
    public partial string TimeText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsManuallyImportant { get; set; }

    [ObservableProperty]
    public partial bool IsArchived { get; set; }

    [ObservableProperty]
    public partial bool HasRelated { get; set; }

    /// <summary>Raised so the Page (which alone can call <c>Shell.Current.GoToAsync</c>) navigates — see <c>NewChatPage</c>-era ViewModel/Page split this codebase already established for MAUI-only concerns.</summary>
    public event Action<string>? RequestNavigate;

    public NotificationDetailViewModel(INotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        Title = string.Empty;
        Body = string.Empty;
        CategoryText = string.Empty;
        PriorityText = string.Empty;
        TimeText = string.Empty;
        StatusText = string.Empty;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("notificationId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _notificationId = id;
    }

    partial void OnErrorMessageChanged(string? value) => HasErrorMessage = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var notification = await _notificationRepository.GetByIdAsync(_notificationId);
            if (notification is null)
            {
                ErrorMessage = "Toto oznámení už neexistuje — bylo pravděpodobně smazáno.";
                return;
            }

            Title = notification.Title;
            Body = notification.Body;
            CategoryText = CategoryDisplayName(notification.Category);
            PriorityText = PriorityDisplayName(notification.Priority);
            TimeText = notification.CreatedAtUtc.ToLocalTime().ToString("d. M. yyyy HH:mm", CultureInfo.CurrentCulture);
            StatusText = notification.IsArchived ? "Archivováno" : notification.IsRead ? "Přečteno" : "Nové";
            IsManuallyImportant = notification.IsManuallyImportant;
            IsArchived = notification.IsArchived;

            _relatedChatSessionId = notification.RelatedChatSessionId;
            _relatedGroupChatId = notification.RelatedGroupChatId;
            _hasRelatedLibraryFile = notification.RelatedLibraryFileId is not null;
            HasRelated = _relatedChatSessionId is not null || _relatedGroupChatId is not null || _hasRelatedLibraryFile;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nepodařilo se načíst oznámení: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleImportantAsync()
    {
        try
        {
            var notification = await _notificationRepository.GetByIdAsync(_notificationId);
            if (notification is null) return;
            notification.SetManuallyImportant(!notification.IsManuallyImportant);
            await _notificationRepository.UpdateAsync(notification);
            IsManuallyImportant = notification.IsManuallyImportant;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nepodařilo se změnit důležitost: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleArchivedAsync()
    {
        try
        {
            var notification = await _notificationRepository.GetByIdAsync(_notificationId);
            if (notification is null) return;
            if (notification.IsArchived) notification.Unarchive(); else notification.Archive();
            await _notificationRepository.UpdateAsync(notification);
            IsArchived = notification.IsArchived;
            StatusText = notification.IsArchived ? "Archivováno" : "Přečteno";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Nepodařilo se archivovat: {ex.Message}";
        }
    }

    /// <summary>Prefers the group over the 1:1 session when both exist (a group message notification carries both — see <c>NotificationPublisher.PublishGroupMessageAsync</c>) since the group is the actual thread the user reads it in. Falls back to the shared-library browsing tab when only a library file relation exists — no per-file deep-link route exists yet to jump straight to one file (see NOTIFICATION_HUB_SPEC.md's Status section).</summary>
    [RelayCommand]
    private void OpenRelated()
    {
        if (_relatedGroupChatId is { } groupChatId)
            RequestNavigate?.Invoke($"GroupChatPage?groupChatId={groupChatId}");
        else if (_relatedChatSessionId is { } chatSessionId)
            RequestNavigate?.Invoke($"ChatPage?chatSessionId={chatSessionId}");
        else if (_hasRelatedLibraryFile)
            RequestNavigate?.Invoke("//LibraryTab");
    }

    private static string CategoryDisplayName(NotificationCategory category) => category switch
    {
        NotificationCategory.Chat => "Chat",
        NotificationCategory.Group => "Skupina",
        NotificationCategory.Library => "Knihovna",
        NotificationCategory.Logbook => "Logbook",
        NotificationCategory.System => "Systém",
        _ => "Ostatní"
    };

    private static string PriorityDisplayName(NotificationPriority priority) => priority switch
    {
        NotificationPriority.Critical => "Kritická",
        NotificationPriority.Important => "Důležitá",
        NotificationPriority.Normal => "Normální",
        _ => "Informativní"
    };
}
