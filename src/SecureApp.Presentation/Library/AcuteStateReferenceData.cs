namespace SecureApp.Presentation.Library;

/// <summary>
/// Built-in fallback recommendations for the "Akutní stavy" quick-access row (2026-10-03) — a plain
/// read-only lookup, same "static reference data, not stored/edited/synced" shape as
/// <see cref="Contacts.ContactDirectoryData"/>'s own phone directory. Used by
/// <c>LibraryViewModel.SearchAcuteStateAsync</c> ONLY as the second-priority fallback, after a real
/// uploaded-and-tagged library document (which always wins when one exists) and before the final
/// plain full-text search fallback.
///
/// Written from general anesthesiology/critical-care knowledge, NOT transcribed from any specific
/// society's copyrighted guideline document — every entry says so explicitly and ends with an
/// "OVĚŘIT" disclaimer. The user's own explicit choice (2026-10-03, after being offered "you upload
/// the real document" vs. "I write a summary you review" and initially picking the former, then
/// asking to just get it working without uploading anything) — same caveat already flagged on the
/// GCS calculator's severity bands: this is NOT validated against this specific hospital team's own
/// protocols, and must not be treated as the sole source for a real clinical decision.
/// </summary>
public static class AcuteStateReferenceData
{
    public static IReadOnlyDictionary<string, string> Recommendations { get; } = new Dictionary<string, string>
    {
        ["DAS algoritmy (dýchací cesty)"] =
            "Algoritmus obtížné/neúspěšné tracheální intubace u dospělého (elektivní výkon) — obecný rámec:\n\n" +
            "Plán A — standardní laryngoskopie a intubace. Optimalizace polohy hlavy, externí laryngeální manipulace, bužie nebo videolaryngoskop. Max. 3 pokusy + 1 pokus zkušenějším kolegou; mezi pokusy udržovat oxygenaci a hloubku anestezie.\n\n" +
            "Plán B — při neúspěchu zavést supraglotickou pomůcku (SAD, např. laryngeální maska 2. generace) a ověřit ventilaci.\n\n" +
            "Plán C — při neúspěchu SAD návrat k obličejové masce, oxygenace/ventilace; pokud možno probrat pacienta a obnovit spontánní ventilaci.\n\n" +
            "Plán D — CICO (nelze intubovat, nelze oxygenovat): okamžitý přechod na emergentní přístup k dýchacím cestám přední plochou krku (krikotyreotomie). Volat o pomoc ihned.\n\n" +
            "⚠ Obecný souhrn z odborné literatury, NENÍ doslovný přepis aktuálních DAS guidelines ani protokolu tohoto pracoviště — NUTNO OVĚŘIT a případně nahradit skutečným dokumentem.",

        ["Bronchospazmus"] =
            "Intraoperační bronchospazmus — obecný postup:\n\n" +
            "• 100% O₂, zvýšit FiO₂.\n" +
            "• Vyloučit mechanickou příčinu — zalomená/dislokovaná ETT, endobronchiální intubace, hlen/sekret (zkontrolovat, odsát).\n" +
            "• Zahloubit anestezii (zvýšit inhalační anestetikum, zvážit bolus propofolu).\n" +
            "• Inhalační bronchodilatancia (salbutamol do okruhu/ETT), případně IV salbutamol nebo terbutalin.\n" +
            "• Zvážit IV magnesium sulfát.\n" +
            "• Při těžkém/refrakterním průběhu zvážit adrenalin (dle tíže IM/IV/nebulizace).\n" +
            "• Zvážit kortikoidy (hydrokortizon IV) při přetrvávání.\n" +
            "• Ruční ventilace s delším expiračním časem, permisivní hyperkapnie — pozor na auto-PEEP/barotrauma.\n" +
            "• Vyloučit anafylaxi jako příčinu.\n" +
            "• Volat o pomoc při těžkém průběhu.\n\n" +
            "⚠ Obecný souhrn z odborné literatury — NUTNO OVĚŘIT oproti protokolům tohoto pracoviště.",

        ["Laryngospazmus"] =
            "Laryngospazmus — obecný postup:\n\n" +
            "• 100% O₂, odstranit stimul (sekret, krev, chirurgický podnět).\n" +
            "• CPAP obličejovou maskou, trojitý/Larsonův manévr (tlak v laryngospastickém zářezu za ušním lalůčkem).\n" +
            "• Zahloubit anestezii — bolus propofolu IV (je-li jen maska), případně zvýšit inhalační anestetikum.\n" +
            "• Odsát sekrety/krev z hltanu.\n" +
            "• Při přetrvávajícím spasmu s desaturací: suxamethonium IV/IM k přerušení spasmu, připravit se na intubaci.\n" +
            "• Zvážit atropin při bradykardii.\n" +
            "• Mít připravenou pomůcku k emergentní intubaci.\n\n" +
            "⚠ Obecný souhrn z odborné literatury — NUTNO OVĚŘIT oproti protokolům tohoto pracoviště.",
    };
}
