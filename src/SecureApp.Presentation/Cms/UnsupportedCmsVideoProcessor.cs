namespace SecureApp.Presentation.Cms;

/// <summary>
/// Non-Windows placeholder for <see cref="ICmsVideoProcessor"/> (2026-10-19, same reasoning as
/// <c>UnsupportedLibraryTranslationService</c>): the CMS editor that calls this is already PC-only
/// (per CMS_PLAN.md, phase 2a), but the interface is a required constructor dependency wherever the
/// editor page is registered, which happens on every platform. Reaching this would itself be a bug in
/// that PC-only gating, not a real runtime path, so throwing is intentional and safe.
/// </summary>
public sealed class UnsupportedCmsVideoProcessor : ICmsVideoProcessor
{
    public Task<CmsProcessedVideo> ProcessAsync(string sourcePath, IProgress<string>? progress = null, CancellationToken ct = default) =>
        throw new PlatformNotSupportedException("Zpracování videa je dostupné jen na Windows.");
}
