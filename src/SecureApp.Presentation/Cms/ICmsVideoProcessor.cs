namespace SecureApp.Presentation.Cms;

/// <summary>Result of transcoding an author-picked video (CMS_PLAN.md, phase 3): a streamable MP4 the
/// reader downloads, a poster frame to show before the reader taps play, and the real aspect ratio and
/// duration read back off that same output file (not the source) so a phone's rotation metadata — which
/// FFmpeg already applied to the output pixels — is reflected correctly.</summary>
public sealed record CmsProcessedVideo(string OutputPath, byte[] PosterJpeg, double AspectRatio, int DurationSec);

/// <summary>Turns whatever video the author picked into something the reader can actually stream:
/// MP4 H.264+AAC with faststart, scaled down (never up) to a sane size, plus a poster. Windows-only in
/// practice (the CMS editor itself is PC-only) — see <c>FfmpegCmsVideoProcessor</c>'s own remarks.</summary>
public interface ICmsVideoProcessor
{
    Task<CmsProcessedVideo> ProcessAsync(string sourcePath, IProgress<string>? progress = null, CancellationToken ct = default);
}
