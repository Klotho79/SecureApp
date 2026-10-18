using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Cms;

/// <summary>Supplies decrypted media for CMS blocks; implementations cache on disk so a repeat open never re-downloads.</summary>
public interface ICmsMediaProvider
{
    /// <summary>Local file path of the best image variant for <paramref name="targetWidthPx"/>, or null if unavailable.</summary>
    Task<string?> GetImagePathAsync(CmsMedia media, int targetWidthPx, CancellationToken ct = default);

    Task<string?> GetVideoPathAsync(CmsMedia media, CancellationToken ct = default);
}

public sealed class LibraryCmsMediaProvider : ICmsMediaProvider
{
    private readonly ISharedLibraryService _library;
    private readonly string _cacheDir = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, "cms-media");

    public LibraryCmsMediaProvider(ISharedLibraryService library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
    }

    public Task<string?> GetImagePathAsync(CmsMedia media, int targetWidthPx, CancellationToken ct = default)
    {
        string? id = media.LibraryFileId;
        if (media.Variants is { Count: > 0 })
        {
            // Smallest variant that still covers the target width; else the largest available.
            var ordered = media.Variants.OrderBy(v => v.W).ToList();
            id = (ordered.FirstOrDefault(v => v.W >= targetWidthPx) ?? ordered[^1]).LibraryFileId;
        }
        return FetchAsync(id, ct);
    }

    public Task<string?> GetVideoPathAsync(CmsMedia media, CancellationToken ct = default)
        => FetchAsync(media.LibraryFileId, ct);

    private async Task<string?> FetchAsync(string? libraryFileId, CancellationToken ct)
    {
        if (!Guid.TryParse(libraryFileId, out var id)) return null;

        Directory.CreateDirectory(_cacheDir);
        var existing = Directory.EnumerateFiles(_cacheDir, id.ToString("N") + ".*").FirstOrDefault();
        if (existing is not null) return existing;

        var (content, fileName) = await _library.DownloadDecryptedAsync(id, ct);
        var path = Path.Combine(_cacheDir, id.ToString("N") + Path.GetExtension(fileName));
        await File.WriteAllBytesAsync(path, content, ct);
        return path;
    }
}
