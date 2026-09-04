namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Platform-specific screen-capture / screen-recording prevention (Milestone 4, Task 4.3).
/// Applied once, app-wide, at startup — not toggled per-page (matches the original spec's
/// wording, e.g. "FLAG_SECURE in MainActivity"), see DEVELOPMENT_PLAN.md's Milestone 4 note.
/// Each platform implements this in its own <c>Platforms/&lt;Platform&gt;/NativeDlpService.cs</c>
/// (same type name/namespace in each folder — only the one matching the active target
/// framework is compiled in, the same pattern <c>MainActivity</c>/<c>AppDelegate</c> already use).
/// </summary>
public interface INativeDlpService
{
    /// <summary>Best-effort. Platforms with no supported mechanism (or nothing meaningful to do) no-op.</summary>
    void PreventScreenCapture();
}
