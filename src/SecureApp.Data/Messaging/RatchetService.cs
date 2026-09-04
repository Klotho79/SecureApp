using SecureApp.Domain.Entities;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Data.Messaging;

/// <inheritdoc cref="IRatchetService"/>
/// <remarks>
/// <para>
/// This repo's own adaptation of the Signal Double Ratchet, not a claim of spec compliance.
/// Classic Double Ratchet's asymmetric "DH ratchet" relies on Diffie-Hellman being a *symmetric*
/// function — either side, holding their own private key and the other's public key, computes
/// the same shared value, which is what lets either party unilaterally rotate to a fresh keypair
/// at any time. ML-KEM is not symmetric that way: only whoever holds the recipient's public key
/// can encapsulate, and only the recipient can decapsulate — there is no single-sided "generate a
/// fresh keypair and ratchet" operation without an extra round trip to carry a reply ciphertext
/// back. Building a fully bidirectional asymmetric ratchet on top of that needs real protocol
/// design (extra header fields, careful epoch bookkeeping) that is out of scope for this pass.
/// </para>
/// <para>
/// What <b>is</b> implemented, and is a real, correct forward-secrecy mechanism: the
/// <b>symmetric-key ratchet</b>. <see cref="InitiateHandshakeAsync"/>/<see cref="CompleteHandshakeAsync"/>
/// perform one ML-KEM encapsulation (an X3DH-style initial agreement) to establish a shared root
/// key, from which two independently-keyed HKDF chains are derived (one per direction). Every
/// <see cref="RatchetEncryptAsync"/>/<see cref="RatchetDecryptAsync"/> call steps its chain
/// forward via HKDF (message key = KDF(chain_key, "MessageKey"), chain_key' =
/// KDF(chain_key, "ChainStep")) and the spent chain key is overwritten in the vault — a
/// compromise of a later chain key can never recover an earlier message's key. What is
/// deliberately <b>not</b> implemented is post-compromise security via re-keying
/// (<see cref="RatchetSessionState.RatchetDh"/> exists on the entity as scaffolding for a future
/// pass, but nothing here calls it) — see DEVELOPMENT_PLAN.md's Milestone 5 note.
/// </para>
/// </remarks>
public sealed class RatchetService : IRatchetService
{
    private const int DerivedKeyLengthBytes = 32;
    private const int MaxSkippedMessages = 1000;

    private const string RootKeyInfo = "SecureApp.Chat.RootKey.v1";
    private const string InitiatorToResponderInfo = "SecureApp.Chat.InitiatorToResponder.v1";
    private const string ResponderToInitiatorInfo = "SecureApp.Chat.ResponderToInitiator.v1";
    private const string MessageKeyInfo = "SecureApp.Chat.MessageKey.v1";
    private const string ChainStepInfo = "SecureApp.Chat.ChainStep.v1";

    private readonly ICryptoService _crypto;
    private readonly ISecureVaultKeyStore _vault;
    private readonly IChatSessionRepository _sessionRepository;
    private readonly IRatchetSessionStateRepository _stateRepository;

