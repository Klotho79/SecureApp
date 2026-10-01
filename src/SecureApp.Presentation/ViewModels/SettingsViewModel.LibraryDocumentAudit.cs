using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Document Library approval-decision audit log search (2026-10-01) — admin-secret-gated, structural
/// twin of <c>SettingsViewModel.DocumentDownloads.cs</c>. The write side (recording an actual
/// approve/reject decision) lives on the relay (<c>POST /library-documents/{id}/review</c>), not here.
/// </summary>
public sealed partial class SettingsViewModel
{
    [ObservableProperty]
    public partial string LibraryDocumentAuditSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<LibraryDocumentAuditItem> LibraryDocumentAuditResults { get; set; } = [];

    [ObservableProperty]
    public partial bool HasNoLibraryDocumentAuditResults { get; set; }

    [ObservableProperty]
    public partial bool IsSearchingLibraryDocumentAudit { get; set; }

    [RelayCommand]
    private async Task SearchLibraryDocumentAuditAsync()
    {
        AdminErrorMessage = null;
        if (!Uri.TryCreate(RelayEndpointText, UriKind.Absolute, out var endpoint))
        {
            AdminErrorMessage = "Nejprve zadejte platnou adresu relay serveru výše.";
            return;
        }
        if (!TryTakeAdminSecret(out var adminSecret)) return;

        IsSearchingLibraryDocumentAudit = true;
        try
        {
            var results = await _relayAdminService.SearchLibraryDocumentAuditAsync(endpoint, adminSecret, LibraryDocumentAuditSearchText);
            LibraryDocumentAuditResults = new ObservableCollection<LibraryDocumentAuditItem>(results.Select(ToLibraryDocumentAuditItem));
            HasNoLibraryDocumentAuditResults = LibraryDocumentAuditResults.Count == 0;
        }
        catch (Exception ex)
        {
            AdminErrorMessage = $"Vyhledávání v logu schvalování se nezdařilo: {ex.Message}";
        }
        finally
        {
            IsSearchingLibraryDocumentAudit = false;
        }
    }

    private static LibraryDocumentAuditItem ToLibraryDocumentAuditItem(Domain.ValueObjects.LibraryDocumentReviewEntry entry) =>
        new(entry.Decision, entry.Comment, entry.DecidedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
}

/// <summary>One row of the Document Library audit search results — <see cref="DecidedAtText"/> is pre-formatted (local time), same "no converters" convention as <c>DocumentDownloadLogItem</c>.</summary>
public sealed record LibraryDocumentAuditItem(string Decision, string? Comment, string DecidedAtText);
