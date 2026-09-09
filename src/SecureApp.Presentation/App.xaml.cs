using Microsoft.Extensions.DependencyInjection;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
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

			// 2026-09-09: a real, previously-flagged-but-deferred gap the user hit live — a
			// ChatViewModel/GroupChatViewModel's own EnvelopeReceived subscription only exists
			// while ITS specific page happens to be open, so an envelope arriving (live, or
			// flushed from the relay's own outbox the moment a device reconnects) for any OTHER
			// thread was simply lost: raised on this singleton event with nothing subscribed to
			// catch it, while the relay had already deleted its own queued copy the instant it
			// handed the frame over — not delayed, genuinely gone. Subscribed here, once, for the
			// app's whole lifetime, so EVERY envelope gets decrypted and persisted regardless of
			// what's on screen; a page's own handler running moments later for the same envelope
			// is a safe no-op (see ReceiveMessageAsync's own idempotency guard) — whichever runs
			// first "wins" the actual decrypt, opening that thread later just shows what's already
			// in local storage.
			transport.EnvelopeReceived += OnEnvelopeReceived;
		}

		// 2026-09-07: two rounds of direct user feedback, both pointing the same direction — the
		// app, not the user, is responsible for noticing something's wrong and fixing it. First: a
		// broken pairwise session shouldn't need anyone to press a reset button (see
		// SessionRecoveryHelper.ResyncAsync). Second, blunter still, about the relay connection
		// itself: "uzivatel vubec nema ovladat připojení apka sama musi zjistovat jestli je připojena
		// akdyz ne podnikne vse aby se pripojila" (the user should never have to manage the
		// connection at all — the app itself must know whether it's connected and, if not, do
		// everything to reconnect). Before this, the app only ever tried to connect ONCE at launch
		// (the old AutoConnectRelayAsync) or reactively when a chat page happened to be opened
		// (ChatViewModel.EnsureConnectedAsync) — a drop at any other moment (relay restart, phone
		// sleep, switching Wi-Fi/mobile data) just sat disconnected, unnoticed, until someone opened
		// Settings and pressed Connect by hand. This one loop replaces both that one-shot connect and
		// the earlier connected-only resync sweep: it runs for the app's whole foreground lifetime,
		// checks IsConnected on every tick, and reconnects the moment it can — no user action
		// anywhere in it.
		_ = RunConnectionSupervisorLoopAsync();
	}

	private static readonly TimeSpan _supervisorTickInterval = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _staleSessionSweepInterval = TimeSpan.FromMinutes(3);

	/// <summary>
	/// Runs for the app's whole foreground lifetime (started once, from the constructor — honest
	/// scope: this is a foreground-process loop, not OS-level background execution, same as every
	/// other "best-effort" spot in this codebase). Every tick: if the relay isn't connected, try to
	/// connect; either right after a reconnect just succeeded, or otherwise every
	/// <see cref="_staleSessionSweepInterval"/> regardless, also sweep for any pairwise session stuck
	/// mid-resync (see <see cref="RunStaleSessionSweepAsync"/>).
	/// </summary>
	private static async Task RunConnectionSupervisorLoopAsync()
	{
		var lastStaleSweep = DateTimeOffset.MinValue;

		while (true)
		{
			try
			{
				var services = IPlatformApplication.Current?.Services;
				var transport = services?.GetService<IMessageTransport>();

				if (services is not null && transport is not null)
				{
					var wasConnected = transport.IsConnected;
					if (!wasConnected)
						await TryConnectAsync(services, transport);

					var justReconnected = !wasConnected && transport.IsConnected;
					var sweepDue = DateTimeOffset.UtcNow - lastStaleSweep >= _staleSessionSweepInterval;

					if (transport.IsConnected && (justReconnected || sweepDue))
					{
						await RunStaleSessionSweepAsync(services, transport);
						lastStaleSweep = DateTimeOffset.UtcNow;
					}
				}
			}
			catch
			{
				// This loop must never die from one bad tick — whatever went wrong is tried again
				// next tick, ~10s later.
			}

			try { await Task.Delay(_supervisorTickInterval); }
			catch { return; }
		}
	}

	/// <summary>The actual connect attempt, called from every supervisor tick that finds the transport disconnected (not just once at launch, as the old AutoConnectRelayAsync did). Respects <c>IsAutoConnectEnabled</c>, so a user who deliberately turned auto-connect off in Settings isn't overridden.</summary>
	private static async Task TryConnectAsync(IServiceProvider services, IMessageTransport transport)
	{
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
			// Best-effort — retried again next tick, ~10s later.
		}
	}

	/// <summary>
	/// Background self-healing sweep (2026-09-07): looks for a peer whose ONLY local <see cref="ChatSession"/>
	/// row is Closed — meaning an earlier <see cref="SessionRecoveryHelper.ResyncAsync"/> call
	/// closed the old session but never got as far as sending (or even creating) its replacement,
	/// most likely because the relay wasn't connected at that exact moment — and retries the whole
	/// resync.
	/// </summary>
	private static async Task RunStaleSessionSweepAsync(IServiceProvider services, IMessageTransport transport)
	{
		using var scope = services.CreateScope();
		var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
		var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
		var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
		var currentUserService = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();

		IReadOnlyList<ChatSession> sessions;
		try { sessions = await sessionRepository.GetAllAsync(); }
		catch { return; }

		var stalePeers = sessions
			.GroupBy(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey))
			.Where(group => group.All(s => s.State == ChatSessionState.Closed))
			.Select(group => group.OrderByDescending(s => s.ModifiedAtUtc).First());

		foreach (var stale in stalePeers)
		{
			if (stale.PeerRelayDeviceId is not { } relayDeviceId)
				continue;

			try
			{
				await SessionRecoveryHelper.ResyncAsync(
					messagingService, transport, transportSettings, currentUserService,
					stale.PeerDisplayName, stale.PeerIdentityPublicKey, relayDeviceId);
			}
			catch
			{
				// Best-effort — retried again next sweep.
			}
		}
	}

	/// <summary>
	/// Always-on persistence (2026-09-09) — see this handler's own subscription comment above for
	/// why it exists. Decrypts and stores every incoming envelope unconditionally, 1:1 or group
	/// alike (<c>MessagingService.ReceiveMessageAsync</c> already handles both the same way, since a
	/// group message is just an ordinary pairwise-session row with a couple of extra tags).
	/// </summary>
	private static async void OnEnvelopeReceived(object? sender, MessageEnvelope envelope)
	{
		var services = IPlatformApplication.Current?.Services;
		if (services is null) return;

		using var scope = services.CreateScope();
		var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

		try
		{
			await messagingService.ReceiveMessageAsync(envelope);
		}
		catch
		{
			// A genuine ratchet decrypt failure (not the idempotency guard's silent-duplicate
			// case, which never throws) — auto-heal the underlying session right here too, same
			// reasoning as ChatViewModel/GroupChatViewModel's own TryAutoHealAsync, so the NEXT
			// message has a real chance even with no page open to react to this one.
			// SessionRecoveryHelper.ResyncAsync's own cooldown makes it safe for a page-level
			// handler to also attempt this a moment later for the same peer.
			try
			{
				var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
				var session = await sessionRepository.GetByIdAsync(envelope.SessionId);
				if (session?.PeerRelayDeviceId is { } relayDeviceId)
				{
					var messageTransport = scope.ServiceProvider.GetRequiredService<IMessageTransport>();
					var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
					var currentUserService = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
					await SessionRecoveryHelper.ResyncAsync(
						messagingService, messageTransport, transportSettings, currentUserService,
						session.PeerDisplayName, session.PeerIdentityPublicKey, relayDeviceId);
				}
			}
			catch
			{
				// Best-effort — nothing further to do at this level.
			}
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
			// A fresh handshake invite from a peer we already have a session with (2026-09-07) now
			// means THAT SIDE determined a resync was needed (see SessionRecoveryHelper.ResyncAsync)
			// and is trying to fix things without any action required here — so always accept it,
			// closing this device's own stale copy first, rather than silently ignoring it as before.
			// (The old behavior assumed "already paired" could only mean a redundant duplicate scan
			// of the initiator's own QR — accepting-and-replacing is harmless in that case too, just
			// a wasted extra handshake.)
			if (await messagingService.FindExistingSessionAsync(invite.InitiatorPublicKey) is { } existingSession)
				await messagingService.CloseSessionAsync(existingSession.Id);

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

			// Milestone 5 (E2EE Chat) connect: handled entirely by RunConnectionSupervisorLoopAsync
			// now, started from the constructor above — its very first tick fires almost immediately,
			// so launch-time connect behavior is unchanged; nothing further to kick off here.
		};

		return window;
	}
}