    public RatchetService(
        ICryptoService crypto,
        ISecureVaultKeyStore vault,
        IChatSessionRepository sessionRepository,
        IRatchetSessionStateRepository stateRepository)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _stateRepository = stateRepository ?? throw new ArgumentNullException(nameof(stateRepository));
    }

    public async Task<byte[]> InitiateHandshakeAsync(Guid sessionId, byte[] peerIdentityPublicKey, CancellationToken ct = default)
    {
        var (kemCipherText, sharedSecret) = await _crypto.EncapsulateAsync(peerIdentityPublicKey, ct);
        await EstablishChainsAsync(sessionId, sharedSecret, isInitiator: true, ct);
        return kemCipherText;
    }

    public async Task CompleteHandshakeAsync(Guid sessionId, byte[] kemCipherText, byte[] peerIdentityPublicKey, CancellationToken ct = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, ct) ?? throw new ChatSessionNotFoundException(sessionId);
        var sharedSecret = await _crypto.DecapsulateAsync(session.LocalIdentityKeyId, kemCipherText, ct);
        await EstablishChainsAsync(sessionId, sharedSecret, isInitiator: false, ct);
    }

    private async Task EstablishChainsAsync(Guid sessionId, byte[] sharedSecret, bool isInitiator, CancellationToken ct)
    {
        var rootKey = await _crypto.DeriveKeyAsync(sharedSecret, salt: null, RootKeyInfo, DerivedKeyLengthBytes, ct);
        var initiatorToResponder = await _crypto.DeriveKeyAsync(rootKey, salt: null, InitiatorToResponderInfo, DerivedKeyLengthBytes, ct);
        var responderToInitiator = await _crypto.DeriveKeyAsync(rootKey, salt: null, ResponderToInitiatorInfo, DerivedKeyLengthBytes, ct);

        var sendChainKey = isInitiator ? initiatorToResponder : responderToInitiator;
        var recvChainKey = isInitiator ? responderToInitiator : initiatorToResponder;

        await _vault.StoreSecretAsync(VaultKey(sessionId, "root-key"), rootKey, ct);
        await _vault.StoreSecretAsync(VaultKey(sessionId, "send-chain-key"), sendChainKey, ct);
        await _vault.StoreSecretAsync(VaultKey(sessionId, "recv-chain-key"), recvChainKey, ct);
    }

    public async Task<(RatchetMessageHeader Header, EncryptedPayload Payload)> RatchetEncryptAsync(Guid sessionId, ReadOnlyMemory<byte> plaintext, CancellationToken ct = default)
    {
        var state = await _stateRepository.GetBySessionIdAsync(sessionId, ct)
            ?? throw new RatchetStateException($"No ratchet state for session '{sessionId}' — complete the handshake first.");
        var sendChainKey = await _vault.RetrieveSecretAsync(VaultKey(sessionId, "send-chain-key"), ct)
            ?? throw new RatchetStateException("Ratchet send chain key is unavailable.");

        var messageKey = await _crypto.DeriveKeyAsync(sendChainKey, salt: null, MessageKeyInfo, DerivedKeyLengthBytes, ct);
        var nextChainKey = await _crypto.DeriveKeyAsync(sendChainKey, salt: null, ChainStepInfo, DerivedKeyLengthBytes, ct);

        var payload = await _crypto.EncryptWithKeyAsync(plaintext, sessionId, messageKey, ct);
        var header = new RatchetMessageHeader(state.CurrentSendChainPublicKey, state.PreviousSendChainLength, state.SendMessageNumber);

        // Only commit the advanced chain key once encryption has actually produced a payload.
        await _vault.StoreSecretAsync(VaultKey(sessionId, "send-chain-key"), nextChainKey, ct);
        state.AdvanceSend();
        await _stateRepository.UpsertAsync(state, ct);

        return (header, payload);
    }

    public async Task<byte[]> RatchetDecryptAsync(Guid sessionId, RatchetMessageHeader header, EncryptedPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(payload);

        var state = await _stateRepository.GetBySessionIdAsync(sessionId, ct)
            ?? throw new RatchetStateException($"No ratchet state for session '{sessionId}' — complete the handshake first.");

        if (header.MessageNumber < state.ReceiveMessageNumber)
            return await DecryptSkippedAsync(sessionId, header.MessageNumber, payload, ct);

        if (header.MessageNumber - state.ReceiveMessageNumber > MaxSkippedMessages)
            throw new RatchetStateException("Too many skipped messages — refusing to derive that many chain steps.");

        var recvChainKey = await _vault.RetrieveSecretAsync(VaultKey(sessionId, "recv-chain-key"), ct)
            ?? throw new RatchetStateException("Ratchet receive chain key is unavailable.");

        // Walk the chain forward to the incoming message's position, computing (but not yet
        // persisting — see below) every intermediate message key we pass over, for out-of-order
        // delivery: an earlier message may still arrive later.
        var skippedKeys = new List<(int MessageNumber, byte[] Key)>();
        var walkedReceiveNumber = state.ReceiveMessageNumber;
        while (walkedReceiveNumber < header.MessageNumber)
        {
            var skippedMessageKey = await _crypto.DeriveKeyAsync(recvChainKey, salt: null, MessageKeyInfo, DerivedKeyLengthBytes, ct);
            skippedKeys.Add((walkedReceiveNumber, skippedMessageKey));
            recvChainKey = await _crypto.DeriveKeyAsync(recvChainKey, salt: null, ChainStepInfo, DerivedKeyLengthBytes, ct);
            walkedReceiveNumber++;
        }

        var messageKey = await _crypto.DeriveKeyAsync(recvChainKey, salt: null, MessageKeyInfo, DerivedKeyLengthBytes, ct);
        var nextChainKey = await _crypto.DeriveKeyAsync(recvChainKey, salt: null, ChainStepInfo, DerivedKeyLengthBytes, ct);

        byte[] plaintext;
        try
        {
            plaintext = await _crypto.DecryptWithKeyAsync(payload, messageKey, ct);
        }
        catch (EncryptionOperationException ex)
        {
            // Deliberately never commits any of the walked-forward state below: a single
            // corrupt/tampered message must not desynchronize this chain from the sender's —
            // that would turn one bad message into a permanent DoS on the whole session.
            throw new RatchetStateException("Message decryption failed.", ex);
        }

        foreach (var (messageNumber, key) in skippedKeys)
        {
            await _vault.StoreSecretAsync(SkippedVaultKey(sessionId, messageNumber), key, ct);
            state.AdvanceReceive();
        }
        await _vault.StoreSecretAsync(VaultKey(sessionId, "recv-chain-key"), nextChainKey, ct);
        state.AdvanceReceive();
        await _stateRepository.UpsertAsync(state, ct);
        return plaintext;
    }

    private async Task<byte[]> DecryptSkippedAsync(Guid sessionId, int messageNumber, EncryptedPayload payload, CancellationToken ct)
    {
        var skippedKey = await _vault.RetrieveSecretAsync(SkippedVaultKey(sessionId, messageNumber), ct)
            ?? throw new RatchetStateException("Message key unavailable (already used, or never skipped).");

        try
        {
            var plaintext = await _crypto.DecryptWithKeyAsync(payload, skippedKey, ct);
            await _vault.RemoveSecretAsync(SkippedVaultKey(sessionId, messageNumber), ct);
            return plaintext;
        }
        catch (EncryptionOperationException ex)
        {
            throw new RatchetStateException("Message decryption failed.", ex);
        }
    }

    /// <summary>Namespaces a ratchet secret under its session's id — deliberately separate from <c>BouncyCastleCryptoService</c>'s own "cryptokey:" namespace, since these are session-scoped, not key-pair-scoped.</summary>
    private static string VaultKey(Guid sessionId, string label) => $"ratchet:{sessionId:N}:{label}";

    private static string SkippedVaultKey(Guid sessionId, int messageNumber) => $"ratchet:{sessionId:N}:skipped:{messageNumber}";
}
