using SecureApp.Domain.Interfaces.Services;
using UIKit;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="INativeDlpService"/>
/// <remarks>
/// iOS has no official API to block screenshots/screen recording the way Android's
/// FLAG_SECURE does. This uses the well-known but unofficial "secure text field" trick (as
/// named in the original spec): a <c>UITextField(isSecureTextEntry: true)</c> is added to the
/// key window, and the *window's own layer* is re-parented underneath that field's secure
/// layer — anything that captures the window's contents (screenshot, screen recording,
/// AirPlay/QuickTime mirroring) ends up capturing the secure layer instead and comes out
/// blank. Fragile: depends on CALayer composition behavior Apple has never documented and
/// could change in a future iOS release.
///
/// RUNTIME-UNVERIFIED: this dev environment is Windows-only with no Mac build host, but
/// `dotnet build -f net10.0-ios` does succeed here (it only needs a Mac for native
/// AOT-linked publish/device deployment, not a plain IL compile) — confirmed clean, 0
/// warnings, against the actual installed net10.0-ios binding surface. What's NOT confirmed
/// is that it does anything at runtime (no simulator/device available here) — see
/// DEVELOPMENT_PLAN.md's Milestone 6 note on the separate, unrelated PDFtoImage iOS packaging
/// gap for the same underlying limitation.
/// </remarks>
public sealed class NativeDlpService : INativeDlpService
{
    public void PreventScreenCapture()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);
        if (window is null) return;

        var secureField = new UITextField { SecureTextEntry = true };
        window.AddSubview(secureField);
        secureField.CenterXAnchor.ConstraintEqualTo(window.CenterXAnchor).Active = true;
        secureField.CenterYAnchor.ConstraintEqualTo(window.CenterYAnchor).Active = true;

        window.Layer.SuperLayer?.AddSublayer(secureField.Layer);
        secureField.Layer.Sublayers?.FirstOrDefault()?.AddSublayer(window.Layer);
    }
}
