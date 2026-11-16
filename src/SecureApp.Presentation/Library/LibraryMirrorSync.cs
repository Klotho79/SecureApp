using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Exceptions;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Cms;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation.Library;

/// <summary>
/// Keeps Soubory → Dokumenty a mirror of the relay's library (2026-11-16, user: "v každém zařízení má být
/// to co na relay a přesně tolikrát" — a new member's Dokumenty was empty, the home PC had Blok N.Femoralis
/// three times, one per version ever opened). The root of Dokumenty holds exactly the relay's listed files,
/// each once, always the currently approved version, downloaded ahead so they open offline too. Everything
/// else that used to sit there — own imports, the author's editor drafts, opened chat attachments — is moved
/// into the folder <see cref="PersonalFolderName"/> (the user's call: "Nechat ve složce Moje"), never deleted.
/// The only things ever deleted are copies that can't lose anything:
/// <list type="bullet">
/// <item>a second local copy of the same listed file;</item>
/// <item>a library copy of an older (or pending) version of a CMS document whose approved version is listed —
/// every version stays on the relay;</item>
/// <item>an author's editor copy whose <see cref="CmsDocument.VersionId"/> equals a library copy's — the very
/// same save, so its content is on the relay.</item>
/// </list>
/// </summary>
public sealed class LibraryMirrorSync
{
    public const string PersonalFolderName = "Moje";

    /// <summary>Bigger files wait for Wi-Fi/Ethernet instead of eating mobile data.</summary>
    private const long MeteredDownloadLimitBytes = 25L * 1024 * 1024;

    private readonly ISharedLibraryService _library;
    private readonly ICryptoService _crypto;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRunUtc = DateTimeOffset.MinValue;

