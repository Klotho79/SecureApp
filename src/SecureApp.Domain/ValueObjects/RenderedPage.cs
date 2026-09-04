namespace SecureApp.Domain.ValueObjects;

/// <summary>A single rasterized page/frame, ready for on-screen display by the Presentation renderer.</summary>
public sealed record RenderedPage(byte[] PixelData, int Width, int Height);
