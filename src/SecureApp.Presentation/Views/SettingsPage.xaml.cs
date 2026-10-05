using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    private bool _systemTabPrewarmed;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
        _viewModel.StartObservingConnection();
        // Coming back from Android's battery setting resumes the window without re-firing OnAppearing.
        if (Window is { } window) window.Resumed += OnWindowResumed;
        PrewarmSystemTab();
    }

    /// <summary>
    /// 2026-10-05, user's own repeated report: the FIRST switch to the "Systém" tab (a plain
    /// IsVisible-bound VerticalStackLayout — see SettingsPage.xaml's own remarks) took noticeably
    /// long. Root cause: that section is much larger than "Uživatel" (~220 vs ~140 XAML lines of
    /// cards), and on Android IsVisible=False maps to View.GONE, which the platform's own layout
    /// system never measures or lays out until it's first made Visible — so the FIRST tap paid the
    /// full one-time cost of measuring/inflating that whole subtree, synchronously, blocking the UI.
    /// Every later switch is instant because the native views already exist with cached measurements.
    ///
    /// Fix: measure+layout the still-GONE SystemTabContent's native Android view directly, bypassing
    /// MAUI/Visibility entirely — View.Measure()/.Layout() are governed by the view itself, not by
    /// its own Visibility (GONE only makes the PARENT skip calling them during its own pass); calling
    /// them directly here does the same expensive work, but completely invisibly, since the view's
    /// Visibility never changes. Deliberately NOT done by toggling SettingsTab/IsSystemSettingsTab
    /// (the obvious alternative) — that bool also drives the tab button's highlight color via a
    /// DataTrigger, so flipping it even briefly risked a visible flash of the wrong button.
    /// Best-effort and Android-only: a failure here just means the original (one-time) delay comes
    /// back, never a crash of the real page.
    /// </summary>
    private void PrewarmSystemTab()
    {
#if ANDROID
        if (_systemTabPrewarmed) return;
        _systemTabPrewarmed = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
        {
            try
            {
                if (UserTabContent.Handler?.PlatformView is not Android.Views.View userNative) return;
                if (SystemTabContent.Handler?.PlatformView is not Android.Views.View systemNative) return;
                // UserTabContent is already visible/measured — its sibling SystemTabContent (same
                // parent, same padding) will get the exact same width once shown for real.
                var widthSpec = Android.Views.View.MeasureSpec.MakeMeasureSpec(userNative.MeasuredWidth, Android.Views.MeasureSpecMode.Exactly);
                var heightSpec = Android.Views.View.MeasureSpec.MakeMeasureSpec(0, Android.Views.MeasureSpecMode.Unspecified);
                systemNative.Measure(widthSpec, heightSpec);
                systemNative.Layout(0, 0, systemNative.MeasuredWidth, systemNative.MeasuredHeight);
            }
            catch
            {
                // Best-effort — see this method's own remarks.
            }
        });
#endif
    }

    private void OnWindowResumed(object? sender, EventArgs e) => _viewModel.RefreshBackgroundRunStatus();

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Window is { } window) window.Resumed -= OnWindowResumed;
        _viewModel.StopObservingConnection();
        _viewModel.StopActivationPolling();
        _viewModel.ClearMemberManagement();
    }
}
