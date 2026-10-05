using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Views;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// One sub-category's content (2026-10-02) — the files filed under it (<c>FolderPath</c> =
/// "{ParentCategory}/{Name}") plus any external links, reached by tapping a card on
/// <see cref="LibraryViewModel"/>'s browse grid. Upload/add-link/delete here all target THIS
/// sub-category specifically — unlike <see cref="LibraryManagePage"/>, which is the general-purpose
/// (uncategorized) upload entry point.
/// </summary>
public sealed partial class LibrarySubcategoryDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly ISharedLibraryService _libraryService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITransportSettingsRepository _transportSettingsRepository;
    private readonly IDiagnosticsReporter _diagnosticsReporter;
    private readonly ILibraryTranslationService _libraryTranslationService;

    private Guid _subcategoryId;
    private string _parentCategory = string.Empty;

    [ObservableProperty]
    public partial string SubcategoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<LibraryFileItem> Files { get; set; } = [];

    [ObservableProperty]
    public partial bool HasFiles { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<SubcategoryLinkItem> Links { get; set; } = [];

    [ObservableProperty]
    public partial bool HasLinks { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool CanModifyContent { get; set; }

    /// <summary>Admin-only, Windows-only local-AI PDF translation (2026-10-05) — see <see cref="LibraryViewModel.Actions.ComputeCanTranslateDocuments"/>'s own remarks for the exact gate; duplicated here rather than shared since this ViewModel has no MAUI-free/MAUI-touching split to push it into.</summary>
    [ObservableProperty]
    public partial bool CanTranslateDocuments { get; set; }

    [ObservableProperty]
    public partial bool IsTranslating { get; set; }

    [ObservableProperty]
    public partial double TranslationProgress { get; set; }

    [ObservableProperty]
    public partial string? TranslationStatusText { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    public LibrarySubcategoryDetailViewModel(
        ISharedLibraryService libraryService,
        ICurrentUserService currentUserService,
        ITransportSettingsRepository transportSettingsRepository,
        IDiagnosticsReporter diagnosticsReporter,
        ILibraryTranslationService libraryTranslationService)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _transportSettingsRepository = transportSettingsRepository ?? throw new ArgumentNullException(nameof(transportSettingsRepository));
        _diagnosticsReporter = diagnosticsReporter ?? throw new ArgumentNullException(nameof(diagnosticsReporter));
        _libraryTranslationService = libraryTranslationService ?? throw new ArgumentNullException(nameof(libraryTranslationService));
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("subcategoryId", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id))
            _subcategoryId = id;
        if (query.TryGetValue("parentCategory", out var parentValue))
            _parentCategory = Uri.UnescapeDataString(parentValue?.ToString() ?? string.Empty);
        if (query.TryGetValue("name", out var nameValue))
            SubcategoryName = Uri.UnescapeDataString(nameValue?.ToString() ?? string.Empty);
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    partial void OnHasFilesChanged(bool value) => IsEmpty = !value && !HasLinks;

    partial void OnHasLinksChanged(bool value) => IsEmpty = !value && !HasFiles;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusErrorMessage = null;
        CanModifyContent = RoleAccessPolicy.IsAllowed(_currentUserService.Current.Role, RbacAction.UploadLibraryFile);
        CanTranslateDocuments = _currentUserService.Current.Role == Role.Admin
            && Microsoft.Maui.Devices.DeviceInfo.Current.Platform == Microsoft.Maui.Devices.DevicePlatform.WinUI;
        try
        {
            var myDeviceId = (await _transportSettingsRepository.GetAsync())?.AssignedDeviceId;
            var folderPath = $"{_parentCategory}/{SubcategoryName}";

            var files = await _libraryService.SearchAsync(folderPath: folderPath);
            Files = new ObservableCollection<LibraryFileItem>(files.Select(f => ToFileItem(f, myDeviceId, CanTranslateDocuments)));
            HasFiles = Files.Count > 0;

            var links = await _libraryService.ListLinksAsync(_subcategoryId);
            Links = new ObservableCollection<SubcategoryLinkItem>(links.Select(l => ToLinkItem(l, myDeviceId)));
            HasLinks = Links.Count > 0;
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se načíst obsah: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (!CanModifyContent)
        {
            StatusErrorMessage = "Přidávat soubory může jen Admin nebo Modifier.";
            return;
        }

        FileResult? picked;
        try
        {
            picked = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Vyberte soubor" });
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se otevřít výběr souborů: {ex.Message}";
            return;
        }
        if (picked is null) return;

        StatusErrorMessage = null;
        try
        {
            await using var stream = await picked.OpenReadAsync();
            await _libraryService.UploadAsync($"{_parentCategory}/{SubcategoryName}", picked.FileName, [], stream);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se nahrát '{picked.FileName}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync(LibraryFileItem? item)
    {
        if (item is null) return;
        StatusErrorMessage = null;
        try
        {
            var document = await _libraryService.DownloadAndImportAsync(item.Id);
            await Shell.Current.GoToAsync($"{nameof(DocumentViewerPage)}?documentId={document.Id}", animate: false);
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se otevřít '{item.FileName}': {ex.Message}";
        }
    }

    /// <summary>Admin-only, Windows-only local-AI PDF translation (2026-10-05) — see <see cref="Translation.ILibraryTranslationService"/>'s own remarks for the full pipeline.</summary>
    [RelayCommand]
    private async Task TranslateAsync(LibraryFileItem? item)
    {
        if (item is null || !item.CanTranslate || IsTranslating) return;

        var targetLanguage = await (Shell.Current?.CurrentPage?.DisplayPromptAsync(
            "Přeložit dokument", "Cílový jazyk:", initialValue: "čeština") ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(targetLanguage)) return;

        IsTranslating = true;
        TranslationProgress = 0;
        TranslationStatusText = "Spouštím překlad…";
        StatusErrorMessage = null;
        try
        {
            var progress = new Progress<double>(p => TranslationProgress = p);
            var status = new Progress<string>(s => TranslationStatusText = s);
            var created = await _libraryTranslationService.TranslateAndSubmitAsync(
                item.Id, System.IO.Path.GetFileNameWithoutExtension(item.FileName),
                $"{_parentCategory}/{SubcategoryName}", targetLanguage.Trim(), progress, status);
            TranslationStatusText = $"Hotovo — koncept '{created.Title}' odeslán ke schválení.";
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Překlad '{item.FileName}' se nezdařil: {ex.Message}";
            TranslationStatusText = null;
        }
        finally
        {
            IsTranslating = false;
        }
    }

    [RelayCommand]
    private async Task DeleteFileAsync(LibraryFileItem? item)
    {
        if (item is null) return;
        try
        {
            await _libraryService.DeleteAsync(item.Id);
            Files = new ObservableCollection<LibraryFileItem>(Files.Where(f => f.Id != item.Id));
            HasFiles = Files.Count > 0;
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"'{item.FileName}' se nepodařilo smazat: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AddLinkAsync()
    {
        if (!CanModifyContent)
        {
            StatusErrorMessage = "Přidávat odkazy může jen Admin nebo Modifier.";
            return;
        }

        var title = await (Shell.Current?.CurrentPage?.DisplayPromptAsync("Nový odkaz", "Název (např. 'PubMed'):") ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(title)) return;
        var url = await (Shell.Current?.CurrentPage?.DisplayPromptAsync("Nový odkaz", "URL adresa:") ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(url)) return;

        StatusErrorMessage = null;
        try
        {
            await _libraryService.CreateLinkAsync(_subcategoryId, title.Trim(), url.Trim());
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se přidat odkaz '{title}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenLinkAsync(SubcategoryLinkItem? item)
    {
        if (item is null) return;
        try
        {
            await Launcher.OpenAsync(item.Url);
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Nepodařilo se otevřít odkaz '{item.Title}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteLinkAsync(SubcategoryLinkItem? item)
    {
        if (item is null) return;
        try
        {
            await _libraryService.DeleteLinkAsync(item.Id);
            Links = new ObservableCollection<SubcategoryLinkItem>(Links.Where(l => l.Id != item.Id));
            HasLinks = Links.Count > 0;
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"'{item.Title}' se nepodařilo smazat: {ex.Message}";
        }
    }

    private static LibraryFileItem ToFileItem(SharedLibraryFileSummary summary, Guid? myDeviceId, bool canTranslateDocuments)
    {
        var sizeAndDate = $"{FormatSize(summary.SizeBytes)} · Aktualizováno {summary.UploadedAtUtc.LocalDateTime:g}";
        var isMine = myDeviceId is { } id && string.Equals(summary.UploadedByDeviceId, id.ToString(), StringComparison.OrdinalIgnoreCase);
        var canTranslate = canTranslateDocuments && summary.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        return new LibraryFileItem(summary.Id, summary.FileName, null, false, [], false, sizeAndDate, isMine, canTranslate);
    }

    private static SubcategoryLinkItem ToLinkItem(LibraryLinkSummary summary, Guid? myDeviceId) => new(
        summary.Id,
        summary.Title,
        summary.Url,
        myDeviceId is { } id && string.Equals(summary.CreatedByDeviceId, id.ToString(), StringComparison.OrdinalIgnoreCase));

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}

/// <summary>One link row on <see cref="LibrarySubcategoryDetailViewModel"/>. <see cref="CanDelete"/> mirrors <see cref="LibraryFileItem.IsMine"/> — only the creator can delete via the ordinary device-authed call.</summary>
public sealed record SubcategoryLinkItem(Guid Id, string Title, string Url, bool CanDelete);
