using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// One row in the Notification Hub (NOTIFICATION_HUB_SPEC.md) — SecureApp's own events (a new chat/
/// group message, a pairing, a shared-library addition, a diagnostic-log warning, …) repackaged into
/// one unified, archivable feed, distinct from the underlying event itself. Archiving a Notification
/// never touches the thing it's about (the Message/GroupChat/LibraryFile stays exactly as it was);
/// this is purely a "have I dealt with this" layer on top.
///
/// <see cref="RelatedChatSessionId"/>/<see cref="RelatedGroupChatId"/>/<see cref="RelatedLibraryFileId"/>
/// are the only relation FKs populated for now — the spec's own Person/Workplace/CalendarEvent
/// relations (§20) are deliberately not modeled yet, since those entities don't exist in this app at
/// all (Phase 5/6 of the spec); adding them later is additive, not a breaking change to this entity.
/// </summary>
public sealed class Notification : Entity
{
    public string Title { get; private set; }
    public string Body { get; private set; }
    public NotificationCategory Category { get; private set; }
    public NotificationPriority Priority { get; private set; }
    public bool IsRead { get; private set; }
    public bool IsArchived { get; private set; }

    /// <summary>Manually pinned by the user regardless of <see cref="Priority"/> — see <see cref="IsImportant"/>.</summary>
    public bool IsManuallyImportant { get; private set; }

    public Guid? RelatedChatSessionId { get; private set; }
    public Guid? RelatedGroupChatId { get; private set; }
    public Guid? RelatedLibraryFileId { get; private set; }

    private Notification()
    {
        // Reserved for materialization by persistence infrastructure.
        Title = string.Empty;
        Body = string.Empty;
    }

    public Notification(
        string title,
        string body,
        NotificationCategory category,
        NotificationPriority priority,
        Guid? relatedChatSessionId = null,
        Guid? relatedGroupChatId = null,
        Guid? relatedLibraryFileId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));

        Title = title;
        Body = body ?? string.Empty;
        Category = category;
        Priority = priority;
        RelatedChatSessionId = relatedChatSessionId;
        RelatedGroupChatId = relatedGroupChatId;
        RelatedLibraryFileId = relatedLibraryFileId;
    }

    /// <summary>The IMPORTANT section's own membership test (spec §4): Critical/Important priority OR manually pinned.</summary>
    public bool IsImportant => Priority >= NotificationPriority.Important || IsManuallyImportant;

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        Touch();
    }

    public void MarkUnread()
    {
        if (!IsRead) return;
        IsRead = false;
        Touch();
    }

    public void SetManuallyImportant(bool value)
    {
        if (IsManuallyImportant == value) return;
        IsManuallyImportant = value;
        Touch();
    }

    /// <summary>Archiving is never deletion (spec §14) — stays stored/searchable/restorable, just off the active lists.</summary>
    public void Archive()
    {
        if (IsArchived) return;
        IsArchived = true;
        Touch();
    }

    public void Unarchive()
    {
        if (!IsArchived) return;
        IsArchived = false;
        Touch();
    }
}
