using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Lists the folders and documents inside the current folder (root when
/// <see cref="_currentFolderId"/> is null) and lets the user navigate deeper, go back up,
/// and manage folders. Split across two files: this one is deliberately free of any MAUI
/// type (only Domain interfaces + CommunityToolkit.Mvvm, neither of which need a running
/// app host), so it's directly testable from a plain console app; <see cref="DocumentBrowserViewModel"/>'s
/// other partial (<c>DocumentBrowserViewModel.Actions.cs</c>) holds the two commands that
/// genuinely need MAUI (<c>FilePicker</c> for import, <c>Shell</c> for navigating to the viewer).
/// </summary>
public sealed partial class DocumentBrowserViewModel : ObservableObject
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentFolderRepository _folderRepository;
    private readonly IDocumentImportService _importService;
    private readonly ICurrentUserService _currentUserService;

    private readonly Stack<(Guid? FolderId, string FolderName)> _breadcrumb = new();
    private Guid? _currentFolderId;

    [ObservableProperty]
    public partial ObservableCollection<DocumentFolderItem> Folders { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DocumentItem> Documents { get; set; }

    [ObservableProperty]
    public partial string CurrentFolderName { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool CanGoUp { get; set; }

    [ObservableProperty]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    public partial bool CanImport { get; set; }

    /// <summary>
    /// RBAC gate (Milestone 3, Task 3.2/3.3) for structural/content-mutating actions —
    /// create/rename/delete folder, import. False for <see cref="Domain.Enums.Role.Viewer"/>.
    /// Recomputed on every <see cref="LoadAsync"/>, so it stays current after a role change
    /// made via SettingsPage without needing an event subscription to a singleton service.
    /// </summary>
    [ObservableProperty]
    public partial bool CanModifyContent { get; set; }

    [ObservableProperty]
    public partial string? StatusErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool HasStatusError { get; set; }

    public DocumentBrowserViewModel(
        IDocumentRepository documentRepository,
        IDocumentFolderRepository folderRepository,
        IDocumentImportService importService,
        ICurrentUserService currentUserService)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _folderRepository = folderRepository ?? throw new ArgumentNullException(nameof(folderRepository));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

        Folders = [];
        Documents = [];
        CurrentFolderName = "Documents";
        CanImport = true;
        CanModifyContent = true;
    }

    partial void OnStatusErrorMessageChanged(string? value) => HasStatusError = !string.IsNullOrEmpty(value);

    partial void OnIsImportingChanged(bool value) => CanImport = !value;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _currentUserService.InitializeAsync();
            CanModifyContent = RoleAccessPolicy.IsAllowed(_currentUserService.Current.Role, RbacAction.CreateFolder);

            var folderEntities = await _folderRepository.GetChildrenAsync(_currentFolderId);
            var documentEntities = await _documentRepository.GetByFolderAsync(_currentFolderId);

            Folders = new ObservableCollection<DocumentFolderItem>(
                folderEntities.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(f => new DocumentFolderItem(f.Id, f.Name)));

            Documents = new ObservableCollection<DocumentItem>(
                documentEntities.OrderBy(d => d.Title, StringComparer.CurrentCultureIgnoreCase)
                    .Select(d => new DocumentItem(d.Id, d.Title, d.DocumentType, d.IsFavorite, IconFor(d.DocumentType))));

            IsEmpty = Folders.Count == 0 && Documents.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync(DocumentFolderItem? folder)
    {
        if (folder is null) return;

        _breadcrumb.Push((_currentFolderId, CurrentFolderName));
        _currentFolderId = folder.Id;
        CurrentFolderName = folder.Name;
        CanGoUp = true;

        await LoadAsync();
    }

    [RelayCommand]
    private async Task GoUpAsync()
    {
        if (_breadcrumb.Count == 0) return;

        (_currentFolderId, CurrentFolderName) = _breadcrumb.Pop();
        CanGoUp = _breadcrumb.Count > 0;

        await LoadAsync();
    }

    [RelayCommand]
    private async Task CreateFolderAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!CanModifyContent)
        {
            StatusErrorMessage = "Your role (Viewer) cannot create folders.";
            return;
        }

        try
        {
            var folder = new DocumentFolder(name, _currentFolderId);
            await _folderRepository.AddAsync(folder);
            StatusErrorMessage = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Could not create folder '{name}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameFolderAsync((DocumentFolderItem Folder, string NewName) args)
    {
        if (string.IsNullOrWhiteSpace(args.NewName) || args.NewName == args.Folder.Name) return;
        if (!CanModifyContent)
        {
            StatusErrorMessage = "Your role (Viewer) cannot rename folders.";
            return;
        }

        try
        {
            var entity = await _folderRepository.GetByIdAsync(args.Folder.Id);
            if (entity is null) return;

            entity.Rename(args.NewName);
            await _folderRepository.UpdateAsync(entity);
            StatusErrorMessage = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Could not rename folder '{args.Folder.Name}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(DocumentFolderItem? folder)
    {
        if (folder is null) return;
        if (!CanModifyContent)
        {
            StatusErrorMessage = "Your role (Viewer) cannot delete folders.";
            return;
        }

        try
        {
            await _folderRepository.DeleteAsync(folder.Id);
            StatusErrorMessage = null;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusErrorMessage = $"Could not delete folder '{folder.Name}': {ex.Message}";
        }
    }

    private static string IconFor(DocumentType documentType) => documentType switch
    {
        DocumentType.Pdf => "📕",
        DocumentType.Image => "🖼",
        DocumentType.Spreadsheet => "📊",
        DocumentType.PlainText => "📄",
        _ => "📦"
    };
}

public sealed record DocumentFolderItem(Guid Id, string Name);

public sealed record DocumentItem(Guid Id, string Title, DocumentType DocumentType, bool IsFavorite, string Icon);
