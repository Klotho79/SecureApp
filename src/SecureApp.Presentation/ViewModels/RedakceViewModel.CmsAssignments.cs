using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.ViewModels;

/// <summary>One row of the assignment lists (CMS_PLAN.md, phase 2c).</summary>
public sealed record CmsAssignmentItem(CmsAssignment Model)
{
    public string Title => Model.Title;
    public string EditorName => Model.EditorName;
    public string StatusText => Model.DocumentStatus switch
    {
        null => "zatím nezačato",
        "Draft" => "koncept",
        "PendingReview" => "čeká na schválení",
        "Published" => "schváleno — úpravy čekají na schválení",
        var other => other,
    };
}

/// <summary>Redakce cards for per-document editor grants: assigners (Admin/Modifier) hand out and withdraw documents, every editor sees "Moje zadání".</summary>
public sealed partial class RedakceViewModel
{
    private const string CmsFolder = "Výukové materiály";

    [ObservableProperty]
    public partial bool CanAssignCms { get; set; }

    [ObservableProperty]
    public partial bool HasMyAssignments { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<CmsAssignmentItem> AllAssignments { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<CmsAssignmentItem> MyAssignments { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<DirectoryMember> AssignableMembers { get; set; } = [];

    [ObservableProperty]
    public partial DirectoryMember? SelectedAssignee { get; set; }

    [ObservableProperty]
    public partial string NewAssignmentTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AssignmentStatusText { get; set; }

    [ObservableProperty]
    public partial bool HasAssignmentStatus { get; set; }

    partial void OnAssignmentStatusTextChanged(string? value) => HasAssignmentStatus = !string.IsNullOrEmpty(value);

    private async Task LoadCmsAssignmentsAsync(Role role)
    {
        // Only a desktop has the editor (CMS_PLAN.md); the assigner overview is still useful elsewhere but stays desktop-only for simplicity.
        if (DeviceInfo.Idiom != DeviceIdiom.Desktop) return;

        CanAssignCms = role is Role.Admin or Role.Modifier;
        try
        {
            MyAssignments = new ObservableCollection<CmsAssignmentItem>((await _cmsAssignments.ListMineAsync()).Select(a => new CmsAssignmentItem(a)));
            HasMyAssignments = MyAssignments.Count > 0;
        }
        catch
        {
            HasMyAssignments = false; // relay unreachable — the rest of Redakce must still load
        }

        if (!CanAssignCms) return;
        try
        {
            AllAssignments = new ObservableCollection<CmsAssignmentItem>((await _cmsAssignments.ListAllAsync()).Select(a => new CmsAssignmentItem(a)));
            AssignableMembers = new ObservableCollection<DirectoryMember>((await _contactDirectory.ListMembersAsync()).OrderBy(m => m.DisplayName));
        }
        catch (Exception ex)
        {
            AssignmentStatusText = $"Zadání se nepodařilo načíst: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AssignCmsEditorAsync()
    {
        if (SelectedAssignee is null || string.IsNullOrWhiteSpace(NewAssignmentTitle))
        {
            AssignmentStatusText = "Vyberte editora a zadejte název dokumentu.";
            return;
        }

        try
        {
            await _cmsAssignments.AssignAsync(null, SelectedAssignee.RelayDeviceId, NewAssignmentTitle.Trim(), CmsFolder);
            AssignmentStatusText = $"Dokument „{NewAssignmentTitle.Trim()}“ je přidělen: {SelectedAssignee.DisplayName}.";
            NewAssignmentTitle = string.Empty;
            AllAssignments = new ObservableCollection<CmsAssignmentItem>((await _cmsAssignments.ListAllAsync()).Select(a => new CmsAssignmentItem(a)));
        }
        catch (Exception ex)
        {
            AssignmentStatusText = $"Přidělení se nezdařilo: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RevokeCmsEditorAsync(CmsAssignmentItem? item)
    {
        if (item is null) return;
        try
        {
            await _cmsAssignments.RevokeAsync(item.Model.DocumentId, item.Model.EditorDeviceId);
            AllAssignments.Remove(item);
            AssignmentStatusText = $"Právo editora pro „{item.Title}“ bylo odebráno.";
        }
        catch (Exception ex)
        {
            AssignmentStatusText = $"Odebrání se nezdařilo: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenMyAssignment(CmsAssignmentItem? item)
    {
        if (item is null) return;
        RequestNavigate?.Invoke($"{nameof(Views.CmsEditorPage)}?assignedDocumentId={item.Model.DocumentId}&assignedTitle={Uri.EscapeDataString(item.Title)}");
    }
}
