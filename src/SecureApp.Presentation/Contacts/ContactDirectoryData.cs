namespace SecureApp.Presentation.Contacts;

/// <summary>One row of the extension directory (2026-09-10, transcribed from the reference "Příručka a logbook začínajícího anesteziologa" PDF's own "Telefonní seznam" page) — <see cref="Name"/> is kept exactly as printed, including a handful of names truncated in the source table's own narrow columns (e.g. "Minařík,..."), rather than guessed.</summary>
public sealed record PhoneDirectoryEntry(string Section, string Name, string Number);

/// <summary>One row of "Kam volat při komplikacích s…" (2026-09-10) — a situation and who/what to contact for it; <see cref="Answer"/> may hold more than one line for a situation with several possible contacts.</summary>
public sealed record QuickContactEntry(string Situation, string Answer);

/// <summary>
/// Static reference data (2026-09-10, user's own ask: "doplň tel. seznam z logbook ale do
/// samostatného menu tel seznam a rychlé kontakty") — a plain read-only lookup, not stored in any
/// database or synced via the relay: unlike the Logbook's checklists/procedure types, nobody edits
/// this from within the app, so there's no reason to carry schema/sync machinery for it. If the
/// hospital's own extensions change, this file is what to update and redeploy.
/// </summary>
public static class ContactDirectoryData
{
    public static IReadOnlyList<PhoneDirectoryEntry> PhoneDirectory { get; } =
    [
        // ARO
        new("ARO", "Gabrhelík", "2280"),
        new("ARO", "Turek", "2380"),
        new("ARO", "vrchní sestra", "2279"),
        new("ARO", "Ševelová", "2765"),
        new("ARO", "Graus", "2595"),
        new("ARO", "Jiřičková", "2389"),
        new("ARO", "Minařík,...", "2289"),
        new("ARO", "Mana", "2835"),
        new("ARO", "GynInspe", "2618"),
        new("ARO", "Číž", "2832"),
        new("ARO", "anes.amb.", "2619"),
        new("ARO", "algez. II", "2896"),
        new("ARO", "anest.II", "2542"),
        new("ARO", "algez.amb.", "2484"),
        new("ARO", "Mašlík,...", "2587"),
        new("ARO", "Neum.,....", "2589"),
        new("ARO", "ARO", "2370, 2287"),
        new("ARO", "NIP", "2081, 2091"),
        new("ARO", "NIP-lék", "2093"),
        new("ARO", "DIOP", "2066"),
        new("ARO", "SestryChir", "2597"),
        new("ARO", "SestryGyn", "2596"),

        // Sály
        new("Sály", "chir. I", "2806"),
        new("Sály", "chir. III", "2807"),
        new("Sály", "endo I", "2816"),
        new("Sály", "endo II", "2801"),
        new("Sály", "ortop.", "2851"),
        new("Sály", "NCH", "2804"),
        new("Sály", "Traum", "2853"),
        new("Sály", "gyn.", "2615"),
        new("Sály", "gyn2", "2635"),
        new("Sály", "ORL", "2716"),
        new("Sály", "por.s.", "2925"),
        new("Sály", "oční", "2997"),
        new("Sály", "zubní", "2611"),
        new("Sály", "angio", "3179"),
        new("Sály", "koronar.", "3188"),
        new("Sály", "dosp.ch", "2815"),
        new("Sály", "dosp.g", "2736"),
        new("Sály", "LERV", "2859"),
        new("Sály", "broncho", "2622"),

        // OUP
        new("OUP", "dolní UP", "2485"),
        new("OUP", "ER", "2247, 2282"),

        // Oddělení
        new("Oddělení", "trauma", "2387"),
        new("Oddělení", "chir. 4", "2477"),
        new("Oddělení", "chir. 5", "2577"),
        new("Oddělení", "NCH", "2129"),
        new("Oddělení", "ortop", "2641"),
        new("Oddělení", "urol.", "2737"),
        new("Oddělení", "ORL", "2727"),
        new("Oddělení", "Neuro 3", "2037"),
        new("Oddělení", "Neuro 4", "2047"),
        new("Oddělení", "Int 3", "2337"),
        new("Oddělení", "Int 4", "2447"),
        new("Oddělení", "Int 5", "2547"),
        new("Oddělení", "Int 6", "2647"),
        new("Oddělení", "Int 7", "2720"),
        new("Oddělení", "Gynek 4", "2947"),
        new("Oddělení", "Gynek 5", "2957"),
        new("Oddělení", "Gynek 6", "2967"),
        new("Oddělení", "Děti 7p", "2227"),
        new("Oddělení", "Děti vel.", "2017"),
        new("Oddělení", "oční", "2987"),
        new("Oddělení", "onkol.V", "2625"),
        new("Oddělení", "stac.gyn.", "2924"),

        // JIP
        new("JIP", "sept.", "2579, 2823"),
        new("JIP", "asept.", "2379, 2834"),
        new("JIP", "lékaři", "6651, 6664"),
        new("JIP", "Int. II.et.", "2228"),
        new("JIP", "Koronár.", "2449"),
        new("JIP", "Neurol.", "2049"),
        new("JIP", "Plicní", "2677"),
        new("JIP", "Dětská", "2913"),
        new("JIP", "Onkol.", "2399"),

        // Laboratoř
        new("Laboratoř", "bioch.", "2797, 2788"),
        new("Laboratoř", "mikro", "3279, 3139"),
        new("Laboratoř", "Bartoníkov", "3136"),
        new("Laboratoř", "hematol.", "2329"),
        new("Laboratoř", "transfuz.", "2334"),

        // Ambulance
        new("Ambulance", "úraz.", "2273, 2377"),

        // Ostatní
        new("Ostatní", "oběd", "2347"),
        new("Ostatní", "Pravdíková", "2492"),
        new("Ostatní", "IT", "2223"),
        new("Ostatní", "Klin.farma", "2241"),

        // RTG
        new("RTG", "CT nov", "2655"),
        new("RTG", "CT nov-lék.", "2657"),
        new("RTG", "MR nov", "2681"),
        new("RTG", "MR nov-lék", "2640"),
        new("RTG", "CT", "6684, 2767"),
        new("RTG", "CT-lékař", "2769"),
        new("RTG", "MR", "2659"),
        new("RTG", "MR-lék.", "2658"),
        new("RTG", "sono", "2630, 2690"),
        new("RTG", "RTG II.et.", "2884"),
        new("RTG", "RTG úraz.", "2871"),
        new("RTG", "lékaři", "2891"),
        new("RTG", "labor.", "2892"),
        new("RTG", "CT registrace", "2654"),
        new("RTG", "ARIM II", "6651"),
        new("RTG", "ARIM III", "6664"),
        new("RTG", "Mašláňová", "2148"),
        new("RTG", "DUP", "2112"),
        new("RTG", "KPR", "2222"),
        new("RTG", "porodnice", "2929"),

        // Mobily UPS
        new("Mobily UPS", "ARO-odd", "6601"),
        new("Mobily UPS", "ARO-KPR", "6654"),
        new("Mobily UPS", "ARO-chir.", "6602"),
        new("Mobily UPS", "ARO-gyn.", "6603"),
        new("Mobily UPS", "AROses", "6604-6605"),
        new("Mobily UPS", "AROses.g.", "6605"),
        new("Mobily UPS", "UP-ER", "6626"),
        new("Mobily UPS", "COS-chir", "6638"),
        new("Mobily UPS", "COS-trau", "6642"),
        new("Mobily UPS", "COS-gyn", "6643"),
        new("Mobily UPS", "Gyn", "6611-6613"),
        new("Mobily UPS", "Chir", "6606, 6607"),
        new("Mobily UPS", "Int.", "6608-6610, 6622"),
        new("Mobily UPS", "Neuro", "6615, 6637"),
        new("Mobily UPS", "NCH", "6616"),
        new("Mobily UPS", "RTG", "6629-6631, 6684"),
        new("Mobily UPS", "Trauma", "6632"),
        new("Mobily UPS", "Urolog", "6617"),
        new("Mobily UPS", "anest.stan", "6653"),
        new("Mobily UPS", "Mana", "6652"),
        new("Mobily UPS", "PACU", "6716"),
        new("Mobily UPS", "Předvolba", "57755"),
    ];

