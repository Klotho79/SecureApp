#if WINDOWS
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace SecureApp.Presentation.Cms;

/// <summary>
/// Windows-only (2026-10-19, CMS phase 3 — see the matching Condition on the Xabe.FFmpeg
/// PackageReference in the .csproj for why that's safe to be Windows-only). Transcodes to MP4
/// H.264+AAC with faststart (streams instead of needing a full download first), scaled down — never
/// up — so the LARGER of width/height is at most <see cref="MaxDimension"/> while the source's own
/// orientation is preserved exactly: the scale filter picks which dimension to cap based on which is
/// already bigger, so 16:9, 21:9 and vertical 9:16 all come out undistorted (CMS_PLAN.md phase 3's
/// own "done when" line). ffmpeg/ffprobe are downloaded once into AppData and cached there; everything
/// is a temp file on disk because <see cref="CmsDocumentSaver"/> already expects pending video media
/// as a file path, not bytes (large videos should never sit fully in memory).
/// </summary>
public sealed class FfmpegCmsVideoProcessor : ICmsVideoProcessor
{
    private const int MaxDimension = 1280;
    private static readonly SemaphoreSlim EnsureGate = new(1, 1);
    private static bool _ready;

    public async Task<CmsProcessedVideo> ProcessAsync(string sourcePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await EnsureFFmpegAsync(progress, ct);

        var sourceInfo = await FFmpeg.GetMediaInfo(sourcePath, ct);
        var videoStream = sourceInfo.VideoStreams.FirstOrDefault()
            ?? throw new InvalidOperationException("Soubor neobsahuje obrazovou stopu.");
        var audioStream = sourceInfo.AudioStreams.FirstOrDefault();

        videoStream.SetCodec(VideoCodec.libx264);
        audioStream?.SetCodec(AudioCodec.aac);

        var outputPath = Path.Combine(Path.GetTempPath(), "cms-video-" + Guid.NewGuid().ToString("N") + ".mp4");
        var conversion = FFmpeg.Conversions.New()
            .AddStream(videoStream)
            // if(gte(iw,ih), ...): landscape/square caps width and lets height follow (-2 keeps it
            // even, required for yuv420p); portrait caps height and lets width follow. min(..) never
            // upscales a source already smaller than MaxDimension.
            .AddParameter($"-vf \"scale='if(gte(iw,ih),min({MaxDimension},iw),-2)':'if(gte(iw,ih),-2,min({MaxDimension},ih))'\"", ParameterPosition.PostInput)
            .AddParameter("-movflags +faststart", ParameterPosition.PostInput)
            .AddParameter("-pix_fmt yuv420p", ParameterPosition.PostInput)
            .SetOutput(outputPath)
            .SetOverwriteOutput(true);
        if (audioStream is not null) conversion.AddStream(audioStream);

        progress?.Report("Překóduji video…");
        conversion.OnProgress += (_, args) => progress?.Report($"Překóduji video… {args.Percent}%");
        await conversion.Start(ct);

        var outputInfo = await FFmpeg.GetMediaInfo(outputPath, ct);
        var outputVideo = outputInfo.VideoStreams.First();
        var aspectRatio = (double)outputVideo.Width / outputVideo.Height;
        var durationSec = Math.Max(1, (int)outputInfo.Duration.TotalSeconds);

        progress?.Report("Vytvářím náhledový obrázek…");
        var posterAt = outputInfo.Duration > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(1) : outputInfo.Duration / 2;
        var posterPath = Path.Combine(Path.GetTempPath(), "cms-poster-" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            var snapshot = await FFmpeg.Conversions.FromSnippet.Snapshot(outputPath, posterPath, posterAt);
            await snapshot.Start(ct);
            var posterBytes = await File.ReadAllBytesAsync(posterPath, ct);
            return new CmsProcessedVideo(outputPath, posterBytes, aspectRatio, durationSec);
        }
        finally
        {
            try { File.Delete(posterPath); } catch { /* best-effort temp cleanup */ }
        }
    }

    /// <summary>Downloads ffmpeg/ffprobe into AppData on first use only; every later call is a no-op (same process) or a cheap file existence check (new process).</summary>
    private static async Task EnsureFFmpegAsync(IProgress<string>? progress, CancellationToken ct)
    {
        if (_ready) return;
        await EnsureGate.WaitAsync(ct);
        try
        {
            if (_ready) return;
            var dir = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "ffmpeg");
            Directory.CreateDirectory(dir);
            FFmpeg.SetExecutablesPath(dir);
            if (!File.Exists(Path.Combine(dir, "ffmpeg.exe")) || !File.Exists(Path.Combine(dir, "ffprobe.exe")))
            {
                progress?.Report("Stahuji ffmpeg (jen poprvé)…");
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, dir);
            }
            _ready = true;
        }
        finally
        {
            EnsureGate.Release();
        }
    }
}
#endif
