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
///
/// The "DAS algoritmy" entry specifically (2026-10-03, user's own follow-up: "použij PDF z DAS") was
/// re-sourced via a live web search — the actual CURRENT guideline turned out to be a 2025 update
/// (BJA, January 2026), not the 2015 version this entry originally summarized from memory; the
/// 2025 update keeps the same Plan A-D structure but makes videolaryngoscopy first-line for Plan A
/// (previously a fallback). Still sourced from a secondary summary (NYSORA's writeup), not the full
/// primary BJA article (paywalled) — the entry says so and still carries its own OVĚŘIT disclaimer.
/// </summary>
public static class AcuteStateReferenceData
{
    public static IReadOnlyDictionary<string, string> Recommendations { get; } = new Dictionary<string, string>
    {
        ["DAS algoritmy (dýchací cesty)"] =
            "Obtížná/neúspěšná tracheální intubace u dospělého — podle DAS 2025 guidelines (BJA, leden 2026; aktualizace oproti dřívější verzi 2015), lineární algoritmus Plán A→D s důrazem na úspěch na první pokus, ne jen na zvládnutí selhání:\n\n" +
            "Plán A — tracheální intubace. 2025 novinka: videolaryngoskopie jako METODA PRVNÍ VOLBY (dříve jen záložní řešení při selhání přímé laryngoskopie), cílem je úspěch na první pokus. Jasně definovaný počet pokusů/čas, poté přechod na Plán B — nečekat na vyčerpání všech možností.\n\n" +
            "Plán B — zavedení supraglotické pomůcky (SAD) a ověření ventilace.\n\n" +
            "Plán C — při neúspěchu SAD obličejová maska; zvážit probuzení pacienta, pokud to stav dovoluje.\n\n" +
            "Plán D — eFONA (emergentní přístup k dýchacím cestám přední plochou krku) BEZ ODKLADU při CICO (nelze intubovat, nelze oxygenovat) — vyžaduje předchozí nácvik a připravený set.\n\n" +
            "Po zajištění — potvrdit polohu kapnografií, zdokumentovat postup a plán extubace, týmový debrief.\n\n" +
            "⚠ Shrnuto ze sekundárních zdrojů (NYSORA přehled DAS 2025, ne přímo z plného znění BJA článku) — NUTNO OVĚŘIT oproti plnému znění guidelines a protokolu tohoto pracoviště, nebo nahradit skutečným dokumentem.",

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
