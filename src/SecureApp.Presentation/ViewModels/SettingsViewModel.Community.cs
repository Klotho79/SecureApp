using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Community governance (2026-09-24, user's own ask): Admin/Modifier post to the notice board, and
/// an Admin assigns each member a role + which tabs they may see. Split into its own partial like
/// the Opicentrum/Updates sections. The board and role/tab policy are the relay's responsibility
/// now — a member no longer picks their own role (the Account card's Role is read-only), and hidden
/// tabs are applied automatically on connect (see <c>App.ApplyDevicePolicyAsync</c>).
/// </summary>
public sealed partial class SettingsViewModel
{
    private readonly ICommunityBoardService _communityBoardService;

    // --- Settings sub-tabs (2026-09-24) — the page had grown to ~15 stacked cards in one scroll,
    // which the user found cluttered ("nepřehledné"). Grouped into Uživatel / Systém / Admin, one
    // shown at a time. Plain int + derived bools + a select command, converter-free (the tab
    // buttons reflect selection via DataTriggers on these bools).

    [ObservableProperty]
    public partial int SettingsTab { get; set; }

    public bool IsUserSettingsTab => SettingsTab == 0;
    public bool IsSystemSettingsTab => SettingsTab == 1;
    public bool IsAdminSettingsTab => SettingsTab == 2;

    partial void OnSettingsTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsUserSettingsTab));
        OnPropertyChanged(nameof(IsSystemSettingsTab));
        OnPropertyChanged(nameof(IsAdminSettingsTab));
    }

    [RelayCommand]
    private void SelectSettingsTab(string index)
    {
        if (int.TryParse(index, out var i))
            SettingsTab = i;
    }

    // --- Appearance (Phase 4, easy customization) ------------------------------------------------
    // Light/dark/system, per device, applied live via App.ApplyThemeMode (which flips every
    // AppThemeBinding immediately) and persisted so it also holds across restarts.

    public IReadOnlyList<string> ThemeModeChoices { get; } = ["Podle systému", "Světlý", "Tmavý"];

    [ObservableProperty]
    public partial int ThemeModeIndex { get; set; }

    private bool _themeModeLoaded;

    private void LoadThemeMode()
    {
        // Set the backing value without letting the change handler re-persist during load.
        _themeModeLoaded = false;
        ThemeModeIndex = Microsoft.Maui.Storage.Preferences.Default.Get(App.ThemeModePreferenceKey, 0);
        _themeModeLoaded = true;
    }

    partial void OnThemeModeIndexChanged(int value)
    {
        if (!_themeModeLoaded) return; // ignore the initial load-time assignment
        Microsoft.Maui.Storage.Preferences.Default.Set(App.ThemeModePreferenceKey, value);
        App.ApplyThemeMode(value);
    }

    // --- Accent colour (Phase 4) — presets + custom hex. Colours are StaticResource, so this stores
    // + applies the accent (overwriting the accent-family resources) but only fully takes effect on
    // the next launch; the preview swatch below shows the chosen colour immediately regardless.

    public IReadOnlyList<AccentSwatchItem> AccentPresets { get; } =
        Infrastructure.AccentPalette.Presets.Select(p => new AccentSwatchItem(p.Name, p.Hex)).ToList();

    [ObservableProperty]
    public partial string AccentHexInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Color AccentPreviewColor { get; set; } = Colors.Transparent;

    [ObservableProperty]
    public partial string? AccentStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasAccentStatus { get; set; }

    partial void OnAccentStatusTextChanged(string? value) => HasAccentStatus = !string.IsNullOrEmpty(value);

    // --- Font size (Phase 4) — rescales the app's base text live via App.ApplyFontScale.

    public IReadOnlyList<string> FontScaleChoices { get; } = ["Menší", "Normální", "Větší", "Největší"];

    [ObservableProperty]
    public partial int FontScaleIndex { get; set; }

    private bool _fontScaleLoaded;

    private void LoadFontScale()
    {
        _fontScaleLoaded = false;
        FontScaleIndex = Microsoft.Maui.Storage.Preferences.Default.Get(App.FontScalePreferenceKey, 1);
        _fontScaleLoaded = true;
    }

    partial void OnFontScaleIndexChanged(int value)
    {
        if (!_fontScaleLoaded) return;
        Microsoft.Maui.Storage.Preferences.Default.Set(App.FontScalePreferenceKey, value);
        App.ApplyFontScale(value);
    }

    // --- Chat appearance (2026-09-26) — message font size + own-bubble colour, both applied live.

    public IReadOnlyList<string> ChatFontSizeChoices { get; } = Infrastructure.ChatAppearance.FontSizeChoices;

    [ObservableProperty]
    public partial int ChatFontSizeIndex { get; set; }

    [ObservableProperty]
    public partial Color ChatBubblePreviewColor { get; set; } = Colors.Transparent;

    private bool _chatAppearanceLoaded;

    private void LoadChatAppearance()
    {
        _chatAppearanceLoaded = false;
        var prefs = Microsoft.Maui.Storage.Preferences.Default;
        ChatFontSizeIndex = prefs.Get(Infrastructure.ChatAppearance.FontSizePreferenceKey, 1);
        var hex = prefs.Get(Infrastructure.ChatAppearance.BubbleColorPreferenceKey, string.Empty);
        ChatBubblePreviewColor = string.IsNullOrEmpty(hex) ? AccentPreviewColor : SafeColor(hex);
        _chatAppearanceLoaded = true;
    }

    partial void OnChatFontSizeIndexChanged(int value)
    {
        if (!_chatAppearanceLoaded || value < 0) return;
        Microsoft.Maui.Storage.Preferences.Default.Set(Infrastructure.ChatAppearance.FontSizePreferenceKey, value);
        Infrastructure.ChatAppearance.ApplyFontSize(value);
    }

    /// <summary>Empty hex = back to following the app colour.</summary>
    [RelayCommand]
    private void SelectChatBubbleColor(string? hex)
    {
        hex = Infrastructure.AccentPalette.IsValidHex(hex) ? hex : string.Empty;
        Microsoft.Maui.Storage.Preferences.Default.Set(Infrastructure.ChatAppearance.BubbleColorPreferenceKey, hex);
        Infrastructure.ChatAppearance.ApplyBubbleColor(hex);
        ChatBubblePreviewColor = string.IsNullOrEmpty(hex) ? AccentPreviewColor : SafeColor(hex!);
    }

    private void LoadAccent()
    {
        var stored = Microsoft.Maui.Storage.Preferences.Default.Get(Infrastructure.AccentPalette.PreferenceKey, string.Empty);
        AccentHexInput = string.IsNullOrEmpty(stored) ? Infrastructure.AccentPalette.Presets[0].Hex : stored;
        AccentPreviewColor = SafeColor(AccentHexInput);
    }

    [RelayCommand]
    private void SelectAccent(string hex)
    {
        if (!Infrastructure.AccentPalette.IsValidHex(hex))
        {
            AccentStatusText = "Neplatná barva (použijte #RRGGBB).";
            return;
        }
        AccentHexInput = hex;
        AccentPreviewColor = SafeColor(hex);
        Microsoft.Maui.Storage.Preferences.Default.Set(Infrastructure.AccentPalette.PreferenceKey, hex);
        Infrastructure.AccentPalette.Apply(hex);
        AccentStatusText = "Barva uložena — plně se projeví po restartu aplikace.";
    }

    private static Color SafeColor(string hex)
    {
        try { return Color.FromArgb(hex.StartsWith('#') ? hex : "#" + hex); }
        catch { return Colors.Transparent; }
    }

    // Held for the duration of a member-management session so the admin types the secret once (at
    // "Nacist cleny") rather than again for every per-member save — the same "reuse within one
    // logical action" exception the activation-approval flow already makes, just spanning the whole
    // management screen. Cleared by ClearMemberManagement (called from the page's OnDisappearing).
    private string? _managementAdminSecret;

    // --- Notice board composer -------------------------------------------------------------------

    [ObservableProperty]
    public partial bool CanPostToBoard { get; set; }

    [ObservableProperty]
    public partial string BoardMessageText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? BoardStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasBoardStatus { get; set; }

    partial void OnBoardStatusTextChanged(string? value) => HasBoardStatus = !string.IsNullOrEmpty(value);

    /// <summary>Set from LoadAsync once the stored role is known — Admin and Modifier may post, Viewer may not (mirrors the relay's own POST /board gate).</summary>
    private void RefreshCanPostToBoard()
        => CanPostToBoard = _currentUserService.Current.Role is Role.Admin or Role.Modifier;

    [RelayCommand]
    private async Task PostToBoardAsync()
    {
        var text = BoardMessageText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            BoardStatusText = "Zpráva je prázdná.";
            return;
        }

        try
        {
            await _communityBoardService.PostAsync(text);
            BoardMessageText = string.Empty;
            BoardStatusText = "Odesláno na nástěnku.";
        }
        catch (Exception ex)
        {
            BoardStatusText = $"Nepodařilo se odeslat: {ex.Message}";
        }
    }

    // --- Member management (Admin only) ----------------------------------------------------------

    public ObservableCollection<MemberPolicyItem> ManagedMembers { get; } = [];

    [ObservableProperty]
    public partial string? MemberManagementStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasMemberManagementStatus { get; set; }

    partial void OnMemberManagementStatusTextChanged(string? value) => HasMemberManagementStatus = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task LoadManagedMembersAsync()
    {
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            MemberManagementStatusText = "Nejprve zadejte platnou adresu relay serveru.";
            return;
        }
        if (!TryTakeAdminSecret(out var adminSecret))
        {
            MemberManagementStatusText = "Nejprve zadejte admin heslo.";
            return;
        }
        _managementAdminSecret = adminSecret;

        try
        {
            var members = await _relayAdminService.GetManagedDevicesAsync(endpoint, adminSecret);
            ManagedMembers.Clear();
            foreach (var m in members)
                ManagedMembers.Add(new MemberPolicyItem(m.DeviceId, m.DisplayName, m.Role, m.HiddenTabs, m.LastSeenUtc));
            MemberManagementStatusText = members.Count == 0 ? "Zatím žádní členové." : $"Načteno členů: {members.Count}.";

            // The admin opening this screen is, by definition, online right now and (almost always)
            // holds the shared library key — so this is the ideal moment to make sure every listed
            // member has their key escrowed, instead of waiting for a key-holder's periodic sweep to
            // happen by. Fire-and-forget, best-effort: a no-op on a device that doesn't hold the key,
            // and it never blocks the list from showing. This is what shortens a brand-new member's
            // "no library key yet" window to "as soon as the admin looks at them".
            _ = _sharedLibraryService.PublishWrappedKeyForMembersAsync();
        }
        catch (Exception ex)
        {
            MemberManagementStatusText = $"Načtení se nezdařilo: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveMemberPolicyAsync(MemberPolicyItem? member)
    {
        if (member is null) return;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            MemberManagementStatusText = "Neplatná adresa relay serveru.";
            return;
        }
        if (string.IsNullOrEmpty(_managementAdminSecret))
        {
            MemberManagementStatusText = "Znovu načtěte členy — vypršelo admin heslo.";
            return;
        }

        try
        {
            await _relayAdminService.SetDevicePolicyAsync(endpoint, _managementAdminSecret, member.DeviceId, member.SelectedRole, member.HiddenTabs);
            member.StatusText = "Uloženo ✓";
            MemberManagementStatusText = $"Uloženo pro {member.DisplayName}.";
        }
        catch (Exception ex)
        {
            member.StatusText = "Chyba";
            MemberManagementStatusText = $"Uložení se nezdařilo: {ex.Message}";
        }
    }

    /// <summary>Called from the page's OnDisappearing so the admin secret held for the management session doesn't outlive it.</summary>
    public void ClearMemberManagement()
    {
        _managementAdminSecret = null;
        ManagedMembers.Clear();
        MemberManagementStatusText = null;
    }
}

