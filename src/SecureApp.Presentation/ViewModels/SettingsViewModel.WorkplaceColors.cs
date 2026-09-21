using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;
using SecureApp.Domain.Enums;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Workplace/Rozpis type colors (2026-09-21, user's own ask alongside the collapsible legend on
/// <c>WorkplacePage</c>: "uživ nastavení barev" — one full color per <see cref="AssignmentType"/>,
/// per-device only, same as the tab-visibility toggles above rather than relay-synced). Storage and
/// the default palette live in <c>AssignmentColorCatalog</c>, not here — this file is just the Settings
/// UI over it. Split into its own partial file, same precedent as <c>SettingsViewModel.Opicentrum.cs</c>.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<AssignmentColorSettingItem> WorkplaceColorItems { get; set; }

    private void LoadWorkplaceColorsState() =>
        WorkplaceColorItems = new ObservableCollection<AssignmentColorSettingItem>(
            AssignmentTypeCatalog.All.Select(type => new AssignmentColorSettingItem(type)));
}

/// <summary>
/// One row on the Settings "Barvy typů rozpisu" card — a live hex-entry + swatch + reset, applying
/// immediately on every valid hex typed (no separate Save button, matching this card's own "projeví se
/// hned" convention already used by the tab-visibility toggles above it). A small <c>ObservableObject</c>
/// rather than a plain record for the same reason <c>LogbookStatGroupItem</c> is (see its own remarks) —
/// <see cref="HexInput"/>/<see cref="Preview"/> are genuinely mutable per-row UI state, not a value handed
/// in once from the owning ViewModel.
/// </summary>
public sealed partial class AssignmentColorSettingItem : ObservableObject
{
    public AssignmentType Type { get; }
    public string Glyph { get; }
    public string Label { get; }

    [ObservableProperty]
    public partial string HexInput { get; set; }

    [ObservableProperty]
    public partial Color Preview { get; set; }

    /// <summary>True only while the current <see cref="HexInput"/> text fails to parse — an empty/in-progress edit isn't flagged as an error until it settles on something invalid.</summary>
    [ObservableProperty]
    public partial bool HasInvalidHex { get; set; }

    [ObservableProperty]
    public partial bool IsCustomized { get; set; }

    // Guards OnHexInputChanged against firing as a "user edit" for the constructor's own initial mirror
    // assignment (and Reset's own re-assignment below) — CommunityToolkit's generated setter calls the
    // partial On*Changed hook on EVERY assignment, including the first one, not just ones a person
    // typed. Without this, just opening Settings would write every type's own default color back into
    // Preferences as a "customized" override (real bug caught live: every Reset button showed enabled
    // immediately, even for untouched types) — silently pinning today's defaults forever, so a future
    // change to AssignmentColorCatalog's own default palette would never reach a device that had merely
    // opened this page once.
    private bool _isInitialized;

    public AssignmentColorSettingItem(AssignmentType type)
    {
        Type = type;
        Glyph = AssignmentTypeCatalog.Glyph(type);
        Label = AssignmentTypeCatalog.Label(type);
        var color = AssignmentColorCatalog.BaseColor(type);
        HexInput = ToHex(color);
        Preview = color;
        IsCustomized = AssignmentColorCatalog.IsCustomized(type);
        _isInitialized = true;
    }

    partial void OnHexInputChanged(string value)
    {
        if (!_isInitialized) return;

        var trimmed = (value ?? string.Empty).Trim();
        if (!Color.TryParse(trimmed, out var color))
        {
            HasInvalidHex = trimmed.Length > 0;
            return;
        }

        HasInvalidHex = false;
        Preview = color;
        AssignmentColorCatalog.SetColor(Type, color);
        IsCustomized = true;
    }

    [RelayCommand]
    private void Reset()
    {
        AssignmentColorCatalog.ResetColor(Type);
        var color = AssignmentColorCatalog.DefaultColor(Type);
        Preview = color;
        _isInitialized = false;
        HexInput = ToHex(color);
        _isInitialized = true;
        IsCustomized = false;
    }

    private static string ToHex(Color color) =>
        $"#{ToByte(color.Red):X2}{ToByte(color.Green):X2}{ToByte(color.Blue):X2}";

    private static byte ToByte(float channel) => (byte)Math.Clamp(MathF.Round(channel * 255f), 0, 255);
}
