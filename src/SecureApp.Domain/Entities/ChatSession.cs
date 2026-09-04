using SecureApp.Domain.Common;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Entities;

/// <summary>
/// A 1-on-1 (Direct) end-to-end encrypted chat session with a remote peer. Group sessions are not
/// modeled yet — group E2EE needs a different protocol (Sender Keys/MLS, not plain Double Ratchet)
/// and is left for a later milestone, see DEVELOPMENT_PLAN.md's Milestone 5 note.
/// </summary>
public sealed class ChatSession : Entity
{
    public string PeerDisplayName { get; private set; }
    public byte[] PeerIdentityPublicKey { get; private set; }
    public Guid LocalIdentityKeyId { get; private set; }
    public ChatSessionState State { get; private set; }
    public DateTimeOffset? LastRatchetedAtUtc { get; private set; }

    /// <summary>
    /// The peer's relay-assigned routing identity (see <c>IMessageTransport</c>'s remarks) — not a
    /// cryptographic identity, just an address <c>IMessageTransport</c> uses to route an envelope
    /// to the right connected device. Exchanged out-of-band alongside <see cref="PeerIdentityPublicKey"/>.
    /// </summary>
    public Guid? PeerRelayDeviceId { get; private set; }

    private ChatSession()
    {
        // Reserved for materialization by persistence/serialization infrastructure.
        PeerDisplayName = string.Empty;
        PeerIdentityPublicKey = Array.Empty<byte>();
    }

    public ChatSession(string peerDisplayName, byte[] peerIdentityPublicKey, Guid localIdentityKeyId)
    {
        if (string.IsNullOrWhiteSpace(peerDisplayName))
            throw new ArgumentException("Peer display name cannot be empty.", nameof(peerDisplayName));
        if (peerIdentityPublicKey is null || peerIdentityPublicKey.Length == 0)
            throw new ArgumentException("Peer identity public key cannot be empty.", nameof(peerIdentityPublicKey));
        if (localIdentityKeyId == Guid.Empty)
            throw new ArgumentException("Local identity key id cannot be empty.", nameof(localIdentityKeyId));

        PeerDisplayName = peerDisplayName;
        PeerIdentityPublicKey = peerIdentityPublicKey;
        LocalIdentityKeyId = localIdentityKeyId;
        State = ChatSessionState.PendingHandshake;
    }

    public void Activate()
    {
        State = ChatSessionState.Active;
        LastRatchetedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Close()
    {
        State = ChatSessionState.Closed;
        Touch();
    }

    public void NoteRatcheted()
    {
        LastRatchetedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void LinkRelayDevice(Guid relayDeviceId)
    {
        if (relayDeviceId == Guid.Empty)
            throw new ArgumentException("Relay device id cannot be empty.", nameof(relayDeviceId));

        PeerRelayDeviceId = relayDeviceId;
        Touch();
    }
}
