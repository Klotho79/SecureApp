using System.Text;
using System.Text.Json;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.Policies;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Cms;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Views;

/// <summary>
/// PC authoring screen for CMS documents (CMS_PLAN.md, phase 2a). The editing surface is a local HTML
/// page in a WebView (<c>Resources/Raw/cms-editor.html</c>); everything that needs the app — file pickers,
/// image variants, media upload, saving — lives here, driven by calling <c>editor.*</c> in the page.
/// Until per-document editor grants exist (phase 2c) anyone allowed to upload to the library may edit.
/// </summary>
public partial class CmsEditorPage : ContentPage, IQueryAttributable
{
    private static readonly (string Label, int Width)[] PreviewSizes =
        [("Plná šířka", 0), ("Telefon 360", 360), ("Telefon 390", 390), ("Telefon 430", 430), ("Tablet 768", 768)];

    private readonly IDocumentRepository _documents;
    private readonly ICryptoService _crypto;
    private readonly ICurrentUserService _currentUser;
    private readonly RolePreviewService _rolePreview;
    private readonly ICmsMediaProvider _mediaProvider;
    private readonly CmsDocumentSaver _saver;

    private readonly Dictionary<string, PendingCmsMedia> _pending = new();
    private Guid? _localDocumentId;
    private Guid? _assignedDocumentId;
    private string? _assignedTitle;
    private CmsDocument? _original;
    private bool _loaded;
    private bool _saving;

