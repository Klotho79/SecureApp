using System.Text.Json;
using System.Text.Json.Nodes;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Cms;

/// <summary>Media chosen in the editor but not uploaded yet. Images keep their source bytes; videos stay on disk.</summary>
public sealed record PendingCmsMedia(string Kind, string FileName, string Mime, byte[]? Bytes = null, string? FilePath = null);

/// <summary>
/// Turns the editor's output into a stored <c>.cmsdoc</c> (CMS_PLAN.md, phase 2a): uploads new media to
/// the shared library as unlisted files (so they never clutter the library browser), builds the
/// <see cref="CmsDocument"/>, and imports it as a local document. Phase 2b will stage it as a library draft instead.
/// </summary>
public sealed class CmsDocumentSaver
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Why the last save could not stage its server draft (null = it did). The local copy is saved either way.</summary>
    public string? LastStageError { get; private set; }

    private readonly ISharedLibraryService _library;
    private readonly IDocumentImportService _import;
    private readonly IDocumentRepository _documents;
    private readonly ILibraryReviewService _review;

    public CmsDocumentSaver(ISharedLibraryService library, IDocumentImportService import, IDocumentRepository documents, ILibraryReviewService review)
    {
        _review = review ?? throw new ArgumentNullException(nameof(review));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _import = import ?? throw new ArgumentNullException(nameof(import));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    /// <param name="editorJson">Decoded <c>editor.getJson()</c>: <c>{ title, blocks }</c>.</param>
    /// <param name="original">The document being edited, or null for a new one.</param>
    /// <param name="originalLocalDocumentId">Local document replaced by this save (deleted after a successful import).</param>
    public async Task<Domain.Entities.Document> SaveAsync(
        string editorJson,
        CmsDocument? original,
        Guid? originalLocalDocumentId,
        IReadOnlyDictionary<string, PendingCmsMedia> pending,
        IProgress<string>? progress = null,
        Guid? assignedDocumentId = null,
        CancellationToken ct = default)
    {
        var root = JsonNode.Parse(editorJson) as JsonObject ?? throw new InvalidOperationException("Editor vrátil neplatná data.");
        var title = root["title"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Zadejte název dokumentu.");

        var blocks = JsonSerializer.Deserialize<List<CmsBlock>>(root["blocks"]?.ToJsonString() ?? "[]", Json) ?? [];

        var used = new HashSet<string>(StringComparer.Ordinal);
        CollectMediaIds(blocks, used);

        var media = new Dictionary<string, CmsMedia>(StringComparer.Ordinal);
        foreach (var id in used)
        {
            if (pending.TryGetValue(id, out var p))
            {
                progress?.Report($"Nahrávám {p.FileName}…");
                media[id] = await UploadAsync(p, ct);
            }
            else if (original?.Media.TryGetValue(id, out var existing) == true)
            {
                media[id] = existing;
            }
            else
            {
                throw new InvalidOperationException($"Chybí soubor pro médium {id}.");
            }
        }

        var version = "v-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var document = new CmsDocument(1, original?.DocumentId ?? assignedDocumentId ?? Guid.NewGuid(), version, original?.VersionId, title, "cs", blocks, media);

        progress?.Report("Ukládám dokument…");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        using var stream = new MemoryStream(bytes);
        var saved = await _import.ImportAsync(stream, SafeFileName(title) + ".cmsdoc", null, null, ct);

        if (originalLocalDocumentId is { } oldId && oldId != saved.Id)
            await _documents.DeleteAsync(oldId, ct);

        // The local copy is already safe; a relay problem must not make the author think the edit was lost.
        progress?.Report("Ukládám koncept na server…");
        try
        {
            await StageDraftAsync(document, bytes, ct);
            LastStageError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastStageError = ex.Message;
        }

        return saved;
    }

    /// <summary>The newest staged version of a server-side document (the editor's continuing draft), or null when none exists yet.</summary>
    public async Task<CmsDocument?> LoadLatestAsync(Guid documentId, CancellationToken ct = default)
    {
        try
        {
            var detail = await _review.GetDetailAsync(documentId, ct);
            var latest = detail.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (latest is null) return null;
            var (content, _) = await _library.DownloadDecryptedAsync(latest.LibraryFileId, ct);
            return JsonSerializer.Deserialize<CmsDocument>(content, Json);
        }
        catch (InvalidOperationException)
        {
            return null; // relay doesn't know the document yet
        }
    }

    /// <summary>Moves the staged draft of <paramref name="documentId"/> into the reviewer queue (readers only see a document after approval).</summary>
    public Task SubmitForReviewAsync(Guid documentId, CancellationToken ct = default) =>
        _review.SubmitForReviewAsync(documentId, ct);

    /// <summary>
    /// Uploads the serialized document as an unlisted library file and stages it as the document's next
    /// version. The library document id IS <see cref="CmsDocument.DocumentId"/>, so a revision needs no
    /// extra lookup: try to add a version, and when the relay doesn't know the document yet, create it.
    /// </summary>
    private async Task StageDraftAsync(CmsDocument document, byte[] bytes, CancellationToken ct)
    {
        using var stream = new MemoryStream(bytes);
        var uploaded = await _library.UploadAsync(LibraryFolder, SafeFileName(document.Title) + ".cmsdoc", ["cms"], stream, listed: false, ct: ct);
        try
        {
            await _review.AddVersionAsync(document.DocumentId, uploaded.Id, null, ct);
        }
        catch (InvalidOperationException)
        {
            await _review.CreateDraftAsync(document.Title, LibraryFolder, uploaded.Id, null, ct, document.DocumentId);
        }
    }

    private const string LibraryFolder = "Výukové materiály";

    private async Task<CmsMedia> UploadAsync(PendingCmsMedia p, CancellationToken ct)
    {
        if (p.Kind == "image")
        {
            var variants = new List<CmsMediaVariant>();
            foreach (var (width, jpeg) in CmsImageProcessor.MakeVariants(p.Bytes!))
            {
                using var s = new MemoryStream(jpeg);
                var name = $"{Path.GetFileNameWithoutExtension(p.FileName)}-{width}.jpg";
                var uploaded = await _library.UploadAsync("CMS média", name, ["cms"], s, listed: false, ct: ct);
                variants.Add(new CmsMediaVariant(width, uploaded.Id.ToString()));
            }
            return new CmsMedia("image", "image/jpeg", Variants: variants);
        }

        await using var file = File.OpenRead(p.FilePath!);
        var video = await _library.UploadAsync("CMS média", p.FileName, ["cms"], file, listed: false, ct: ct);
        return new CmsMedia("video", p.Mime, video.Id.ToString());
    }

    private static void CollectMediaIds(IEnumerable<CmsBlock> blocks, HashSet<string> into)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case CmsImageBlock i: into.Add(i.MediaId); break;
                case CmsVideoBlock v:
                    into.Add(v.MediaId);
                    if (v.PosterMediaId is { } poster) into.Add(poster);
                    break;
                case CmsRowBlock r:
                    foreach (var cell in r.Children) CollectMediaIds(cell.Blocks, into);
                    break;
            }
        }
    }

    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "dokument" : cleaned;
    }
}
