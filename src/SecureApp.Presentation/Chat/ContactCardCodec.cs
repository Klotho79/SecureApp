using System.Text.Json;

namespace SecureApp.Presentation.Chat;

/// <summary>Encodes/decodes <see cref="ContactCardBlob"/>/<see cref="ChatInviteBlob"/> as a single copy-paste-friendly base64 block — no special characters to worry about when pasted across different apps.</summary>
public static class ContactCardCodec
{
    public static string Encode<T>(T value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value));

    public static T Decode<T>(string text) => JsonSerializer.Deserialize<T>(Convert.FromBase64String(text.Trim()))
        ?? throw new InvalidOperationException("Empty or invalid blob.");
}
