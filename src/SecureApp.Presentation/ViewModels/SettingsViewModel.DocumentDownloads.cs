using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Document-download audit log search (2026-09-30, user's own ask: "bude log kdo co kdy stahl podle
/// dokumentu vyhledatelny") — admin-secret-gated, same "type the admin password fresh each time,
/// never stored" pattern <see cref="SettingsViewModel.TryTakeAdminSecret"/> already established for
/// every other admin action on this page. The write side (logging an actual download) lives in
/// <c>DocumentViewerViewModel.DownloadAsync</c>, not here.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial string DocumentDownloadSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<DocumentDownloadLogItem> DocumentDownloadResults { get; set; } = [];

    [ObservableProperty]
    public partial bool HasNoDocumentDownloadResults { get; set; }

    [ObservableProperty]
    public partial bool IsSearchingDocumentDownloads { get; set; }

    [RelayCommand]
    private async Task SearchDocumentDownloadsAsync()
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }
        if (!TryTakeAdminSecret(out var adminSecret)) return;

        IsSearchingDocumentDownloads = true;
        try
        {
            var results = await _relayAdminService.SearchDocumentDownloadsAsync(endpoint, adminSecret, DocumentDownloadSearchText);
            DocumentDownloadResults = new ObservableCollection<DocumentDownloadLogItem>(results.Select(ToDocumentDownloadLogItem));
            HasNoDocumentDownloadResults = DocumentDownloadResults.Count == 0;
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Vyhledávání v logu stažení se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsSearchingDocumentDownloads = false;
        }
    }

    private static DocumentDownloadLogItem ToDocumentDownloadLogItem(Domain.ValueObjects.DocumentDownloadEntry entry) =>
        new(entry.DisplayName, entry.DocumentTitle, entry.DownloadedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
}

/// <summary>One row of the document-download search results — <see cref="DownloadedAtText"/> is pre-formatted (local time) here so the DataTemplate needs no value converter, this codebase's established "no converters" convention.</summary>
public sealed record DocumentDownloadLogItem(string DisplayName, string DocumentTitle, string DownloadedAtText);
