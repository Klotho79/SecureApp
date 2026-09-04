using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());

		// Milestone 4 (DLP): applied once, app-wide, as soon as the native platform window
		// actually exists — CreateWindow itself returns before that's true (Handler/PlatformView
		// are still null here), so this waits for Window.Created rather than calling it inline.
		window.Created += (_, _) =>
		{
			var dlpService = IPlatformApplication.Current?.Services.GetService<INativeDlpService>();
			dlpService?.PreventScreenCapture();

			// Milestone 5 (E2EE Chat): if this device already registered with a relay before and
			// auto-connect wasn't turned off, reconnect without the user having to open Settings
			// and press Connect on every launch. Fire-and-forget — a relay that's unreachable at
			// startup (offline, VPN not up yet) must never block or crash app launch; the Settings
			// page's Connect button remains available as a manual retry either way.
			_ = AutoConnectRelayAsync();
		};

		return window;
	}

	private static async Task AutoConnectRelayAsync()
	{
		var services = IPlatformApplication.Current?.Services;
		if (services is null) return;

		var transport = services.GetService<IMessageTransport>();
		if (transport is null || transport.IsConnected) return;

		using var scope = services.CreateScope();
		var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
		var configuration = await transportSettings.GetAsync();
		if (configuration is not { AssignedDeviceId: not null, IsAutoConnectEnabled: true, EndpointUri: { } endpoint })
			return;

		try
		{
			await transport.ConnectAsync(endpoint);
		}
		catch
		{
			// Best-effort — see the remark on the call site above.
		}
	}
}