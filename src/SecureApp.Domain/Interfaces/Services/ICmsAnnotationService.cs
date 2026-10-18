using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Shared annotations on CMS documents (CMS_PLAN.md, phase 4). The relay enforces who sees what
/// (private = author only, selected = author + chosen members, all = everyone, Admin = everything) and
/// stores only an opaque, shared-key-encrypted body; this service encrypts/decrypts it.
/// </summary>
public interface ICmsAnnotationService
{
    /// <summary>All annotations on the document that the current member may see (including ones they ignored — see <see cref="CmsAnnotationView.HiddenByMe"/>).</summary>
    Task<IReadOnlyList<CmsAnnotationView>> ListAsync(Guid documentId, CancellationToken ct = default);

    /// <param name="audience">"private", "selected" or "all".</param>
    /// <param name="recipientDeviceIds">Devices of the chosen members; only used for "selected".</param>
    Task<CmsAnnotationView> CreateAsync(Guid documentId, CmsAnchor anchor, string text, string audience, IReadOnlyList<Guid> recipientDeviceIds, CancellationToken ct = default);

    Task<CmsAnnotationView> ReplyAsync(Guid annotationId, string text, CancellationToken ct = default);

    /// <summary>The author proposes the annotation for incorporation into the document (moderators then see it).</summary>
    Task<CmsAnnotationView> ProposeAsync(Guid annotationId, string? note, CancellationToken ct = default);

    /// <summary>A moderator's decision on a proposal: "incorporated", "declined" or "kept" (stays as a comment for everyone).</summary>
    Task<CmsAnnotationView> ModerateAsync(Guid annotationId, string decision, string? note, CancellationToken ct = default);

    /// <summary>Deletes the annotation (its author or Admin).</summary>
    Task DeleteAsync(Guid annotationId, CancellationToken ct = default);

    /// <summary>Per-member "ignore": hides the annotation for this member only.</summary>
    Task SetHiddenAsync(Guid annotationId, bool hidden, CancellationToken ct = default);
}

public sealed record CmsAnnotationReplyView(Guid Id, string AuthorName, string Text, DateTimeOffset CreatedAtUtc);

/// <summary><see cref="Anchor"/> is null and <see cref="Text"/> a placeholder when the body could not be decrypted (missing shared key).</summary>
public sealed record CmsAnnotationView(
    Guid Id, Guid DocumentId, string AuthorName, string Audience, CmsAnchor? Anchor, string Text,
    DateTimeOffset CreatedAtUtc, bool HiddenByMe, bool IsMine, IReadOnlyList<CmsAnnotationReplyView> Replies,
    string Moderation = "none", bool CanModerate = false);
