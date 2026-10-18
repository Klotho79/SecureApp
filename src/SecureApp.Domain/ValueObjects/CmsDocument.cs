using System.Text.Json.Serialization;

namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One immutable version of a CMS document (see CMS_PLAN.md). Pure content tree — no pixels, no
/// annotations; annotations live in a separate layer (<see cref="CmsAnnotation"/>) anchored to
/// <see cref="CmsBlock.Id"/>, which must stay stable across edits.
/// </summary>
public sealed record CmsDocument(
    int Schema,
    Guid DocumentId,
    string VersionId,
    string? BaseVersionId,
    string Title,
    string Locale,
    IReadOnlyList<CmsBlock> Blocks,
    IReadOnlyDictionary<string, CmsMedia> Media);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CmsHeadingBlock), "heading")]
[JsonDerivedType(typeof(CmsParagraphBlock), "paragraph")]
[JsonDerivedType(typeof(CmsListBlock), "list")]
[JsonDerivedType(typeof(CmsImageBlock), "image")]
[JsonDerivedType(typeof(CmsVideoBlock), "video")]
[JsonDerivedType(typeof(CmsRowBlock), "row")]
public abstract record CmsBlock(string Id);

/// <summary>Text run; <see cref="Marks"/> are e.g. "bold", "italic", "underline".</summary>
public sealed record CmsTextRun(string T, IReadOnlyList<string>? Marks = null);

public sealed record CmsHeadingBlock(string Id, int Level, IReadOnlyList<CmsTextRun> Text) : CmsBlock(Id);

public sealed record CmsParagraphBlock(string Id, IReadOnlyList<CmsTextRun> Text) : CmsBlock(Id);

public sealed record CmsListBlock(string Id, bool Ordered, IReadOnlyList<IReadOnlyList<CmsTextRun>> Items) : CmsBlock(Id);

/// <summary>Layout is intent, never pixels: height always derives from <see cref="AspectRatio"/> (width / height).</summary>
public sealed record CmsImageBlock(
    string Id,
    string MediaId,
    double AspectRatio,
    string Fit = "contain",
    string Align = "center",
    int MaxWidthPct = 100,
    string? Alt = null,
    IReadOnlyList<CmsTextRun>? Caption = null) : CmsBlock(Id);

public sealed record CmsVideoBlock(
    string Id,
    string MediaId,
    double AspectRatio,
    string? PosterMediaId = null,
    int? DurationSec = null,
    string? CaptionsMediaId = null) : CmsBlock(Id);

/// <summary>12-column row. Children stack in order when the container is compact.</summary>
public sealed record CmsRowBlock(string Id, int Gap, IReadOnlyList<CmsRowCell> Children) : CmsBlock(Id);

public sealed record CmsRowCell(CmsSpan Span, IReadOnlyList<CmsBlock> Blocks);

/// <summary>Column span (1-12) per width class: compact &lt; 600, medium 600-840, expanded &gt; 840 (device-independent units).</summary>
public sealed record CmsSpan(int Compact = 12, int Medium = 12, int Expanded = 12);

public sealed record CmsMediaVariant(int W, string LibraryFileId);

/// <summary>Images carry several width variants; video a single transcoded file (<see cref="LibraryFileId"/>).</summary>
public sealed record CmsMedia(
    string Kind,
    string? Mime = null,
    string? LibraryFileId = null,
    string? Sha256 = null,
    IReadOnlyList<CmsMediaVariant>? Variants = null);

public enum CmsEditorGrantEndReason { Approved, Revoked, Reassigned }

/// <summary>
/// Per-document editing right (CMS_PLAN.md): active while <see cref="EndedAtUtc"/> is null, ends
/// automatically when the document is approved/published. Independent of the global Role.
/// </summary>
public sealed record CmsEditorGrant(
    Guid DocumentId,
    Guid UserId,
    Guid GrantedBy,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? EndedAtUtc = null,
    CmsEditorGrantEndReason? EndReason = null)
{
    public bool IsActive => EndedAtUtc is null;
}

/// <summary>Text anchor: block + W3C-style quote selector; offsets are only a hint, the quote is authoritative.</summary>
public sealed record CmsAnchor(
    string VersionId,
    string BlockId,
    string Exact,
    string Prefix,
    string Suffix,
    int Start,
    int End);

public enum CmsAnchorState { Ok, Shifted, Orphaned, NeedsReview }

public enum CmsAudience { All, Selected }

/// <summary>Admin always sees every annotation regardless of audience (user decision 2026-10-18).</summary>
public sealed record CmsVisibility(CmsAudience Audience, IReadOnlyList<Guid> UserIds);

public enum CmsModerationState { None, Pending, Approved, Rejected, Incorporated }

public sealed record CmsModeration(
    CmsModerationState State,
    Guid? ReviewedBy = null,
    DateTimeOffset? ReviewedAtUtc = null,
    string? Reason = null,
    string? IncorporatedInVersionId = null,
    string? IncorporatedAs = null);

public sealed record CmsAnnotationReply(Guid Id, Guid AuthorId, DateTimeOffset CreatedAtUtc, string Body);

/// <summary>
/// Materialized state of an annotation (folded from append-only events). Per-user "ignore" is NOT
/// stored here — see CMS_PLAN.md (annotation_hides, private to each user).
/// </summary>
public sealed record CmsAnnotation(
    Guid Id,
    Guid DocumentId,
    Guid AuthorId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EditedAtUtc,
    CmsAnchor Anchor,
    CmsAnchorState AnchorState,
    string Body,
    CmsVisibility Visibility,
    CmsModeration Moderation,
    IReadOnlyList<CmsAnnotationReply> Replies,
    bool Deleted);
