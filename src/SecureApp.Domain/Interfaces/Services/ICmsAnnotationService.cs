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
    /// <param name="incorporatedInVersionId">Only meaningful for "incorporated" — the CMS document VersionId the moderator just saved after working the annotation's text into it.</param>
    Task<CmsAnnotationView> ModerateAsync(Guid annotationId, string decision, string? note, string? incorporatedInVersionId = null, CancellationToken ct = default);

    /// <summary>Deletes the annotation (its author or Admin).</summary>
    Task DeleteAsync(Guid annotationId, CancellationToken ct = default);

    /// <summary>Per-member "ignore": hides the annotation for this member only.</summary>
    Task SetHiddenAsync(Guid annotationId, bool hidden, CancellationToken ct = default);

    /// <summary>The shared moderation queue (CMS_PLAN.md, phase 5) — all pending proposals across every document. Admin/Modifier only; empty for anyone else.</summary>
    Task<IReadOnlyList<CmsPendingAnnotationView>> ListPendingAsync(CancellationToken ct = default);

    /// <summary>The caller's own annotations that have been proposed or decided — lets this device notice a moderation decision happened (no cross-device push exists).</summary>
    Task<IReadOnlyList<CmsMyAnnotationView>> ListMyModeratedAsync(CancellationToken ct = default);
}

public sealed record CmsAnnotationReplyView(Guid Id, string AuthorName, string Text, DateTimeOffset CreatedAtUtc);

/// <summary><see cref="Anchor"/> is null and <see cref="Text"/> a placeholder when the body could not be decrypted (missing shared key).</summary>
public sealed record CmsAnnotationView(
    Guid Id, Guid DocumentId, string AuthorName, string Audience, CmsAnchor? Anchor, string Text,
    DateTimeOffset CreatedAtUtc, bool HiddenByMe, bool IsMine, IReadOnlyList<CmsAnnotationReplyView> Replies,
    string Moderation = "none", bool CanModerate = false, string? IncorporatedInVersionId = null);

/// <summary>One pending proposal in the shared moderation queue; <see cref="Text"/>/<see cref="Anchor"/> are null when the body could not be decrypted.</summary>
public sealed record CmsPendingAnnotationView(Guid AnnotationId, Guid DocumentId, string DocumentTitle, string AuthorName, CmsAnchor? Anchor, string Text, DateTimeOffset CreatedAtUtc);

public sealed record CmsMyAnnotationView(Guid AnnotationId, string DocumentTitle, string Moderation, DateTimeOffset UpdatedAtUtc);
