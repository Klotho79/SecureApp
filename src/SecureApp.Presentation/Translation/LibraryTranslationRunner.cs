using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Translation;

/// <summary>The page state a library translation reports into; both library view models already have these properties.</summary>
public interface ILibraryTranslationHost
{
    bool IsTranslating { get; set; }
    double TranslationProgress { get; set; }
    string? TranslationStatusText { get; set; }
    string? StatusErrorMessage { get; set; }
}

/// <summary>
/// The "Přeložit" action shared by the library browse page and a sub-category page (2026-11-14 — it used to be copied in
/// both view models). Admin-only, Windows-only local-AI PDF translation; see <see cref="ILibraryTranslationService"/>.
/// </summary>
public static class LibraryTranslationRunner
{
    /// <summary>Asks for the target language, then translates <paramref name="item"/> and submits the result for review. True when a draft was submitted.</summary>
    public static async Task<bool> RunAsync(ILibraryTranslationHost host, ILibraryTranslationService service, LibraryFileItem? item, string folderPath)
    {
        if (item is null || !item.CanTranslate || host.IsTranslating) return false;

        var targetLanguage = await (Shell.Current?.CurrentPage?.DisplayPromptAsync(
            "Přeložit dokument", "Cílový jazyk:", initialValue: "čeština") ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(targetLanguage)) return false;

        host.IsTranslating = true;
        host.TranslationProgress = 0;
        host.TranslationStatusText = "Spouštím překlad…";
        host.StatusErrorMessage = null;
        try
        {
            var progress = new Progress<double>(p => host.TranslationProgress = p);
            var status = new Progress<string>(s => host.TranslationStatusText = s);
            var created = await service.TranslateAndSubmitAsync(
                item.Id, Path.GetFileNameWithoutExtension(item.FileName), folderPath, targetLanguage.Trim(), progress, status);
            host.TranslationStatusText = $"Hotovo — koncept '{created.Title}' odeslán ke schválení.";
            return true;
        }
        catch (Exception ex)
        {
            host.StatusErrorMessage = $"Překlad '{item.FileName}' se nezdařil: {ex.Message}";
            host.TranslationStatusText = null;
            return false;
        }
        finally
        {
            host.IsTranslating = false;
        }
    }
}
