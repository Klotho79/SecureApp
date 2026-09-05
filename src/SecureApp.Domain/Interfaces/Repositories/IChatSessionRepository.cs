using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IChatSessionRepository
{
    Task<ChatSession?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ChatSession>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds an existing non-Closed session with this exact peer identity, if any — used to stop
    /// "New Chat" from unconditionally minting a fresh session (and fresh invite) every time the
    /// same peer's contact card is pasted/scanned again, which otherwise silently accumulates
    /// duplicate sessions with the same peer display name and no way to tell them apart. Closed
    /// sessions are excluded so a deliberately-ended chat can always be re-paired from scratch.
    /// </summary>
    Task<ChatSession?> GetByPeerPublicKeyAsync(byte[] peerIdentityPublicKey, CancellationToken ct = default);
    Task AddAsync(ChatSession chatSession, CancellationToken ct = default);
    Task UpdateAsync(ChatSession chatSession, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
