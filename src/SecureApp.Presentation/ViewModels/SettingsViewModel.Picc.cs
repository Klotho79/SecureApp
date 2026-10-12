using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// PICC roster (2026-10-12, user's own ask — see <see cref="IPiccRosterService"/>'s own remarks for
/// the full design). This device's OWN "picc_team" flag (distinct from the per-MEMBER switch in
/// <c>SettingsViewModel.Community</c>'s Správa členů, which is the admin setting it for someone
/// ELSE) gates whether the upload card below even shows — the relay enforces the same check
/// independently either way, so this is purely a "don't show a button that will just 403" nicety.
/// </summary>
public sealed partial class SettingsViewModel
{
    private readonly IDevicePolicyService _devicePolicyService;
    private readonly IPiccRosterService _piccRosterService;

    [ObservableProperty]
    public partial bool IsPiccTeam { get; set; }

    [ObservableProperty]
    public partial bool IsUploadingPiccRoster { get; set; }

    public bool CanUploadPiccRoster => !IsUploadingPiccRoster;

    [ObservableProperty]
    public partial string? PiccReportText { get; set; }

    [ObservableProperty]
    public partial bool HasPiccReport { get; set; }

    partial void OnIsUploadingPiccRosterChanged(bool value) => OnPropertyChanged(nameof(CanUploadPiccRoster));
    partial void OnPiccReportTextChanged(string? value) => HasPiccReport = !string.IsNullOrEmpty(value);

    /// <summary>Best-effort: a failure here must never block the rest of Settings from loading.</summary>
    private async Task LoadPiccStateAsync()
    {
        try
        {
            IsPiccTeam = await _piccRosterService.CanManageRosterAsync();
            if (!IsPiccTeam)
            {
                PiccReportText = null;
                return;
            }

            var roster = await _piccRosterService.GetFullRosterAsync();
            PiccReportText = roster is null
                ? "Zatím nebyl nahrán žádný rozpis PICC."
                : DescribeRoster(roster);
        }
        catch (Exception ex)
        {
            PiccReportText = $"Rozpis PICC se nepodařilo načíst: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UploadPiccRosterAsync()
    {
        FileResult? picked;
        try
        {
            picked = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Vyberte rozpis PICC (.xlsx)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".xlsx" },
                    [DevicePlatform.Android] = new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                    [DevicePlatform.iOS] = new[] { "org.openxmlformats.spreadsheetml.sheet" },
                    [DevicePlatform.MacCatalyst] = new[] { "org.openxmlformats.spreadsheetml.sheet" },
                })
            });
        }
        catch (Exception ex)
        {
            PiccReportText = $"Nepodařilo se otevřít výběr souborů: {ex.Message}";
            return;
        }

        if (picked is null) return;

        IsUploadingPiccRoster = true;
        PiccReportText = null;
        try
        {
            await using var stream = await picked.OpenReadAsync();
            var snapshot = await _piccRosterService.UploadRosterAsync(stream);
            PiccReportText = $"Nahráno ✓\n\n{DescribeRoster(snapshot)}";
        }
        catch (Exception ex)
        {
            PiccReportText = $"Nahrání se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsUploadingPiccRoster = false;
        }
    }

    private static string DescribeRoster(Domain.ValueObjects.PiccRosterSnapshot roster)
    {
        if (roster.Today is null)
            return $"Pro dnešek ({DateOnly.FromDateTime(DateTime.Today):d.M.yyyy}) nebyl v souboru nalezen žádný řádek — zkontrolujte formát data.\n\nNačteno dní celkem: {roster.AllDays.Count}.";

        var doctor = roster.Today.DoctorName ?? "(nevyplněno)";
        var nurses = roster.Today.NurseNames.Count == 0 ? "(nevyplněno)" : string.Join(", ", roster.Today.NurseNames);
        return $"Dnešní lékař PICC: {doctor}\nDnešní sestry PICC: {nurses}\n\nNačteno dní celkem: {roster.AllDays.Count}.";
    }
}
