namespace SecureApp.Presentation.Transport;

/// <summary>
/// Wire layout for data encrypted under the community's shared library key (library files, the notice
/// board, the PICC roster): <c>Nonce (12) + AuthTag (16) + CipherText</c> concatenated into one opaque
/// blob the relay only stores/serves verbatim. The fixed GCM nonce/tag sizes match
/// <c>BouncyCastleCryptoService</c>'s own constants. One shared copy instead of one per service.
/// </summary>
internal static class SharedKeyWire
{
    public const int NonceLength = 12;
    public const int AuthTagLength = 16;

    public static byte[] Combine(byte[] nonce, byte[] authTag, byte[] cipherText)
    {
        var result = new byte[nonce.Length + authTag.Length + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(authTag, 0, result, nonce.Length, authTag.Length);
        Buffer.BlockCopy(cipherText, 0, result, nonce.Length + authTag.Length, cipherText.Length);
        return result;
    }

    public static (byte[] Nonce, byte[] AuthTag, byte[] CipherText) Split(byte[] wireBytes, string tooShortMessage)
    {
        if (wireBytes.Length < NonceLength + AuthTagLength)
            throw new InvalidOperationException(tooShortMessage);

        var nonce = wireBytes[..NonceLength];
        var authTag = wireBytes[NonceLength..(NonceLength + AuthTagLength)];
        var cipherText = wireBytes[(NonceLength + AuthTagLength)..];
        return (nonce, authTag, cipherText);
    }
}