    /// <summary>Sections in the order they should list, matching the reference PDF's own column order.</summary>
    public static IReadOnlyList<string> Sections { get; } =
        ["ARO", "Sály", "OUP", "Oddělení", "JIP", "Laboratoř", "Ambulance", "Ostatní", "RTG", "Mobily UPS"];

    /// <summary>"Kam volat v případě komplikací s…" — the reference PDF's own quick-lookup table.</summary>
    public static IReadOnlyList<QuickContactEntry> QuickContacts { get; } =
    [
        new("Obecně", "Váš školitel — číslo si zapište do Nastavení nebo sem, jak vám vyhovuje."),
        new("Akutní stavy", "Lékař z vedlejšího sálu."),
        new("Akutní stavy (není-li nikdo v okolí)", "Lékař ARIM I — kl. 6654."),
        new("Komplikace s anestezií", "Atestovaný lékař z okolních sálů.\nLékař PACU — kl. 6716.\nOrdinář: Vilém Dvořák."),
        new("Organizace operačních programů", "Rudolf Mana — kl. 6652."),
        new("Rozpis pracovišť, dovolená, nemoc", "Mana — kl. 6652, dále Turek, Gabrhelík."),
        new("Organizace anesteziologických sester", "Miriam Skoumalová — kl. 6653."),
        new("Dostupnost lůžek ARIM I", "kl. 2370, 6601."),
        new("Dostupnost lůžek ARIM II", "kl. 2579, 6651."),
        new("Dostupnost lůžek ARIM III", "kl. 2379, 6664."),
        new("Dostupnost lůžek PACU, PNB", "kl. 2815, 6716."),
        new("Výkaz, čipová karta, přístupy", "Sekretariát ARIM — kl. 2765."),
    ];
}
