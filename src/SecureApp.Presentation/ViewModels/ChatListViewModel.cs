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
                    .Select(s => new ChatSessionItem(s.Id, s.PeerDisplayName, s.State, DescribeLastActivity(s), ComputeInitials(s.PeerDisplayName))));
            IsEmpty = Sessions.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string DescribeLastActivity(ChatSession session) => session.State switch
    {
        ChatSessionState.Closed => "Uzavřeno",
        ChatSessionState.PendingHandshake => "Čeká na připojení…",
        _ => session.LastRatchetedAtUtc is { } last ? last.LocalDateTime.ToString("g") : "Zatím žádné zprávy"
    };

    /// <summary>Up to 2 letters for the avatar circle in the redesigned list (2026-09-06) — first letter of up to the first two words, e.g. "Dr. B. Chen" -&gt; "DB". Plain string, not a MAUI Color, to keep this partial's own stated MAUI-free claim true; the avatar's actual color is one fixed accent tint set in XAML, not per-contact.</summary>
    private static string ComputeInitials(string displayName)
    {
        var letters = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]))
            .Take(2)
            .ToArray();
        return letters.Length == 0 ? "?" : new string(letters);
    }
}

public sealed record ChatSessionItem(Guid Id, string PeerDisplayName, ChatSessionState State, string LastActivityText, string Initials);
