namespace SecureApp.Domain.ValueObjects;

/// <summary>Badge/counter numbers for the Notification Hub's dashboard header (NOTIFICATION_HUB_SPEC.md §10/§13) — always computed over non-archived notifications only.</summary>
public sealed record NotificationCounts(int ImportantCount, int UnreadCount, int TodayCount);
