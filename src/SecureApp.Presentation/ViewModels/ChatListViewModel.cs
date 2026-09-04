using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Lists existing chat sessions. Split like <see cref="DocumentBrowserViewModel"/>: this partial
/// is free of any MAUI type (only Domain interfaces + CommunityToolkit.Mvvm), so it's directly
/// testable from a plain console app; <c>ChatListViewModel.Actions.cs</c> holds the two
/// Shell-navigation commands.
/// </summary>
public sealed partial class ChatListViewModel : ObservableObject
{
    private readonly IChatSessionRepository _sessionRepository;

    [ObservableProperty]
    public partial ObservableCollection<ChatSessionItem> Sessions { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public ChatListViewModel(IChatSessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        Sessions = [];
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var sessions = await _sessionRepository.GetAllAsync();
            Sessions = new ObservableCollection<ChatSessionItem>(
                sessions.OrderByDescending(s => s.LastRatchetedAtUtc ?? s.CreatedAtUtc)
                    .Select(s => new ChatSessionItem(s.Id, s.PeerDisplayName, s.State, DescribeLastActivity(s))));
            IsEmpty = Sessions.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string DescribeLastActivity(ChatSession session) => session.State switch
    {
        ChatSessionState.Closed => "Closed",
        ChatSessionState.PendingHandshake => "Waiting to connect…",
        _ => session.LastRatchetedAtUtc is { } last ? last.LocalDateTime.ToString("g") : "No messages yet"
    };
}

public sealed record ChatSessionItem(Guid Id, string PeerDisplayName, ChatSessionState State, string LastActivityText);