    public CmsEditorPage(
        IDocumentRepository documents, ICryptoService crypto, ICurrentUserService currentUser,
        RolePreviewService rolePreview, ICmsMediaProvider mediaProvider, CmsDocumentSaver saver)
    {
        InitializeComponent();
        _documents = documents;
        _crypto = crypto;
        _currentUser = currentUser;
        _rolePreview = rolePreview;
        _mediaProvider = mediaProvider;
        _saver = saver;

        PreviewPicker.ItemsSource = PreviewSizes.Select(s => s.Label).ToList();
        PreviewPicker.SelectedIndex = 0;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("documentId", out var value) && Guid.TryParse(value?.ToString(), out var id))
            _localDocumentId = id;
        // "Moje zadání": the document id and title come from the editor grant (CMS_PLAN.md, phase 2c).
        if (query.TryGetValue("assignedDocumentId", out var assigned) && Guid.TryParse(assigned?.ToString(), out var assignedId))
        {
            _assignedDocumentId = assignedId;
            _assignedTitle = query.TryGetValue("assignedTitle", out var t) ? Uri.UnescapeDataString(t?.ToString() ?? "") : null;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;

        await _currentUser.InitializeAsync();
        var role = _rolePreview.Resolve(_currentUser.Current.Role);
        // An assigned editor (even a Viewer) may open the editor; the relay enforces the actual grant.
        if (_assignedDocumentId is null && !RoleAccessPolicy.IsAllowed(role, RbacAction.UploadLibraryFile))
        {
            await DisplayAlertAsync("Editor", "Vaše role nemůže vytvářet ani upravovat dokumenty.", "OK");
            await Shell.Current.GoToAsync("..");
            return;
        }

        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("cms-editor.html");
            using var reader = new StreamReader(stream);
            Editor.Source = new HtmlWebViewSource { Html = await reader.ReadToEndAsync() };
        }
        catch (Exception ex)
        {
            SetStatus($"Editor se nepodařilo načíst: {ex.Message}", error: true);
        }
    }

    private async void OnEditorNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (_loaded || e.Result != WebNavigationResult.Success) return;
        _loaded = true;

        try
        {
            if (_assignedDocumentId is { } assigned)
                await LoadAssignmentAsync(assigned, _assignedTitle ?? "Nový dokument");
            else if (_localDocumentId is { } id)
                await LoadExistingAsync(id);
        }
        catch (Exception ex)
        {
            SetStatus($"Dokument se nepodařilo otevřít: {ex.Message}", error: true);
        }
    }

    private async Task LoadExistingAsync(Guid id)
    {
        SetStatus("Načítám dokument…");
        var document = await _documents.GetByIdAsync(id) ?? throw new InvalidOperationException("Dokument nenalezen.");
        var plaintext = await _crypto.DecryptAsync(document.EncryptedContent);
        _original = JsonSerializer.Deserialize<CmsDocument>(plaintext, CmsDocumentSaver.Json)
            ?? throw new InvalidOperationException("Dokument má neplatný obsah.");
        await ShowOriginalAsync();
    }

    /// <summary>Opens an assignment: continues the latest server draft when one exists, otherwise starts an empty document with the assigned title.</summary>
    private async Task LoadAssignmentAsync(Guid documentId, string title)
    {
        SetStatus("Načítám zadání…");
        _original = await _saver.LoadLatestAsync(documentId);
        if (_original is not null)
        {
            await ShowOriginalAsync();
            return;
        }

        await RunJsAsync($"editor.load({Js(JsonSerializer.Serialize(new { title, blocks = Array.Empty<object>() }, CmsDocumentSaver.Json))})");
        SetStatus(string.Empty);
    }

    private async Task ShowOriginalAsync()
    {
        if (_original is null) return;

        foreach (var (mediaId, media) in _original.Media.Where(m => m.Value.Kind == "image"))
        {
            try
            {
                var path = await _mediaProvider.GetImagePathAsync(media, 960);
                if (path is null) continue;
                var bytes = await File.ReadAllBytesAsync(path);
                await RunJsAsync($"editor.addPreview({Js(mediaId)}, {Js("data:image/jpeg;base64," + Convert.ToBase64String(bytes))})");
            }
            catch (Exception ex)
            {
                AppLog.Error("CmsEditor.Load", $"preview of {mediaId} failed", ex);
            }
        }

        var model = JsonSerializer.Serialize(new { title = _original.Title, blocks = _original.Blocks }, CmsDocumentSaver.Json);
        await RunJsAsync($"editor.load({Js(model)})");
        SetStatus(string.Empty);
    }

    private async void OnInsertImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var file = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Vyberte obrázek", FileTypes = FilePickerFileType.Images });
            if (file is null) return;

            await using var source = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            var prepared = await Task.Run(() => CmsImageProcessor.Prepare(bytes));
            var id = "m-" + Guid.NewGuid().ToString("N")[..8];
            _pending[id] = new PendingCmsMedia("image", file.FileName, "image/jpeg", Bytes: bytes);

            var url = "data:image/jpeg;base64," + Convert.ToBase64String(prepared.PreviewJpeg);
            await RunJsAsync($"editor.insertImage({Js(id)}, {Js(url)}, {prepared.AspectRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        }
        catch (Exception ex)
        {
            SetStatus($"Obrázek se nepodařilo vložit: {ex.Message}", error: true);
        }
    }

    private async void OnInsertVideoClicked(object? sender, EventArgs e)
    {
        try
        {
            var types = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".mp4", ".m4v", ".mov", ".webm"],
                [DevicePlatform.Android] = ["video/*"],
                [DevicePlatform.iOS] = ["public.movie"],
                [DevicePlatform.MacCatalyst] = ["public.movie"],
            });
            var file = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Vyberte video", FileTypes = types });
            if (file is null) return;

            var id = "m-" + Guid.NewGuid().ToString("N")[..8];
            var mime = Path.GetExtension(file.FileName).ToLowerInvariant() == ".webm" ? "video/webm" : "video/mp4";
            _pending[id] = new PendingCmsMedia("video", file.FileName, mime, FilePath: file.FullPath);

            await RunJsAsync($"editor.insertVideo({Js(id)}, {Js(file.FileName)}, 1.7778)");
            SetStatus("Video se nahrává beze změny (bez překódování) — poměr stran nastavte v nástrojích bloku.");
        }
        catch (Exception ex)
        {
            SetStatus($"Video se nepodařilo vložit: {ex.Message}", error: true);
        }
    }

    private async void OnPreviewChanged(object? sender, EventArgs e)
    {
        if (!_loaded || PreviewPicker.SelectedIndex < 0) return;
        await RunJsAsync($"editor.setPreview({PreviewSizes[PreviewPicker.SelectedIndex].Width})");
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_saving) return;
        _saving = true;
        SaveButton.IsEnabled = false;
        try
        {
            var encoded = (await Editor.EvaluateJavaScriptAsync("editor.getJson()"))?.Trim('"');
            if (string.IsNullOrEmpty(encoded)) throw new InvalidOperationException("Editor nevrátil žádná data.");
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));

            var progress = new Progress<string>(m => SetStatus(m));
            var saved = await _saver.SaveAsync(json, _original, _localDocumentId, _pending, progress, _assignedDocumentId);

            _localDocumentId = saved.Id;
            _pending.Clear();
            _original = JsonSerializer.Deserialize<CmsDocument>(await _crypto.DecryptAsync(saved.EncryptedContent), CmsDocumentSaver.Json);
            SetStatus(_saver.LastStageError is { } stageError
                ? $"Uloženo jen v tomto počítači ({DateTime.Now:HH:mm:ss}) — koncept na server se nepodařilo uložit: {stageError}"
                : $"Uloženo jako koncept ({DateTime.Now:HH:mm:ss}). Čtenáři ho uvidí po „Odeslat ke schválení“ a schválení.",
                error: _saver.LastStageError is not null);
            SubmitButton.IsEnabled = _saver.LastStageError is null && _original is not null;
        }
        catch (Exception ex)
        {
            AppLog.Error("CmsEditor.Save", "save failed", ex);
            SetStatus($"Uložení se nezdařilo: {ex.Message}", error: true);
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
        }
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        if (_original is null) return;
        if (!await DisplayAlertAsync("Odeslat ke schválení", "Dokument půjde ke schválení a po schválení ho uvidí ostatní. Pokračovat?", "Odeslat", "Zrušit")) return;
        SubmitButton.IsEnabled = false;
        try
        {
            await _saver.SubmitForReviewAsync(_original.DocumentId);
            SetStatus("Odesláno ke schválení.");
        }
        catch (Exception ex)
        {
            AppLog.Error("CmsEditor.Submit", "submit failed", ex);
            SetStatus($"Odeslání se nezdařilo: {ex.Message}", error: true);
            SubmitButton.IsEnabled = true;
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (!await DisplayAlertAsync("Zavřít editor", "Neuložené změny budou ztraceny. Zavřít?", "Zavřít", "Zůstat")) return;
        await Shell.Current.GoToAsync("..");
    }

    private Task<string?> RunJsAsync(string script) => Editor.EvaluateJavaScriptAsync(script);

    private static string Js(string value) => JsonSerializer.Serialize(value);

    private void SetStatus(string message, bool error = false)
    {
        StatusLabel.Text = message;
        StatusLabel.TextColor = error ? Colors.IndianRed : Colors.Gray;
    }
}
