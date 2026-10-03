using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Glasgow Coma Scale calculator (2026-10-03) — the first built-in native "Nástroje" tool, reached
/// from <see cref="LibraryViewModel.ShowNastrojeTools"/>'s own card. Plain client-side arithmetic,
/// no relay/library involvement at all — this is a clinical calculator, not library content.
/// Defaults every component to its best/normal value (E4+V5+M6=15) so the page shows a real result
/// immediately rather than an empty state; the user adjusts down from there.
/// </summary>
public sealed partial class GcsCalculatorViewModel : ObservableObject
{
    public ObservableCollection<GcsOption> EyeOptions { get; } = new(
    [
        new GcsOption(4, "4 — Spontánní otevření očí"),
        new GcsOption(3, "3 — Otevření na výzvu (oslovení)"),
        new GcsOption(2, "2 — Otevření na bolestivý podnět"),
        new GcsOption(1, "1 — Neotevírá oči"),
    ]);

    public ObservableCollection<GcsOption> VerbalOptions { get; } = new(
    [
        new GcsOption(5, "5 — Orientovaná, přiléhavá řeč"),
        new GcsOption(4, "4 — Zmatená řeč"),
        new GcsOption(3, "3 — Nepřiléhavá slova"),
        new GcsOption(2, "2 — Nesrozumitelné zvuky"),
        new GcsOption(1, "1 — Žádná slovní reakce"),
    ]);

    public ObservableCollection<GcsOption> MotorOptions { get; } = new(
    [
        new GcsOption(6, "6 — Vyhoví výzvě (pohyb na příkaz)"),
        new GcsOption(5, "5 — Lokalizuje bolest (cílený pohyb)"),
        new GcsOption(4, "4 — Normální flexe (úniková reakce)"),
        new GcsOption(3, "3 — Abnormální flexe (dekortikace)"),
        new GcsOption(2, "2 — Extenze (decerebrace)"),
        new GcsOption(1, "1 — Žádná motorická reakce"),
    ]);

    [ObservableProperty]
    public partial GcsOption SelectedEye { get; set; }

    [ObservableProperty]
    public partial GcsOption SelectedVerbal { get; set; }

    [ObservableProperty]
    public partial GcsOption SelectedMotor { get; set; }

    [ObservableProperty]
    public partial int TotalScore { get; set; }

    [ObservableProperty]
    public partial string ResultText { get; set; } = string.Empty;

    /// <summary>Drives the result card's color via a XAML DataTrigger (see GcsCalculatorPage.xaml) — same "Level"-driven color pattern the diagnostic log already uses, not a converter.</summary>
    [ObservableProperty]
    public partial string Severity { get; set; } = string.Empty;

    public GcsCalculatorViewModel()
    {
        SelectedEye = EyeOptions[0];
        SelectedVerbal = VerbalOptions[0];
        SelectedMotor = MotorOptions[0];
        Recompute();
    }

    partial void OnSelectedEyeChanged(GcsOption value) => Recompute();
    partial void OnSelectedVerbalChanged(GcsOption value) => Recompute();
    partial void OnSelectedMotorChanged(GcsOption value) => Recompute();

    /// <summary>
    /// Each Selected* assignment in the constructor fires its own OnXChanged handler immediately
    /// (CommunityToolkit's generated setter), BEFORE the other two have been assigned yet — the
    /// null-guard here is load-bearing, not defensive filler: without it, the very first assignment
    /// (SelectedEye = ...) calls this while SelectedVerbal/SelectedMotor are still null, throwing
    /// NullReferenceException from inside page construction during Shell navigation. That exception
    /// doesn't surface as a normal .NET crash dialog there — it crosses a native/managed boundary
    /// and takes the whole process down as a native APPCRASH (combase.dll, E_POINTER), which is what
    /// made this look like a platform/MediaElement bug until bisected down to this one call.
    /// </summary>
    private void Recompute()
    {
        if (SelectedEye is null || SelectedVerbal is null || SelectedMotor is null) return;

        TotalScore = SelectedEye.Score + SelectedVerbal.Score + SelectedMotor.Score;
        (ResultText, Severity) = TotalScore switch
        {
            15 => ("GCS 15 — normální stav vědomí.", "Normal"),
            >= 13 => ($"GCS {TotalScore} — lehká porucha vědomí (13–15).", "Mild"),
            >= 9 => ($"GCS {TotalScore} — středně těžká porucha vědomí (9–12).", "Moderate"),
            _ => ($"GCS {TotalScore} — těžká porucha vědomí / kóma (3–8). Zvážit zajištění dýchacích cest.", "Severe"),
        };
    }
}

/// <summary>One selectable option for one GCS component (E/V/M) — plain data, no MAUI type, matching this codebase's established convention (see e.g. CategoryChipItem).</summary>
public sealed record GcsOption(int Score, string Label);
