using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SecureApp.Presentation.Transport;

/// <summary>
/// Trust for the relay's own HTTPS/WSS endpoint (2026-11-03). The relay presents a self-signed certificate for its
/// static public IP — deliberately no domain, so nothing about it appears in Certificate Transparency logs — and the
/// app accepts exactly that certificate's public key instead of relying on a public CA, the same idea as the pinned
/// WireGuard installer hash. A certificate the system already trusts is accepted as usual; anything else is accepted
/// only when its public key hash equals <see cref="PinnedPublicKeySha256Base64"/>, so a wrong host name on the
/// certificate (the relay is reached by LAN address too) is not an error but a different key always is.
/// </summary>
internal static class RelayTls
{
    /// <summary>Base64 SHA-256 of the relay certificate's SubjectPublicKeyInfo (valid until 2036; the key is kept if the certificate is renewed).</summary>
    public const string PinnedPublicKeySha256Base64 = "BY8RYKuMt518JgZmGPWa8bOiFg81X7ThtZWwq9oP5kU=";

    /// <summary>
    /// TLS 1.2 only on Windows (2026-11-07): a work PC could complete a TLS 1.2 handshake with the relay but not TLS 1.3
    /// ("no common algorithm" — 1.3 unavailable or disabled by policy on that machine), while the app's default negotiation
    /// failed with "The SSL connection could not be established". The relay accepts 1.2, and the key is pinned either way.
    /// </summary>
    public const System.Security.Authentication.SslProtocols WindowsProtocols = System.Security.Authentication.SslProtocols.Tls12;

    private static readonly Lazy<HttpMessageInvoker> WindowsWebSocketInvoker = new(() =>
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.EnabledSslProtocols = WindowsProtocols;
        handler.SslOptions.RemoteCertificateValidationCallback = ValidateWebSocket;
        return new HttpMessageInvoker(handler);
    });

    /// <summary>The invoker a WebSocket connects through on Windows (pins TLS 1.2 and the relay certificate); null elsewhere, where the socket's own options do that.</summary>
    public static HttpMessageInvoker? WebSocketInvoker => OperatingSystem.IsWindows() ? WindowsWebSocketInvoker.Value : null;

    public static bool Validate(HttpRequestMessage? request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
        => errors == SslPolicyErrors.None || IsPinned(certificate);

    public static bool ValidateWebSocket(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        => errors == SslPolicyErrors.None || IsPinned(certificate is null ? null : new X509Certificate2(certificate));

    private static bool IsPinned(X509Certificate2? certificate)
    {
        if (certificate is null)
            return false;

        var hash = Convert.ToBase64String(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(hash),
            System.Text.Encoding.ASCII.GetBytes(PinnedPublicKeySha256Base64));
    }
}
