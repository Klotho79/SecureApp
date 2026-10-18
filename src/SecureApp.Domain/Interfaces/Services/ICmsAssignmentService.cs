using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Per-document CMS editor grants (CMS_PLAN.md, phase 2c). Admin or Modifier assign a document to an
/// editor, who may then stage and submit just that document; the relay ends the grant automatically
/// when the document is approved. Device-authenticated; the relay enforces who may assign.
/// </summary>
public interface ICmsAssignmentService
{
    /// <summary>Assigns <paramref name="editorDeviceId"/> to the document; a null <paramref name="documentId"/> starts a new document.</summary>
    Task<CmsAssignment> AssignAsync(Guid? documentId, Guid editorDeviceId, string title, string folderPath, CancellationToken ct = default);

    Task RevokeAsync(Guid documentId, Guid editorDeviceId, CancellationToken ct = default);

    /// <summary>Members that can be assigned, one per e-mail identity (Admin/Modifier only).</summary>
    Task<IReadOnlyList<CmsMember>> ListMembersAsync(CancellationToken ct = default);

    /// <summary>Every active grant (Admin/Modifier overview).</summary>
    Task<IReadOnlyList<CmsAssignment>> ListAllAsync(CancellationToken ct = default);

    /// <summary>This device's own active grants — "Moje zadání".</summary>
    Task<IReadOnlyList<CmsAssignment>> ListMineAsync(CancellationToken ct = default);
}
