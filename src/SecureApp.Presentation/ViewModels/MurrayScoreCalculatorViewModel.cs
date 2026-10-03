using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Murray lung injury score (2026-10-03) — second built-in "Nástroje" tool after the GCS calculator
/// (see <see cref="LibraryViewModel.NastrojeTools"/>/<see cref="GcsCalculatorViewModel"/> for the
/// shared card/list pattern). Four components — RTG hrudníku (alveolární konsolidace), oxygenace
/// (PaO₂/FiO₂), PEEP, plicní compliance — each scored 0–4; the total is their AVERAGE (sum/4), the
/// standard Murray (1988) scoring, not a sum, so one severely abnormal component alone can't push
/// the total into the severe band on its own.
/// </summary>
public sealed partial class MurrayScoreCalculatorViewModel : ObservableObject
{
    public ObservableCollection<MurrayOption> ChestXRayOptions { get; } = new(
    [
        new(0, "0 — bez alveolární konsolidace"),
        new(1, "1 — konsolidace v 1 kvadrantu"),
        new(2, "2 — konsolidace ve 2 kvadrantech"),
        new(3, "3 — konsolidace ve 3 kvadrantech"),
        new(4, "4 — konsolidace ve 4 kvadrantech"),
    ]);

    public ObservableCollection<MurrayOption> HypoxemiaOptions { get; } = new(
    [
        new(0, "0 — PaO₂/FiO₂ ≥ 300"),
        new(1, "1 — PaO₂/FiO₂ 225–299"),
        new(2, "2 — PaO₂/FiO₂ 175–224"),
        new(3, "3 — PaO₂/FiO₂ 100–174"),
        new(4, "4 — PaO₂/FiO₂ < 100"),
    ]);

    public ObservableCollection<MurrayOption> PeepOptions { get; } = new(
    [
        new(0, "0 — PEEP ≤ 5 cmH₂O"),
        new(1, "1 — PEEP 6–8 cmH₂O"),
        new(2, "2 — PEEP 9–11 cmH₂O"),
        new(3, "3 — PEEP 12–14 cmH₂O"),
        new(4, "4 — PEEP ≥ 15 cmH₂O"),
    ]);

    public ObservableCollection<MurrayOption> ComplianceOptions { get; } = new(
    [
        new(0, "0 — compliance ≥ 80 ml/cmH₂O"),
        new(1, "1 — compliance 60–79 ml/cmH₂O"),
        new(2, "2 — compliance 40–59 ml/cmH₂O"),
        new(3, "3 — compliance 20–39 ml/cmH₂O"),
        new(4, "4 — compliance ≤ 19 ml/cmH₂O"),
    ]);

    [ObservableProperty]
    public partial MurrayOption SelectedChestXRay { get; set; }

    [ObservableProperty]
    public partial MurrayOption SelectedHypoxemia { get; set; }

    [ObservableProperty]
    public partial MurrayOption SelectedPeep { get; set; }

    [ObservableProperty]
    public partial MurrayOption SelectedCompliance { get; set; }

    [ObservableProperty]
    public partial double TotalScore { get; set; }

    [ObservableProperty]
    public partial string ResultText { get; set; } = string.Empty;

    /// <summary>Drives the result card's color via a XAML DataTrigger — same "Level"-driven color pattern <see cref="GcsCalculatorViewModel.Severity"/> already established (here: Normal/Mild/Severe, 3 bands not 4).</summary>
    [ObservableProperty]
    public partial string Severity { get; set; } = string.Empty;

    public MurrayScoreCalculatorViewModel()
    {
        SelectedChestXRay = ChestXRayOptions[0];
        SelectedHypoxemia = HypoxemiaOptions[0];
        SelectedPeep = PeepOptions[0];
        SelectedCompliance = ComplianceOptions[0];
        Recompute();
    }

    partial void OnSelectedChestXRayChanged(MurrayOption value) => Recompute();
    partial void OnSelectedHypoxemiaChanged(MurrayOption value) => Recompute();
    partial void OnSelectedPeepChanged(MurrayOption value) => Recompute();
    partial void OnSelectedComplianceChanged(MurrayOption value) => Recompute();

    /// <summary>
    /// Same constructor-ordering hazard <see cref="GcsCalculatorViewModel.Recompute"/> already
    /// documented and fixed after a real crash: each Selected* assignment in the constructor above
    /// fires its own OnXChanged → Recompute() immediately, before the other three properties have
    /// been assigned yet — this null-guard is load-bearing from the start here, not added after the
    /// fact.
    /// </summary>
    private void Recompute()
    {
        if (SelectedChestXRay is null || SelectedHypoxemia is null || SelectedPeep is null || SelectedCompliance is null) return;

        TotalScore = (SelectedChestXRay.Score + SelectedHypoxemia.Score + SelectedPeep.Score + SelectedCompliance.Score) / 4.0;
        (ResultText, Severity) = TotalScore switch
        {
            0 => ("Murray 0 — žádné plicní poškození.", "Normal"),
            <= 2.5 => ($"Murray {TotalScore:0.##} — lehké až středně těžké plicní poškození (ALI).", "Mild"),
            _ => ($"Murray {TotalScore:0.##} — těžké plicní poškození (ARDS).", "Severe"),
        };
    }
}

/// <summary>One selectable option for one Murray component — plain data, same shape as <see cref="GcsOption"/>.</summary>
public sealed record MurrayOption(int Score, string Label);
