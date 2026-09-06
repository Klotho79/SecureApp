using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Presentation.Chat;

namespace SecureApp.Presentation;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// 2026-09-06 pairing simplification: whichever side is online when the other calls
		// CreateSessionAsync now gets the invite delivered automatically (see
		// NewChatViewModel.CreateSessionAsync's own remarks) instead of needing a second manual
		// QR/copy-paste round trip. Subscribed here (once, for the app's whole lifetime) rather
		// than only while NewChatPage happens to be open — the other person might complete pairing
		// at any time, not just while you're looking at that screen.
		var transport = IPlatformApplication.Current?.Services.GetService<IMessageTransport>();
		if (transport is not null)
			transport.PairingInviteReceived += OnPairingInviteReceived;
	}

	private static async void OnPairingInviteReceived(object? sender, string inviteBlob)
	{
		var services = IPlatformApplication.Current?.Services;
		if (services is null) return;

		ChatInviteBlob invite;
		try
		{
			invite = ContactCardCodec.Decode<ChatInviteBlob>(inviteBlob);
		}
		catch
		{
			return; // Malformed/foreign frame — never crash a background event handler over it.
		}

		using var scope = services.CreateScope();
		var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
		try
		{
			if (await messagingService.FindExistingSessionAsync(invite.InitiatorPublicKey) is not null)
				return; // Already paired — e.g. the sender's own fallback QR was also scanned separately.

			await messagingService.AcceptSessionAsync(invite.InitiatorDisplayName, invite.InitiatorPublicKey, invite.InitiatorRelayDeviceId, invite.HandshakeCipherText);
		}
		catch
		{
			// Best-effort, same reasoning as AutoConnectRelayAsync below — the manual QR/copy-paste
			// fallback the initiator's own screen still shows remains available either way.
		}
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