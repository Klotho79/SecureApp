using SecureApp.Data.Import;
using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.Library;

/// <summary>
/// The one wording for "what happens when I tap this library file", shared by Knihovna and Dokumenty (2026-11-18,
/// user: "ponechat možnost otevření ze serveru, ale s jasným označením, že se při otevření bude stahovat… a ne
/// všechny soubory by se daly otevřít, třeba zapakované soubory").
/// </summary>
public static class LibraryFileAvailability
{
    /// <summary>False for what the viewer can only hand to another app (zip, docx, pptx… — <see cref="DocumentType.Other"/>).</summary>
    public static bool CanOpenInApp(string fileName) => DocumentImportService.ClassifyByExtension(fileName) != DocumentType.Other;

    public static string Hint(string fileName, bool onDevice) => (onDevice, CanOpenInApp(fileName)) switch
    {
        (true, true) => "✓ v zařízení",
        (true, false) => "✓ v zařízení · nelze otevřít v appce, jen v jiné aplikaci",
        (false, true) => "☁ na serveru · stáhne se při otevření",
        (false, false) => "☁ na serveru · jen ke stažení, nelze otevřít v appce",
    };

    /// <summary>
    /// Before 🗑 (2026-11-18 — Admin and Modifier now delete anyone's material, so it is asked every time). A teaching
    /// document from Redakce is withdrawn and goes back to its author; any other file is gone for good.
    /// </summary>
    public static Task<bool> ConfirmDeleteAsync(Page page, string fileName) =>
        fileName.EndsWith(".cmsdoc", StringComparison.OrdinalIgnoreCase)
            ? page.DisplayAlertAsync("Smazat z knihovny",
                $"Stáhnout „{Path.GetFileNameWithoutExtension(fileName)}“ z knihovny? Zmizí všem, i ze zařízení. Dokument se vrátí autorovi jako zamítnutý a jeho verze zůstanou, takže ho může opravit a poslat znovu.",
                "Smazat", "Zrušit")
            : page.DisplayAlertAsync("Smazat z knihovny",
                $"Smazat „{fileName}“ z knihovny? Zmizí všem, i ze zařízení, a nejde to vrátit.",
                "Smazat", "Zrušit");

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} kB",
        _ => $"{bytes / (1024d * 1024):0.#} MB"
    };
}
