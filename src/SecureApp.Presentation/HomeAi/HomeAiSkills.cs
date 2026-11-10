using System.Text.Json;
using Microsoft.Maui.Storage;

namespace SecureApp.Presentation.HomeAi;

/// <summary>
/// A "skill" of the home AI (2026-11-10): a named preset the user picks in the chat — extra instructions for the model
/// and, optionally, step-by-step problem decomposition. The user chooses it (a small model cannot be trusted to pick one
/// itself); the list lives only on the phone.
/// </summary>
public sealed class HomeAiSkill
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    /// <summary>One line shown in the empty chat: what to attach or ask with this skill.</summary>
    public string Hint { get; set; } = string.Empty;

    /// <summary>Always split the task into steps for this skill (see ProblemDecomposer).</summary>
    public bool Decompose { get; set; }

    /// <summary>The "no skill" entry in the picker.</summary>
    public static HomeAiSkill None { get; } = new() { Id = string.Empty, Name = "(bez skillu)" };

    public bool IsNone => Id.Length == 0;

    public override string ToString() => Name;
}

internal static class HomeAiSkillStore
{
    private const string ListKey = "homeai_skills_v1";
    private const string SelectedKey = "homeai_skill_id";

    public static string SelectedId
    {
        get => Preferences.Default.Get(SelectedKey, string.Empty);
        set => Preferences.Default.Set(SelectedKey, value);
    }

    /// <summary>The saved skills; the starter set the first time (nothing stored yet).</summary>
    public static List<HomeAiSkill> Load()
    {
        var json = Preferences.Default.Get(ListKey, string.Empty);
        if (json.Length > 0)
        {
            try
            {
                var saved = JsonSerializer.Deserialize<List<HomeAiSkill>>(json);
                if (saved is not null)
                    return saved;
            }
            catch (JsonException)
            {
                // A damaged value falls back to the starter set below.
            }
        }

        var defaults = Defaults();
        Save(defaults);
        return defaults;
    }

    public static void Save(IEnumerable<HomeAiSkill> skills)
        => Preferences.Default.Set(ListKey, JsonSerializer.Serialize(skills.Where(s => !s.IsNone).ToList()));

    /// <summary>The starter skills; the ids are fixed so "Obnovit výchozí" can tell which ones are missing.</summary>
    public static List<HomeAiSkill> Defaults() =>
    [
        new()
        {
            Id = "default-summary",
            Name = "Shrnutí dokumentu",
            Hint = "přiložte soubor (📎) a klepněte na Odeslat, text psát nemusíte.",
            Instructions = "Shrň přiložený text. Nejdřív napiš jednu větu, o čem dokument je, potom 5 až 8 nejdůležitějších bodů jako odrážky " +
                           "a nakonec případné termíny, částky nebo úkoly, které z textu vyplývají. Nic si nevymýšlej a nepřidávej.",
        },
        new()
        {
            Id = "default-table",
            Name = "Rozbor tabulky",
            Hint = "přiložte .xlsx nebo .xls a zeptejte se, třeba „kolik je celkem ve sloupci Částka?“ nebo „najdi řádky, kde…“.",
            Instructions = "Pracuješ s tabulkou. Použij názvy sloupců z řádku „Sloupce:“. Čísla uvedená v „Souhrn čísel“ jsou přesná, " +
                           "použij je a nepočítej sám. Při hledání řádků vypiš čísla řádků (Ř…) a hodnoty, které jsi použil. " +
                           "Když odpověď z tabulky nelze určit, řekni to.",
            Decompose = true,
        },
        new()
        {
            Id = "default-fix-text",
            Name = "Oprav text",
            Hint = "vložte nebo napište text a odešlete. Vrátí opravenou verzi a seznam změn.",
            Instructions = "Oprav pravopis, interpunkci a slovosled v textu, který ti uživatel pošle, ale zachovej jeho smysl a styl. " +
                           "Vrať nejdřív opravený text a pod ním stručně vypiš, co jsi změnil.",
        },
        new()
        {
            Id = "default-translate",
            Name = "Překlad česky ↔ anglicky",
            Hint = "napište text v češtině nebo angličtině, přeloží ho do druhého jazyka.",
            Instructions = "Přelož text z češtiny do angličtiny, nebo z angličtiny do češtiny podle toho, v jakém jazyce je napsaný. " +
                           "Vrať jen překlad bez komentáře. Zachovej odstavce a odrážky.",
        },
        new()
        {
            Id = "default-contract",
            Name = "Rozbor smlouvy / dopisu",
            Hint = "přiložte PDF nebo Word (nebo vložte text) a odešlete. Najde strany, lhůty, částky a rizikové body.",
            Instructions = "Projdi přiložený text a vypiš: kdo jsou strany, co se komu ukládá, termíny a lhůty, částky a sankce, a body, " +
                           "které jsou nejasné nebo rizikové pro uživatele. Neposuzuj právně závazně; u důležitých věcí doporuč konzultaci s odborníkem.",
            Decompose = true,
        },
    ];
}
