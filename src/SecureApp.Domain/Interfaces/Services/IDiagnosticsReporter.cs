using SecureApp.Domain.Enums;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Domain.Interfaces.Services;

/// <summary>
/// Client for the relay's shared diagnostics log (2026-09-10) — the user's own direct ask, made
/// explicit: the whole point of the previous day's WireGuard/tunnel work was to be able to work with
/// an AI assistant instead of having to physically sit at PC + S9+ + S23+ simultaneously to debug a
/// cross-device issue. A per-device local log nobody can see from anywhere else defeats that — this
/// gives every device a single shared place to report a problem to, and lets any device (or an
/// operator with SSH into the relay) read the whole community's recent errors in one place, without
/// needing an admin secret (same device-authenticated, not admin-gated, reasoning as
/// <see cref="IContactDirectoryService"/>/<see cref="ISharedLibraryService"/> — a device that's
/// already on this relay is exactly who this should be visible to).
///
/// <see cref="ReportAsync"/> is deliberately best-effort and MUST NEVER throw or block its caller —
/// implementations swallow their own failures internally (network down, relay unreachable, not yet
/// registered) — since it's called from the exact failure-handling paths (catch blocks, a global
/// unhandled-exception hook) that can least afford a reporting call to introduce a NEW exception.
/// </summary>
public interface IDiagnosticsReporter
{
    /// <summary>Fire-and-forget from the caller's perspective — safe to call without awaiting, and safe to await too (it never throws either way). <paramref name="context"/> is typically the calling class/method name, e.g. "ChatViewModel.SendAsync".</summary>
    Task ReportAsync(DiagnosticLogLevel level, string message, string? context = null, Exception? exception = null, CancellationToken ct = default);

    /// <summary>Most recent entries across the whole community, newest first. Throws on a genuine failure (unlike <see cref="ReportAsync"/>) — a viewer explicitly asking to see the log should see why it failed, not a silently empty list.</summary>
    Task<IReadOnlyList<DiagnosticLogEntry>> GetRecentAsync(int limit = 100, CancellationToken ct = default);
}
