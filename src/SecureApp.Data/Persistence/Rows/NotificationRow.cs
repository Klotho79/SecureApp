using SQLite;

namespace SecureApp.Data.Persistence.Rows;

/// <summary>sqlite-net's read-side row shape for <c>notifications</c> — see <see cref="DocumentRow"/> for why this exists.</summary>
[Table("notifications")]
internal sealed class NotificationRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("title")] public string Title { get; set; } = string.Empty;
    [Column("body")] public string Body { get; set; } = string.Empty;
    [Column("category")] public int Category { get; set; }
    [Column("priority")] public int Priority { get; set; }
    [Column("is_read")] public int IsRead { get; set; }
    [Column("is_archived")] public int IsArchived { get; set; }
    [Column("is_manually_important")] public int IsManuallyImportant { get; set; }
    [Column("related_chat_session_id")] public string? RelatedChatSessionId { get; set; }
    [Column("related_group_chat_id")] public string? RelatedGroupChatId { get; set; }
    [Column("related_library_file_id")] public string? RelatedLibraryFileId { get; set; }
    [Column("created_at_utc")] public string CreatedAtUtc { get; set; } = string.Empty;
    [Column("modified_at_utc")] public string ModifiedAtUtc { get; set; } = string.Empty;
}
