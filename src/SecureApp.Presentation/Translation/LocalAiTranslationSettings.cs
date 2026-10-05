namespace SecureApp.Presentation.Translation;

/// <summary>
/// Preferences-backed config for the admin-only local-AI PDF translation feature (2026-10-05) —
/// not a secret, so plain <c>Preferences</c>, not <see cref="SecureApp.Domain.Interfaces.Services.ISecureVaultKeyStore"/>.
/// Shared between the Settings card (reads/writes) and <see cref="LocalAiLibraryTranslationService"/>
/// (reads only), so the two preference-key strings are defined exactly once.
/// </summary>
public static class LocalAiTranslationSettings
{
    private const string BaseUrlKey = "translation.localai.baseurl";
    private const string ModelNameKey = "translation.localai.model";

    /// <summary>Ollama's own default port; LM Studio users repoint this to their own port instead.</summary>
    public const string DefaultBaseUrl = "http://localhost:11434";

    public static string GetBaseUrl() => Microsoft.Maui.Storage.Preferences.Default.Get(BaseUrlKey, DefaultBaseUrl);

    public static void SetBaseUrl(string value) => Microsoft.Maui.Storage.Preferences.Default.Set(BaseUrlKey, value);

    public static string GetModelName() => Microsoft.Maui.Storage.Preferences.Default.Get(ModelNameKey, string.Empty);

    public static void SetModelName(string value) => Microsoft.Maui.Storage.Preferences.Default.Set(ModelNameKey, value);
}
