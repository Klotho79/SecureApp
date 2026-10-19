using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Shared moderation queue (CMS_PLAN.md, phase 5) — every pending annotation proposal across every CMS
/// document, for Admin/Modifier. "Ponechat"/"Odmítnout" can run on several selected rows at once (plain
/// status flips, nothing to review by hand); "Začlenit" always opens one document in <see cref="CmsEditorPage"/>
/// at its anchored block, because incorporating text is a manual edit, never an automatic replacement.
/// </summary>
public sealed partial class CmsAnnotationQueueViewModel : ObservableObject
{
    private readonly ICmsAnnotationService _annotations;

    [ObservableProperty]
    public partial ObservableCollection<CmsAnnotationQueueItem> PendingItems { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    public CmsAnnotationQueueViewModel(ICmsAnnotationService annotations)
    {
        _annotations = annotations ?? throw new ArgumentNullException(nameof(annotations));
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        try
        {
            var pending = await _annotations.ListPendingAsync();
            PendingItems = new ObservableCollection<CmsAnnotationQueueItem>(pending.Select(ToItem));
            IsEmpty = PendingItems.Count == 0;
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Frontu se nepodařilo načíst: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Opens the document the same way "Moje zadání" continues a draft — <c>assignedDocumentId</c>
    /// (<see cref="CmsEditorPage.LoadAssignmentAsync"/> pulls the latest version straight from the relay,
    /// no local import needed, and skips the upload-role gate since it's set). The pending-annotation
    /// params just drive the banner; the relay still enforces who may actually save.
    /// </summary>
    [RelayCommand]
    private async Task IncorporateAsync(CmsAnnotationQueueItem? item)
    {
        if (item is null) return;
        var title = Uri.EscapeDataString(item.DocumentTitle);
        var quote = Uri.EscapeDataString(item.ContextQuote);
        var note = Uri.EscapeDataString(item.Text);
        await Shell.Current.GoToAsync(
            $"{nameof(CmsEditorPage)}?assignedDocumentId={item.DocumentId}&assignedTitle={title}&pendingAnnotationId={item.AnnotationId}&pendingAnnotationQuote={quote}&pendingAnnotationText={note}",
            animate: false);
    }

    /// <summary>Bulk "Ponechat"/"Odmítnout" over several selected rows — plain status flips, so a per-item loop is enough (no new relay bulk endpoint).</summary>
    public async Task ModerateManyAsync(IReadOnlyList<CmsAnnotationQueueItem> items, string decision)
    {
        if (items.Count == 0) return;
        StatusErrorMessage = null;
        var failures = 0;
        foreach (var item in items)
        {
            try
            {
                await _annotations.ModerateAsync(item.AnnotationId, decision, null);
            }
            catch
            {
                failures++;
            }
        }
        if (failures > 0)
            StatusErrorMessage = $"{failures} z {items.Count} poznámek se nepodařilo vyřídit.";
        await LoadAsync();
    }

    private static CmsAnnotationQueueItem ToItem(CmsPendingAnnotationView v) => new(
        v.AnnotationId, v.DocumentId, v.DocumentTitle, v.AuthorName,
        v.Anchor?.Exact ?? string.Empty, v.Text, v.CreatedAtUtc.LocalDateTime.ToString("d.M. HH:mm"));
}

/// <summary>One row in the moderation queue.</summary>
public sealed record CmsAnnotationQueueItem(
    Guid AnnotationId, Guid DocumentId, string DocumentTitle, string AuthorName, string ContextQuote, string Text, string CreatedAtText);
