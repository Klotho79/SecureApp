using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="INativeDlpService"/>
/// <remarks>
/// Deliberate no-op. The original spec's Task 4.3 only names Android, iOS, and Windows —
/// MacCatalyst is an extra target this solution happens to build for (see the architecture
/// tree in DEVELOPMENT_PLAN.md) but was never in scope for DLP. iOS's UITextField/CALayer
/// trick (see the iOS NativeDlpService's remarks) isn't guaranteed to behave the same under
/// Mac Catalyst's windowing, and macOS's own screenshot/screen-recording model differs enough
/// from iOS's that guessing at an untested equivalent seemed worse than an honest no-op.
/// Revisit if MacCatalyst is ever actually targeted for release.
/// </remarks>
public sealed class NativeDlpService : INativeDlpService
{
    public void PreventScreenCapture()
    {
        // Intentionally empty — see remarks above.
    }
}
