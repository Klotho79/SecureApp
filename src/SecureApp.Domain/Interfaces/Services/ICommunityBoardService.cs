using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// The community notice board (2026-09-24, user's own ask: Admin/Modifier post messages that
/// appear on everyone's Nástěnka). Device-authenticated. Posts are encrypted with the shared
/// community library key before leaving the device, so the relay only ever stores ciphertext —
/// same treatment as a shared-library file. Who may POST is enforced both here (the relay checks
/// the caller's assigned role) and in the UI; anyone may read.
/// </summary>
public interface ICommunityBoardService
{
    /// <summary>Posts a message to the board. Throws if the caller's relay role isn't Admin/Modifier, or the shared key / relay config is missing.</summary>
    Task PostAsync(string text, CancellationToken ct = default);

    /// <summary>The most recent posts, newest first, decrypted. Best-effort — returns empty on failure rather than throwing.</summary>
    Task<IReadOnlyList<BoardPost>> ListAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>Deletes a post. The relay allows it only for the post's author or an admin-secret holder.</summary>
    Task<bool> DeleteAsync(Guid postId, CancellationToken ct = default);
}
