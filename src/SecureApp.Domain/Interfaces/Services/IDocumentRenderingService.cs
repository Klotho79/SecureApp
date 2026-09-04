using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>Rasterizes a document page for on-screen display (SkiaSharp + HarfBuzz, in Presentation).</summary>
public interface IDocumentRenderingService
{
    Task<int> GetPageCountAsync(Guid documentId, CancellationToken ct = default);
    Task<RenderedPage> RenderPageAsync(Guid documentId, int pageIndex, int maxWidth, int maxHeight, CancellationToken ct = default);
}
