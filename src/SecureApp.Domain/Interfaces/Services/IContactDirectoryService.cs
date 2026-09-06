using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Client for the relay's member directory (2026-09-06) — replaces manually generating/copying/
/// scanning a contact card to start a chat with someone already on this relay. Deliberately built
/// on top of, not instead of, the activation flow (<c>IMessageTransport.RequestActivationAsync</c>):
/// the trust-model shift this represents — "any admin-approved device may see any other's public
/// identity key" rather than "I personally verified this exact device's card" — only became a
/// reasonable default once every device's real identity was already vetted once at activation
/// time. <c>NewChatViewModel</c>'s manual paste/QR flow stays as a fallback for a peer who somehow
/// isn't in the directory yet (hasn't connected since this feature shipped, or a genuinely offline
/// hand-off is preferred) — this doesn't replace that path, it just makes it unnecessary for the
/// common case.
/// </summary>
public interface IContactDirectoryService
{
    /// <summary>
    /// Publishes (upserts) this device's own display name + chat-identity public key to the relay.
    /// Cheap and idempotent by design — intended to be called on every successful
    /// <c>IMessageTransport.ConnectAsync</c>, not just once, so a later display-name change or key
    /// rotation is picked up automatically without a dedicated "re-publish" action anywhere.
    /// </summary>
    Task PublishSelfAsync(CancellationToken ct = default);

    /// <summary>Every other community member currently published — used by "New Chat" to list people who can be messaged with a single tap, no contact card required.</summary>
    Task<IReadOnlyList<DirectoryMember>> ListMembersAsync(CancellationToken ct = default);
}
