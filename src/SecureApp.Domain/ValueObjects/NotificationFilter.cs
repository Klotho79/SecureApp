using SecureApp.Domain.Enums;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// What <see cref="Interfaces.Repositories.INotificationRepository.GetPagedAsync"/> narrows a page
/// to — mirrors the Notification Hub's filter chips (NOTIFICATION_HUB_SPEC.md §12). Every field is
/// additive (AND'd together, never OR'd), so combining filters always narrows, never widens.
///
/// The archive has three states, not two: <see cref="ArchivedOnly"/> (the "Archiv" chip — only
/// archived rows), plain default (only ACTIVE, non-archived rows — every other chip), and
/// <see cref="IncludeArchived"/> (both together — reserved for a future cross-archive search, spec
/// §14: "archive must support search" alongside everything else). <see cref="ArchivedOnly"/> wins if
/// both are somehow set.
/// </summary>
public sealed record NotificationFilter(
    bool IncludeArchived = false,
    bool ArchivedOnly = false,
    bool OnlyImportant = false,
    bool OnlyUnread = false,
    NotificationCategory? Category = null,
    string? SearchText = null)
{
    public static readonly NotificationFilter Default = new();
}