    public LibraryMirrorSync(ISharedLibraryService library, ICryptoService crypto, IServiceScopeFactory scopeFactory)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <summary>
    /// Brings the local documents in line with the relay, unless the last completed run is younger than
    /// <paramref name="minInterval"/>. Returns true when anything local changed. Never throws; offline or
    /// without the library key it simply does nothing. Runs one at a time — a second caller waits and then
    /// usually finds nothing left to do.
    /// </summary>
    public async Task<bool> SyncAsync(TimeSpan minInterval, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow - _lastRunUtc < minInterval)
                return false;
            return await RunAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return false; // offline / relay unreachable — next time
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Error(nameof(LibraryMirrorSync), "library mirror sync failed", ex);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The root folder <see cref="PersonalFolderName"/>, created on first use.</summary>
    public static async Task<Guid> EnsurePersonalFolderAsync(IDocumentFolderRepository folders, CancellationToken ct = default)
    {
        var existing = (await folders.GetChildrenAsync(null, ct))
            .FirstOrDefault(f => string.Equals(f.Name, PersonalFolderName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing.Id;

        var created = new DocumentFolder(PersonalFolderName);
        await folders.AddAsync(created, ct);
        return created.Id;
    }

    private async Task<bool> RunAsync(CancellationToken ct)
    {
        if (!await _library.HasSharedKeyAsync(ct))
            return false; // nothing could be decrypted yet — the key arrives on its own (SharedLibraryKeySync)

        var listed = await _library.SearchAsync(ct: ct);
        var listedIds = listed.Select(f => f.Id).ToHashSet();
        _lastRunUtc = DateTimeOffset.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var folders = scope.ServiceProvider.GetRequiredService<IDocumentFolderRepository>();
        var changes = new Changes();

        await EnsureListedFilesAsync(documents, listed, changes, ct);
        await ClearRootAsync(documents, folders, listedIds, changes, ct);

        if (changes.Any)
            AppLog.Event("library.mirror", ("downloaded", changes.Downloaded), ("removed", changes.Removed), ("movedToMine", changes.Moved));
        return changes.Any;
    }

    /// <summary>Every listed file exactly once, at the root.</summary>
    private async Task EnsureListedFilesAsync(IDocumentRepository documents, IReadOnlyList<SharedLibraryFileSummary> listed, Changes changes, CancellationToken ct)
    {
        var root = await documents.GetInfosByFolderAsync(null, ct);
        var rootBySource = root.Where(d => d.SourceLibraryFileId is not null)
            .GroupBy(d => d.SourceLibraryFileId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var extra in rootBySource.Values.SelectMany(copies => copies.Skip(1)))
            changes.Removed += await TryDeleteAsync(documents, extra.Id, ct);

        foreach (var file in listed.Where(f => !rootBySource.ContainsKey(f.Id)))
        {
            // Already downloaded but filed somewhere else (e.g. moved by hand) — it belongs at the root.
            if (await documents.GetBySourceLibraryFileIdAsync(file.Id, ct) is { } elsewhere)
            {
                elsewhere.MoveToFolder(null);
                await documents.UpdateAsync(elsewhere, ct);
                changes.Moved++;
                continue;
            }

            if (file.SizeBytes > MeteredDownloadLimitBytes && !IsOnUnmeteredNetwork())
                continue;

            try
            {
                await _library.DownloadAndImportAsync(file.Id, null, ct);
                changes.Downloaded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Warning(nameof(LibraryMirrorSync), $"downloading '{file.FileName}' failed, retried next run", ex);
            }
        }
    }

    /// <summary>Anything at the root that isn't a listed file: a superseded CMS copy goes, everything else moves to "Moje".</summary>
    private async Task ClearRootAsync(IDocumentRepository documents, IDocumentFolderRepository folders, HashSet<Guid> listedIds, Changes changes, CancellationToken ct)
    {
        var root = await documents.GetInfosByFolderAsync(null, ct);
        bool IsListed(DocumentInfo d) => d.SourceLibraryFileId is { } source && listedIds.Contains(source);

        var strays = root.Where(d => !IsListed(d)).ToList();
        var rootFolders = (await folders.GetChildrenAsync(null, ct))
            .Where(f => !string.Equals(f.Name, PersonalFolderName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (strays.Count == 0 && rootFolders.Count == 0)
            return;

        // CMS documents are a few KB each — read them all once to know which document/version each copy holds.
        var cms = new Dictionary<Guid, CmsDocument>();
        foreach (var info in root.Where(d => d.DocumentType == DocumentType.Cms))
        {
            if (await documents.GetByIdAsync(info.Id, ct) is { } document && await TryParseAsync(document, ct) is { } model)
                cms[info.Id] = model;
        }

        var listedCmsDocuments = root.Where(d => IsListed(d) && cms.ContainsKey(d.Id)).Select(d => cms[d.Id].DocumentId).ToHashSet();
        var libraryVersions = root.Where(d => d.SourceLibraryFileId is not null && cms.ContainsKey(d.Id))
            .Select(d => (cms[d.Id].DocumentId, cms[d.Id].VersionId))
            .ToHashSet();

        Guid? personal = null;
        foreach (var stray in strays)
        {
            if (cms.TryGetValue(stray.Id, out var model) && listedCmsDocuments.Contains(model.DocumentId)
                && (stray.SourceLibraryFileId is not null || libraryVersions.Contains((model.DocumentId, model.VersionId))))
            {
                changes.Removed += await TryDeleteAsync(documents, stray.Id, ct);
                continue;
            }

            if (await documents.GetByIdAsync(stray.Id, ct) is not { } document)
                continue;
            personal ??= await EnsurePersonalFolderAsync(folders, ct);
            document.MoveToFolder(personal);
            await documents.UpdateAsync(document, ct);
            changes.Moved++;
        }

        foreach (var folder in rootFolders)
        {
            personal ??= await EnsurePersonalFolderAsync(folders, ct);
            folder.MoveTo(personal);
            await folders.UpdateAsync(folder, ct);
            changes.Moved++;
        }
    }

    private async Task<CmsDocument?> TryParseAsync(Document document, CancellationToken ct)
    {
        try
        {
            return CmsDocumentSaver.Parse(await _crypto.DecryptAsync(document.EncryptedContent, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // unreadable — treated like any other document (moved, never deleted)
        }
    }

    private static async Task<int> TryDeleteAsync(IDocumentRepository documents, Guid id, CancellationToken ct)
    {
        try
        {
            await documents.DeleteAsync(id, ct);
            return 1;
        }
        catch (DocumentNotFoundException)
        {
            return 0; // already gone
        }
    }

    private static bool IsOnUnmeteredNetwork()
    {
        try
        {
            var profiles = Microsoft.Maui.Networking.Connectivity.Current.ConnectionProfiles;
            return profiles.Contains(Microsoft.Maui.Networking.ConnectionProfile.WiFi)
                || profiles.Contains(Microsoft.Maui.Networking.ConnectionProfile.Ethernet);
        }
        catch
        {
            return true; // platform can't tell — don't hold files back forever
        }
    }

    private sealed class Changes
    {
        public int Downloaded;
        public int Removed;
        public int Moved;
        public bool Any => Downloaded + Removed + Moved > 0;
    }
}
