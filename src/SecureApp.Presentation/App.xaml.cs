using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Entities;
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
		{
			transport.PairingInviteReceived += OnPairingInviteReceived;
			transport.GroupInviteReceived += OnGroupInviteReceived;
		}
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

	/// <summary>
	/// A group-invite blob (2026-09-07) is always a FULL membership snapshot, whether it's a brand
	/// new group, a rename, or a membership change (see <c>IGroupMemberRepository.ReplaceAllAsync</c>'s
	/// own remarks) — so applying one is the same "replace everything for this group id" operation
	/// every time. After syncing the local copy, this establishes this device's own pairwise
	/// <c>ChatSession</c> with any OTHER member it doesn't already have one with — the full-mesh
	/// crypto design <c>GroupChat</c>'s remarks describe. Each device runs this identical logic
	/// independently and symmetrically; there's no central coordinator beyond whoever last
	/// broadcast the snapshot.
	/// </summary>
	private static async void OnGroupInviteReceived(object? sender, string groupInviteBlobText)
	{
		var services = IPlatformApplication.Current?.Services;
		if (services is null) return;

		GroupInviteBlob invite;
		try
		{
			invite = ContactCardCodec.Decode<GroupInviteBlob>(groupInviteBlobText);
		}
		catch
		{
			return; // Malformed/foreign frame — never crash a background event handler over it.
		}

		using var scope = services.CreateScope();
		var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
		var groupChatRepository = scope.ServiceProvider.GetRequiredService<IGroupChatRepository>();
		var groupMemberRepository = scope.ServiceProvider.GetRequiredService<IGroupMemberRepository>();
		var messageTransport = scope.ServiceProvider.GetRequiredService<IMessageTransport>();

		try
		{
			var localPublicKey = await messagingService.GetLocalIdentityPublicKeyAsync();

			var existingGroup = await groupChatRepository.GetByIdAsync(invite.GroupId);
			if (existingGroup is null)
				await groupChatRepository.UpsertAsync(new GroupChat(invite.GroupId, invite.GroupName, invite.FounderPublicKey));
			else if (existingGroup.Name != invite.GroupName)
			{
				existingGroup.Rename(invite.GroupName);
				await groupChatRepository.UpsertAsync(existingGroup);
			}

			var members = invite.Members
				.Select(m => new GroupMember(invite.GroupId, m.DisplayName, m.PublicKey, m.RelayDeviceId))
				.ToList();
			await groupMemberRepository.ReplaceAllAsync(invite.GroupId, members);

			foreach (var member in invite.Members)
			{
				if (member.PublicKey.AsSpan().SequenceEqual(localPublicKey))
					continue; // that's me

				if (await messagingService.FindExistingSessionAsync(member.PublicKey) is not null)
					continue; // already paired with this member from an earlier group/1:1 chat

				if (!ShouldInitiateTo(localPublicKey, member.PublicKey))
					continue; // the deterministic tie-break says THEY initiate toward us instead — OnPairingInviteReceived above auto-accepts whenever that arrives

				try
				{
					var ownCard = await BuildOwnContactCardAsync(scope.ServiceProvider);
					var (_, handshakeCipherText) = await messagingService.CreateSessionAsync(member.DisplayName, member.PublicKey, member.RelayDeviceId);
					var chatInvite = new ChatInviteBlob(ownCard.DisplayName, ownCard.PublicKey, ownCard.RelayDeviceId, handshakeCipherText);

					if (messageTransport.IsConnected)
						await messageTransport.SendPairingInviteAsync(member.RelayDeviceId, ContactCardCodec.Encode(chatInvite));
					// Not connected right now: the session still exists locally (PendingHandshake),
					// same "stays Pending, no error surfaced" policy this app already uses elsewhere
					// (ChatViewModel.SendAsync). No manual QR/copy fallback UI for this particular
					// gap in this pass — a later reconnect + a fresh group resync would recover it.
				}
				catch
				{
					// Best-effort per member — one failed pairwise handshake must never abort
					// establishing sessions with the group's other members.
				}
			}
		}
		catch
		{
			// Best-effort, same reasoning as OnPairingInviteReceived above.
		}
	}

	/// <summary>Deterministic tie-break so exactly one side initiates a given pairwise handshake instead of both racing to create one simultaneously (which would otherwise deadlock both sessions in PendingHandshake — each side waiting for the other to accept an invite it never looked for). The comparison itself carries no meaning beyond being consistent on both ends.</summary>
	private static bool ShouldInitiateTo(byte[] localPublicKey, byte[] peerPublicKey)
	{
		var comparison = localPublicKey.Length != peerPublicKey.Length
			? localPublicKey.Length.CompareTo(peerPublicKey.Length)
			: string.CompareOrdinal(Convert.ToHexStringLower(localPublicKey), Convert.ToHexStringLower(peerPublicKey));
		return comparison > 0;
	}

	/// <summary>Duplicated from <c>NewChatViewModel.BuildOwnContactCardAsync</c> rather than shared — this static handler isn't a ViewModel and has no instance to call it on; small enough to stay independently readable.</summary>
	private static async Task<ContactCardBlob> BuildOwnContactCardAsync(IServiceProvider services)
	{
		var transportSettings = services.GetRequiredService<ITransportSettingsRepository>();
		var currentUserService = services.GetRequiredService<ICurrentUserService>();
		var messagingService = services.GetRequiredService<IMessagingService>();

		var configuration = await transportSettings.GetAsync();
		var deviceId = configuration?.AssignedDeviceId
			?? throw new InvalidOperationException("Register with a relay in Settings first.");
		var publicKey = await messagingService.GetLocalIdentityPublicKeyAsync();
		await currentUserService.InitializeAsync();
		return new ContactCardBlob(currentUserService.Current.DisplayName, publicKey, deviceId);
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