using SecureApp.Domain.Common;

namespace SecureApp.Domain.Entities;

/// <summary>
/// Non-secret Double Ratchet bookkeeping for one <see cref="ChatSession"/> — message counters and
/// DH public keys only. <see cref="Entity.Id"/> is always set equal to the owning session's id
/// (1:1, same correlation trick <c>EncryptionKeyMetadata.Id = keyPair.KeyId</c> uses in
/// <c>DocumentImportService</c>). The actual secret root/chain keys never become properties here —
/// they live only in <c>ISecureVaultKeyStore</c>, namespaced by this session's id.
/// </summary>
public sealed class RatchetSessionState : Entity
{
    public int SendMessageNumber { get; private set; }
    public int ReceiveMessageNumber { get; private set; }
    public int PreviousSendChainLength { get; private set; }
    public byte[] CurrentSendChainPublicKey { get; private set; }
    public byte[]? RemoteRatchetPublicKey { get; private set; }

    private RatchetSessionState()
    {
        // Reserved for materialization by persistence/serialization infrastructure.
        CurrentSendChainPublicKey = Array.Empty<byte>();
    }

    public RatchetSessionState(byte[] currentSendChainPublicKey)
    {
        if (currentSendChainPublicKey is null || currentSendChainPublicKey.Length == 0)
            throw new ArgumentException("Current send chain public key cannot be empty.", nameof(currentSendChainPublicKey));

        CurrentSendChainPublicKey = currentSendChainPublicKey;
    }

    public void AdvanceSend()
    {
        SendMessageNumber++;
        Touch();
    }

    public void AdvanceReceive()
    {
        ReceiveMessageNumber++;
        Touch();
    }

    /// <summary>Records a DH ratchet step: a new remote public key was seen, so a new sending chain begins.</summary>
    public void RatchetDh(byte[] newRemotePublicKey, byte[] newLocalPublicKey)
    {
        if (newRemotePublicKey is null || newRemotePublicKey.Length == 0)
            throw new ArgumentException("New remote public key cannot be empty.", nameof(newRemotePublicKey));
        if (newLocalPublicKey is null || newLocalPublicKey.Length == 0)
            throw new ArgumentException("New local public key cannot be empty.", nameof(newLocalPublicKey));

        PreviousSendChainLength = SendMessageNumber;
        SendMessageNumber = 0;
        ReceiveMessageNumber = 0;
        RemoteRatchetPublicKey = newRemotePublicKey;
        CurrentSendChainPublicKey = newLocalPublicKey;
        Touch();
    }
}