/// <summary>One accent-colour preset swatch in Settings — name + hex + a Color for the preview square.</summary>
public sealed class AccentSwatchItem
{
    public AccentSwatchItem(string name, string hex)
    {
        Name = name;
        Hex = hex;
        try { SwatchColor = Color.FromArgb(hex); } catch { SwatchColor = Colors.Gray; }
    }

    public string Name { get; }
    public string Hex { get; }
    public Color SwatchColor { get; }
}

/// <summary>One member row in the admin's "Správa členů" screen — editable role + per-tab visibility.</summary>
public sealed partial class MemberPolicyItem : ObservableObject
{
    public MemberPolicyItem(Guid deviceId, string displayName, Role? role, IReadOnlyList<string> hiddenTabs, DateTimeOffset? lastSeenUtc)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
        SelectedRole = role;
        ShowChaty = !hiddenTabs.Contains(AppShell.ChatsTabVisibilityPreferenceKey);
        ShowSoubory = !hiddenTabs.Contains(AppShell.FilesTabVisibilityPreferenceKey);
        ShowKontakty = !hiddenTabs.Contains(AppShell.ContactsTabVisibilityPreferenceKey);
        ShowNastenka = !hiddenTabs.Contains(AppShell.NotificationsTabVisibilityPreferenceKey);
        ShowLogbook = !hiddenTabs.Contains(AppShell.LogbookVisibilityPreferenceKey);
        LastSeenText = lastSeenUtc is { } seen
            ? $"naposledy {seen.LocalDateTime:d.M. HH:mm}"
            : "nikdy nepřipojeno";
    }

    public Guid DeviceId { get; }
    public string DisplayName { get; }
    public string LastSeenText { get; }

    /// <summary>Picker source: index 0 = "ponechat na zařízení" (null), then the three real roles.</summary>
    public IReadOnlyList<string> RoleChoices { get; } = ["Neurčeno (ponechat)", "Admin", "Modifier", "Viewer"];

    [ObservableProperty]
    public partial int SelectedRoleIndex { get; set; }

    [ObservableProperty]
    public partial bool ShowChaty { get; set; }

    [ObservableProperty]
    public partial bool ShowSoubory { get; set; }

    [ObservableProperty]
    public partial bool ShowKontakty { get; set; }

    [ObservableProperty]
    public partial bool ShowNastenka { get; set; }

    [ObservableProperty]
    public partial bool ShowLogbook { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    /// <summary>Maps <see cref="SelectedRoleIndex"/> back to the domain role (index 0 => null = unmanaged).</summary>
    public Role? SelectedRole
    {
        get => SelectedRoleIndex switch { 1 => Role.Admin, 2 => Role.Modifier, 3 => Role.Viewer, _ => (Role?)null };
        set => SelectedRoleIndex = value switch { Role.Admin => 1, Role.Modifier => 2, Role.Viewer => 3, _ => 0 };
    }

    public IReadOnlyList<string> HiddenTabs
    {
        get
        {
            var hidden = new List<string>();
            if (!ShowChaty) hidden.Add(AppShell.ChatsTabVisibilityPreferenceKey);
            if (!ShowSoubory) hidden.Add(AppShell.FilesTabVisibilityPreferenceKey);
            if (!ShowKontakty) hidden.Add(AppShell.ContactsTabVisibilityPreferenceKey);
            if (!ShowNastenka) hidden.Add(AppShell.NotificationsTabVisibilityPreferenceKey);
            if (!ShowLogbook) hidden.Add(AppShell.LogbookVisibilityPreferenceKey);
            return hidden;
        }
    }
}
