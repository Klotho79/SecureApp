using System.Collections.ObjectModel;
using System.Windows.Input;
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
    // --- Profil (2026-10-05, Windows multi-profile login) — Profiles.ActiveProfile's own remarks.
    // HasActiveProfile is false on every platform except a logged-in Windows session, so this card
    // simply doesn't render anywhere else — no separate platform check needed in the ViewModel.

    public bool HasActiveProfile => Profiles.ActiveProfile.Current is not null;

    public string ActiveProfileNameText => Profiles.ActiveProfile.Current is { } profile ? $"Přihlášen jako: {profile.Name}" : string.Empty;

    [RelayCommand]
    private void LogOut() => Profiles.ActiveProfile.ClearAndRestart();

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

    // --- Sub-tabs within each main tab (2026-10-11, user's ask: "v nastaveni jsou strasne dlouhe
    // obsahy... udelej podmenu jako napr v uzivateli udelej podmenu ucet vzhled zobrazeni") — one
    // level deeper than SettingsTab above, same converter-free int+derived-bools+command pattern,
    // one independent int per main tab so switching between Uživatel/Systém/Admin never disturbs
    // which sub-section was open in the others.

    [ObservableProperty]
    public partial int UserSubTab { get; set; }

    public bool IsUserAccountSubTab => UserSubTab == 0;
    public bool IsUserAppearanceSubTab => UserSubTab == 1;
    public bool IsUserVisibilitySubTab => UserSubTab == 2;
    // Nástěnka (former index 3) removed 2026-10-13 — its only content, the board composer, moved to
    // the new Redakce tab (RedakceViewModel), consolidated there with the sál-status-options admin
    // card that used to live inline on Nástěnka. User's own ask: "vsechny modifikace upravy a
    // vkladani zprav... dej do nastenky jako jednu polozku na spodnim menu".

    partial void OnUserSubTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsUserAccountSubTab));
        OnPropertyChanged(nameof(IsUserAppearanceSubTab));
        OnPropertyChanged(nameof(IsUserVisibilitySubTab));
    }

    [RelayCommand]
    private void SelectUserSubTab(string index)
    {
        if (int.TryParse(index, out var i))
            UserSubTab = i;
    }

    [ObservableProperty]
    public partial int SystemSubTab { get; set; }

    public bool IsSystemConnectionSubTab => SystemSubTab == 0;
    public bool IsSystemIdentitySubTab => SystemSubTab == 1;
    public bool IsSystemOpicentrumSubTab => SystemSubTab == 2;
    // Rozpis (index 3) removed 2026-10-12 — its only content, "Barvy typů rozpisu", moved to
    // Uživatel/Vzhled (user's own ask: a per-device appearance setting belongs there, not under
    // Systém). Every sub-tab after it shifted down by one; renumber here AND in SettingsPage.xaml's
    // matching CommandParameters if another one is ever inserted/removed.
    public bool IsSystemUpdateSubTab => SystemSubTab == 3;
    public bool IsSystemLibrarySubTab => SystemSubTab == 4;
    public bool IsSystemDiagnosticsSubTab => SystemSubTab == 5;
    public bool IsSystemPiccSubTab => SystemSubTab == 6;

    partial void OnSystemSubTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsSystemConnectionSubTab));
        OnPropertyChanged(nameof(IsSystemIdentitySubTab));
        OnPropertyChanged(nameof(IsSystemOpicentrumSubTab));
        OnPropertyChanged(nameof(IsSystemUpdateSubTab));
        OnPropertyChanged(nameof(IsSystemLibrarySubTab));
        OnPropertyChanged(nameof(IsSystemDiagnosticsSubTab));
        OnPropertyChanged(nameof(IsSystemPiccSubTab));
    }

    [RelayCommand]
    private void SelectSystemSubTab(string index)
    {
        if (int.TryParse(index, out var i))
            SystemSubTab = i;
    }

    [ObservableProperty]
    public partial int AdminSubTab { get; set; }

    public bool IsAdminDebugSubTab => AdminSubTab == 0;
    public bool IsAdminLogsSubTab => AdminSubTab == 1;
    // 2026-10-17, user's own ask ("potom sloucilo se menu clenove a zarizeni") — the former separate
    // "Zařízení" sub-tab (index 3) is gone; its content now lives nested under each member on THIS
    // tab, see MemberPolicyItem/AdminMemberGroupItem's own remarks. Every sub-tab after it shifted
    // down by one, same renumbering discipline as the Rozpis-removal precedent above.
    public bool IsAdminMembersSubTab => AdminSubTab == 2;
    public bool IsAdminDeploySubTab => AdminSubTab == 3;
    public bool IsAdminAiSubTab => AdminSubTab == 4;
    public bool IsAdminNewMemberSubTab => AdminSubTab == 5;

    partial void OnAdminSubTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsAdminDebugSubTab));
        OnPropertyChanged(nameof(IsAdminLogsSubTab));
        OnPropertyChanged(nameof(IsAdminMembersSubTab));
        OnPropertyChanged(nameof(IsAdminDeploySubTab));
        OnPropertyChanged(nameof(IsAdminAiSubTab));
        OnPropertyChanged(nameof(IsAdminNewMemberSubTab));
    }

    [RelayCommand]
    private void SelectAdminSubTab(string index)
    {
        if (int.TryParse(index, out var i))
            AdminSubTab = i;
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

    // --- Background messaging (2026-09-26) — status + a way into Android's battery setting.

    public bool IsBackgroundRunSupported => Infrastructure.BackgroundRun.IsSupported;

    [ObservableProperty]
    public partial string BackgroundRunStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBackgroundRunAllowed { get; set; }

    public void RefreshBackgroundRunStatus()
    {
        IsBackgroundRunAllowed = Infrastructure.BackgroundRun.IsAllowed();
        BackgroundRunStatusText = IsBackgroundRunAllowed
            ? "✓ Aplikace smí běžet na pozadí — zprávy a oznámení chodí i když je zavřená."
            : "⚠ Android může aplikaci uspat — zprávy pak přijdou až při jejím otevření.";
    }

    [RelayCommand]
    private async Task OpenBackgroundRunSettingsAsync()
    {
        if (Shell.Current is { } shell && !await shell.DisplayAlertAsync(Infrastructure.BackgroundRun.Title, Infrastructure.BackgroundRun.Explanation, "Nastavit", "Zrušit"))
            return;
        Infrastructure.BackgroundRun.OpenSettings();
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

    // --- Member management (Admin only) ----------------------------------------------------------
    //
    // 2026-10-17, user's own ask: "nezobrazovali vsichni clenove ale bylo roll nenu se cleny a
    // vybrany by se zobrazil a potom sloucilo se menu clenove a zarizeni.... a ukazalo se to pod
    // clenem vsechna zarizeni... spolecny mail" — replaces the old flat "every member, every device"
    // list with a Picker of PEOPLE (MemberGroups); selecting one shows just their shared e-mail
    // (ArimContactEmail, once) and every one of THEIR devices nested underneath, each combining the
    // role/tab-policy editor this screen always had with the staleness/log/deregister actions that
    // used to live on the separate "Zařízení" sub-tab — see MemberPolicyItem/AdminMemberGroupItem's
    // own remarks for the merged shape.

    public ObservableCollection<AdminMemberGroupItem> MemberGroups { get; } = [];

    [ObservableProperty]
    public partial AdminMemberGroupItem? SelectedMemberGroup { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMembers { get; set; }

    [ObservableProperty]
    public partial string? MemberManagementStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasMemberManagementStatus { get; set; }

    partial void OnMemberManagementStatusTextChanged(string? value) => HasMemberManagementStatus = !string.IsNullOrEmpty(value);

    /// <summary>
    /// Loads BOTH the role/policy data (<see cref="IRelayAdminService.GetManagedDevicesAsync"/>) and
    /// the staleness/pending-outbox data (<see cref="IRelayAdminService.GetRegisteredDevicesAsync"/>)
    /// and merges them one row per device (joined by DeviceId — both calls read from the same
    /// underlying `devices` table, see RelayDatabase.GetManagedDevices/GetAllDevicesWithStatus's own
    /// remarks, so every device appears in both), then groups by the same ArimContactId both already
    /// carry into one <see cref="AdminMemberGroupItem"/> per person. 2026-10-17, user's own ask:
    /// neither call needs the admin secret to LOAD — only <see cref="SaveMemberPolicyAsync"/> and
    /// <see cref="DeregisterDeviceAsync"/> below still take it, fresh per call.
    /// </summary>
    [RelayCommand]
    private async Task LoadMembersAsync()
    {
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            MemberManagementStatusText = "Nejprve zadejte platnou adresu relay serveru.";
            return;
        }

        IsLoadingMembers = true;
        try
        {
            var managed = await _relayAdminService.GetManagedDevicesAsync(endpoint);
            var registered = await _relayAdminService.GetRegisteredDevicesAsync(endpoint);
            var registeredById = registered.ToDictionary(d => d.Id);

            var rows = managed.Select(m =>
            {
                registeredById.TryGetValue(m.DeviceId, out var reg);
                var platformSuffix = !string.IsNullOrEmpty(reg?.Platform) ? $"  ({DescribePlatform(reg.Platform)})" : "";
                var statusText = reg?.LastActiveAtUtc is { } lastActive
                    ? $"Aktivní v adresáři — naposledy {lastActive.LocalDateTime:g}"
                    : "Nikdy nepublikoval do adresáře, nebo je dávno neaktivní";
                var pendingCount = reg?.PendingOutboxCount ?? 0;
                var pendingText = pendingCount > 0
                    ? $"⚠ {pendingCount} zpráv čeká na doručení tomuto zařízení"
                    : "Žádné čekající zprávy";

                return new MemberPolicyItem(
                    m.DeviceId, m.DisplayName + platformSuffix, m.Role, m.HiddenTabs, m.LastSeenUtc,
                    m.IsDocumentReviewer, m.AppVersion, m.IsPiccTeam, m.HasSharedLibraryKey,
                    statusText, pendingText, pendingCount > 0, reg?.LastActiveAtUtc is null,
                    DeregisterDeviceCommand, ShowDeviceErrorsCommand, ShowDeviceEventsCommand);
            }).ToList();

            var previousName = SelectedMemberGroup?.DisplayName;
            var groups = new List<AdminMemberGroupItem>();

            var byPerson = rows.Zip(managed, (item, m) => (item, m))
                .Where(x => x.m.ArimContactId is not null)
                .GroupBy(x => x.m.ArimContactId!.Value)
                .OrderBy(g => g.Select(x => x.m.ArimContactName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "", StringComparer.CurrentCultureIgnoreCase);
            foreach (var person in byPerson)
            {
                var name = person.Select(x => x.m.ArimContactName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Neznámý kontakt";
                var email = person.Select(x => x.m.ArimContactEmail).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));
                groups.Add(new AdminMemberGroupItem(name, email, person.Select(x => x.item).ToList()));
            }

            var unlinked = rows.Zip(managed, (item, m) => (item, m)).Where(x => x.m.ArimContactId is null).Select(x => x.item).ToList();
            if (unlinked.Count > 0)
                groups.Add(new AdminMemberGroupItem("❔ Nepřiřazená zařízení", null, unlinked));

            MemberGroups.Clear();
            foreach (var g in groups)
                MemberGroups.Add(g);
            SelectedMemberGroup = MemberGroups.FirstOrDefault(g => g.DisplayName == previousName) ?? MemberGroups.FirstOrDefault();

            MemberManagementStatusText = managed.Count == 0 ? "Zatím žádní členové." : $"Načteno členů: {managed.Count} zařízení, {groups.Count} osob.";

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
        finally
        {
            IsLoadingMembers = false;
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
        if (!TryTakeAdminSecret(out var adminSecret))
        {
            MemberManagementStatusText = "Nejprve zadejte admin heslo.";
            return;
        }

        try
        {
            await _relayAdminService.SetDevicePolicyAsync(endpoint, adminSecret, member.DeviceId, member.SelectedRole, member.HiddenTabs, member.IsDocumentReviewer, member.IsPiccTeam);
            member.StatusText = "Uloženo ✓";
            MemberManagementStatusText = $"Uloženo pro {member.DisplayName}.";
        }
        catch (Exception ex)
        {
            member.StatusText = "Chyba";
            MemberManagementStatusText = $"Uložení se nezdařilo: {ex.Message}";
        }
    }

    /// <summary>
    /// Admin-triggered nudge for a member stuck without the shared library key (2026-10-12, direct
    /// follow-up to a real user report of bogus Nástěnka notifications — see
    /// <c>App.SyncBoardToNotificationsAsync</c>'s own remarks for the bug this surfaced). Re-runs the
    /// SAME escrow publish <see cref="LoadManagedMembersAsync"/> already fires automatically on every
    /// load — there is no per-recipient targeting in <c>PublishWrappedKeyForMembersAsync</c> (it wraps
    /// for every current member at once), so this is a manual "do it again right now" rather than a
    /// way to single out one device. Still worth exposing per-row: the admin reaches for it exactly
    /// when looking at ONE stuck member, even though it helps everyone who's missing the key.
    /// </summary>
    [RelayCommand]
    private async Task ResendSharedLibraryKeyAsync(MemberPolicyItem? member)
    {
        if (member is null) return;
        try
        {
            await _sharedLibraryService.PublishWrappedKeyForMembersAsync();
            member.StatusText = "Klíč znovu odeslán — zařízení ho vyzvedne při příštím připojení (do ~3 min).";
        }
        catch (Exception ex)
        {
            member.StatusText = $"Odeslání se nezdařilo: {ex.Message}";
        }
    }

    /// <summary>Called from the page's OnDisappearing.</summary>
    public void ClearMemberManagement()
    {
        MemberGroups.Clear();
        SelectedMemberGroup = null;
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

/// <summary>
/// One member group in the merged "Členové" admin screen (2026-10-17) — see the "Member management"
/// section's own remarks above for why this replaced the old flat "every member, every device" list.
/// <see cref="Email"/> is the ONE shared e-mail for this person (<c>ManagedDevice.ArimContactEmail</c>,
/// the same "Soukromé kontakty ARIM" field every device record already carries), shown once above
/// their device list rather than per-device — the user's own explicit call ("Jeden e-mail na člena,
/// ne na zařízení"). <see cref="Devices"/> holds every one of that person's devices, each already
/// merged with its staleness/log/deregister data — see <see cref="MemberPolicyItem"/>'s own remarks.
/// The "❔ Nepřiřazená zařízení" pseudo-group (devices with no ArimContactId yet) has a null Email.
/// </summary>
public sealed class AdminMemberGroupItem
{
    public AdminMemberGroupItem(string displayName, string? email, IReadOnlyList<MemberPolicyItem> devices)
    {
        DisplayName = displayName;
        Email = email;
        Devices = new ObservableCollection<MemberPolicyItem>(devices);
    }

    public string DisplayName { get; }
    public string? Email { get; }
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);
    public ObservableCollection<MemberPolicyItem> Devices { get; }
}

/// <summary>
/// One device row in the admin's merged "Členové" screen — editable role + per-tab visibility
/// (always available), PLUS, 2026-10-17, the staleness/pending-outbox/log/deregister data that used
/// to live on the separate "Zařízení" sub-tab before the user asked to merge the two
/// ("potom sloucilo se menu clenove a zarizeni... a ukazalo se to pod clenem vsechna zarizeni") —
/// see <c>SettingsViewModel.Community.LoadMembersAsync</c> for how the two data sources (role/policy
/// vs. staleness) are joined into one of these per device. <see cref="DeviceStatusText"/>/
/// <see cref="PendingText"/> are named distinctly from <see cref="StatusText"/> (this class's
/// pre-existing "Save result" message) to avoid a collision between the two unrelated meanings.
/// </summary>
public sealed partial class MemberPolicyItem : ObservableObject
{
    public MemberPolicyItem(
        Guid deviceId, string displayName, Role? role, IReadOnlyList<string> hiddenTabs, DateTimeOffset? lastSeenUtc,
        bool isDocumentReviewer = false, string? appVersion = null, bool isPiccTeam = false, bool hasSharedLibraryKey = false,
        string? deviceStatusText = null, string? pendingText = null, bool hasPendingMessages = false, bool isStale = false,
        ICommand? deregisterCommand = null, ICommand? showErrorsCommand = null, ICommand? showEventsCommand = null)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
        SelectedRole = role;
        ShowChaty = !hiddenTabs.Contains(AppShell.ChatsTabVisibilityPreferenceKey);
        ShowSoubory = !hiddenTabs.Contains(AppShell.FilesTabVisibilityPreferenceKey);
        ShowKontakty = !hiddenTabs.Contains(AppShell.ContactsTabVisibilityPreferenceKey);
        ShowNastenka = !hiddenTabs.Contains(AppShell.NotificationsTabVisibilityPreferenceKey);
        ShowLogbook = !hiddenTabs.Contains(AppShell.LogbookVisibilityPreferenceKey);
        IsDocumentReviewer = isDocumentReviewer;
        IsPiccTeam = isPiccTeam;
        LastSeenText = lastSeenUtc is { } seen
            ? $"naposledy {seen.LocalDateTime:d.M. HH:mm}"
            : "nikdy nepřipojeno";
        // AppVersion (2026-10-01) — null means either an older client that predates this field, or a
        // device that has never published to the directory since this field shipped; both read the
        // same to the admin ("neznámá"), since neither case is actionable differently right now.
        AppVersionText = string.IsNullOrWhiteSpace(appVersion) ? "verze neznámá" : $"verze {appVersion}";

        // HasSharedLibraryKey (2026-10-12) — see ManagedDevice's own remarks on why this is here at
        // all. HasSharedLibraryKey itself (not just the text) drives the "Odeslat klíč" button's
        // IsVisible in SettingsPage.xaml — no point showing it for a member who already has the key.
        HasSharedLibraryKey = hasSharedLibraryKey;
        SharedLibraryKeyText = hasSharedLibraryKey ? "🔑 má klíč sdílené knihovny" : "🔒 NEMÁ klíč sdílené knihovny";

        DeviceStatusText = deviceStatusText ?? "";
        PendingText = pendingText ?? "";
        HasPendingMessages = hasPendingMessages;
        IsStale = isStale;
        DeregisterCommand = deregisterCommand;
        ShowErrorsCommand = showErrorsCommand;
        ShowEventsCommand = showEventsCommand;
    }

    public Guid DeviceId { get; }
    public string DisplayName { get; }
    public string LastSeenText { get; }
    public string AppVersionText { get; }
    public bool HasSharedLibraryKey { get; }
    /// <summary>Converter-free inverse of <see cref="HasSharedLibraryKey"/> for the resend button's IsVisible, same style as the rest of this file's own derived bools.</summary>
    public bool MissingSharedLibraryKey => !HasSharedLibraryKey;
    public string SharedLibraryKeyText { get; }

    /// <summary>Directory staleness (2.1, 2026-09-17) — see the old <c>RegisteredDeviceItem.StatusText</c> this replaces.</summary>
    public string DeviceStatusText { get; }
    public string PendingText { get; }
    public bool HasPendingMessages { get; }
    public bool IsStale { get; }
    public ICommand? DeregisterCommand { get; }
    public ICommand? ShowErrorsCommand { get; }
    public ICommand? ShowEventsCommand { get; }

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

    /// <summary>Document Library review capability (2026-10-01) — orthogonal to <see cref="SelectedRole"/>, see <c>DevicePolicy.IsDocumentReviewer</c>'s own remarks.</summary>
    [ObservableProperty]
    public partial bool IsDocumentReviewer { get; set; }

    /// <summary>PICC roster capability (2026-10-12) — orthogonal to <see cref="SelectedRole"/>, see <c>DevicePolicy.IsPiccTeam</c>'s own remarks.</summary>
    [ObservableProperty]
    public partial bool IsPiccTeam { get; set; }

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
