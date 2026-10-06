using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Translation;

/// <summary>
/// Non-Windows placeholder for <see cref="ILibraryTranslationService"/> (2026-10-06, real crash
/// fix — see <see cref="LocalAiLibraryTranslationService"/>'s own remarks for the full story).
/// The local-AI translation feature is Windows-only (every entry point already gates on
/// <c>DeviceInfo.Current.Platform == WinUI</c>), but <see cref="ILibraryTranslationService"/> is a
/// required constructor dependency of several ViewModels on every platform. Registered here only so
/// DI resolves cleanly on Android/iOS/MacCatalyst, which must never reference QuestPDF/PdfPig at all
/// — those packages' native libraries are what crashed the app at startup on Android before this
/// fix. Neither method here should ever actually run; reaching one would itself be a bug in the
/// Windows-only gating elsewhere, not a real runtime path, so throwing is intentional and safe.
/// </summary>
public sealed class UnsupportedLibraryTranslationService : ILibraryTranslationService
{
    public Task<LibraryDocumentSummary> TranslateAndSubmitAsync(
        Guid sourceLibraryFileId,
        string sourceTitle,
        string sourceFolderPath,
        string targetLanguage,
        IProgress<double>? onProgress = null,
        IProgress<string>? onStatusText = null,
        CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("Místní AI překlad je dostupný jen na Windows.");

    public Task PingAsync(CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("Místní AI překlad je dostupný jen na Windows.");
}
