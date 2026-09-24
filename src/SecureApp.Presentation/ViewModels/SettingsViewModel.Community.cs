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
