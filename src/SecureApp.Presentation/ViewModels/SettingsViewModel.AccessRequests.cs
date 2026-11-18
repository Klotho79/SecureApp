using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Nastavení → Admin → Členové: "Žádosti o přístup" (2026-11-18, user: "uživatel vytvoří účet a já ho schválím, vše
/// ostatní udělá appka"). A new member's request waits here; the admin picks Modifier, Viewer or Redaktor (never Admin,
/// the user's rule) and approves or rejects — from this device, no admin secret. The new member's app connects by itself.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<AccessRequestItem> AccessRequests { get; set; } = [];

    [ObservableProperty]
    public partial bool HasAccessRequests { get; set; }

    /// <summary>Mirror of <see cref="HasAccessRequests"/> — this app's pattern instead of a converter.</summary>
    [ObservableProperty]
    public partial bool HasNoAccessRequests { get; set; } = true;

    partial void OnHasAccessRequestsChanged(bool value) => HasNoAccessRequests = !value;

    [ObservableProperty]
    public partial string? AccessRequestsStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasAccessRequestsStatus { get; set; }

    partial void OnAccessRequestsStatusTextChanged(string? value) => HasAccessRequestsStatus = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task LoadAccessRequestsAsync()
    {
        if (!IsAdmin || !Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint)) return;
        try
        {
            var pending = await _relayAdminService.GetPendingAccessRequestsAsync(endpoint);
            AccessRequests = new ObservableCollection<AccessRequestItem>(pending.OrderBy(r => r.CreatedAtUtc).Select(r => new AccessRequestItem(r)));
            HasAccessRequests = AccessRequests.Count > 0;
        }
        catch (Exception ex)
        {
            AccessRequestsStatusText = $"Žádosti o přístup se nepodařilo načíst: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApproveAccessRequestAsync(AccessRequestItem? item)
    {
        if (item is null || !Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint)) return;
        AccessRequestsStatusText = null;
        try
        {
            await _relayAdminService.ApproveAccessRequestAsync(endpoint, item.Id, item.SelectedRole);
            AppLog.Event("admin.access-request.approved", ("role", item.SelectedRole));
            AccessRequestsStatusText = $"✓ {item.DisplayName} schválen(a) jako {item.SelectedRole} — appka se mu připojí sama.";
            await LoadAccessRequestsAsync();
            await LoadMembersAsync();
        }
        catch (Exception ex)
        {
            AccessRequestsStatusText = $"Schválení se nezdařilo: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RejectAccessRequestAsync(AccessRequestItem? item)
    {
        if (item is null || !Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint)) return;
        if (Shell.Current?.CurrentPage is not { } page
            || !await page.DisplayAlertAsync("Zamítnout žádost", $"Zamítnout přístup pro „{item.DisplayName}“ ({item.Email})?", "Zamítnout", "Zrušit"))
            return;

        AccessRequestsStatusText = null;
        try
        {
            await _relayAdminService.RejectAccessRequestAsync(endpoint, item.Id);
            AppLog.Event("admin.access-request.rejected");
            AccessRequestsStatusText = $"Žádost „{item.DisplayName}“ zamítnuta.";
            await LoadAccessRequestsAsync();
        }
        catch (Exception ex)
        {
            AccessRequestsStatusText = $"Zamítnutí se nezdařilo: {ex.Message}";
        }
    }
}

/// <summary>One waiting request, with the role the admin is about to give (Modifier by default).</summary>
public sealed partial class AccessRequestItem(AccessRequest request) : ObservableObject
{
    public Guid Id { get; } = request.Id;
    public string DisplayName { get; } = request.DisplayName;
    public string Email { get; } = request.Email;
    public string CreatedText { get; } = $"Požádal(a) {request.CreatedAtUtc.LocalDateTime:d. M. H:mm}";

    /// <summary>No Admin — a new member never gets it (the relay refuses it as well).</summary>
    public IReadOnlyList<string> RoleChoices { get; } = ["Modifier", "Viewer", "Redaktor"];

    [ObservableProperty]
    public partial int RoleIndex { get; set; }

    public Role SelectedRole => RoleIndex switch { 1 => Role.Viewer, 2 => Role.Redaktor, _ => Role.Modifier };
}
