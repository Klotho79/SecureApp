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

    private readonly ISharedLibraryService _library;
    private readonly IDocumentImportService _import;
    private readonly IDocumentRepository _documents;

    public CmsDocumentSaver(ISharedLibraryService library, IDocumentImportService import, IDocumentRepository documents)
    {
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
        var document = new CmsDocument(1, original?.DocumentId ?? Guid.NewGuid(), version, original?.VersionId, title, "cs", blocks, media);

        progress?.Report("Ukládám dokument…");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        using var stream = new MemoryStream(bytes);
        var saved = await _import.ImportAsync(stream, SafeFileName(title) + ".cmsdoc", null, null, ct);

        if (originalLocalDocumentId is { } oldId && oldId != saved.Id)
            await _documents.DeleteAsync(oldId, ct);

        return saved;
    }

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
                case CmsVideoBlock v: into.Add(v.MediaId); break;
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
