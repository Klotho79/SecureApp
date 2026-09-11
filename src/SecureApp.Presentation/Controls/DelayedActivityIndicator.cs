using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Controls;

/// <summary>
/// A loading spinner that appears only if the busy state lasts longer than a short delay (2026-09-11,
/// the user's ask: globally, the spinner shouldn't flicker on brief loads). Bind <see cref="IsBusy"/>
/// where an <c>ActivityIndicator</c> would normally bind IsRunning/IsVisible; when busy turns on, the
/// spinner waits <see cref="Delay"/> ms before actually showing, and if busy turns off first it never
/// shows at all. Subclasses <see cref="ActivityIndicator"/> so every layout property (Horizontal/
/// VerticalOptions, Color, size) works exactly as on a plain one.
/// </summary>
public class DelayedActivityIndicator : ActivityIndicator
{
    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(DelayedActivityIndicator), false, propertyChanged: OnIsBusyChanged);

    /// <summary>How long busy must persist before the spinner is shown. Default 600 ms — long enough that ordinary loads never flash it, short enough that a genuine wait still gets feedback.</summary>
    public static readonly BindableProperty DelayProperty = BindableProperty.Create(
        nameof(Delay), typeof(int), typeof(DelayedActivityIndicator), 600);

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public int Delay
    {
        get => (int)GetValue(DelayProperty);
        set => SetValue(DelayProperty, value);
    }

    private CancellationTokenSource? _cts;
    private DateTime _busyStartedUtc;
    private bool _shown;

    public DelayedActivityIndicator()
    {
        IsVisible = false;
        IsRunning = false;
    }

    private static void OnIsBusyChanged(BindableObject bindable, object oldValue, object newValue)
        => ((DelayedActivityIndicator)bindable).Apply((bool)newValue);

    private void Apply(bool busy)
    {
        _cts?.Cancel();

        if (!busy)
        {
            // 2026-09-11 instrumentation: report how long the busy state actually lasted and whether
            // the spinner ended up showing — so we can tell a genuinely-slow load from a control bug.
            var elapsedMs = (int)(DateTime.UtcNow - _busyStartedUtc).TotalMilliseconds;
            Report($"busy ended after {elapsedMs} ms, spinnerShown={_shown}");
            IsVisible = false;
            IsRunning = false;
            _shown = false;
            return;
        }

        _busyStartedUtc = DateTime.UtcNow;
        var cts = _cts = new CancellationTokenSource();
        _ = ShowAfterDelayAsync(cts.Token);
    }

    private async Task ShowAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Delay, ct);
        }
        catch (TaskCanceledException)
        {
            return; // busy turned off before the delay elapsed — never show the spinner
        }

        if (ct.IsCancellationRequested) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (ct.IsCancellationRequested) return; // a newer state change won the race
            IsVisible = true;
            IsRunning = true;
            _shown = true;
        });
    }

    private static void Report(string message)
    {
        try
        {
            var reporter = IPlatformApplication.Current?.Services.GetService<IDiagnosticsReporter>();
            _ = reporter?.ReportAsync(DiagnosticLogLevel.Info, "Spinner: " + message, nameof(DelayedActivityIndicator));
        }
        catch { /* diagnostics is best-effort */ }
    }
}
