using Microsoft.Extensions.DependencyInjection;
using SecureApp.Data.Persistence;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Infrastructure;

namespace SecureApp.Presentation;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Shared diagnostics log (2026-09-10) — a genuine crash is exactly the class of failure
		// this log exists for (see IDiagnosticsReporter's own remarks): the one thing worse than an
		// error nobody can see remotely is a crash that kills the process before anything ELSE gets
		// a chance to report it. Both handlers are itself best-effort (the reporter never throws —
		// see HttpDiagnosticsReporter's own remarks — but resolving it from DI theoretically could
		// if the container itself is in a bad state, hence the outer try/catch here too).
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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

		// 2026-09-11 perf: open (and migrate) the SQLCipher database once, up front, in the
		// background. That first connection is the one-time cost on the path to opening the first
		// chat — doing it here means a chat opened moments later reads an already-open connection
		// instead of waiting ~a second behind a spinner for the DB to come up. Best-effort.
		_ = WarmUpDatabaseAsync();

		// 2026-09-13: prune churned sessions. Resync/auto-heal/group-mesh close the old session and
		// create a new one each time (see SessionRecoveryHelper.ResyncAsync), leaving stale EMPTY,
		// Closed rows behind. Besides cluttering the list (now deduped per peer in ChatListViewModel),
		// they bloat every GetAllAsync — which GroupChatViewModel runs on each open to build sender
		// names — so pruning them also speeds up opening. Deletes ONLY empty (no messages) Closed
		// sessions that have another session for the same peer, so no history is lost and every peer
		// keeps at least one session. Best-effort, once at launch.
		_ = PruneChurnedSessionsAsync();

		// 2026-09-13: warm the SkiaSharp render pipeline in the background so the first photo/document
		// open doesn't pay the ~157ms one-time native init. Best-effort.
		_ = Task.Run(SecureApp.Presentation.Rendering.DocumentRenderingService.WarmUp);
	}

	private static async Task PruneChurnedSessionsAsync()
	{
		try
		{
			var services = IPlatformApplication.Current?.Services;
			if (services is null) return;
			using var scope = services.CreateScope();
			var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
			var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

			var sessions = await sessionRepository.GetAllAsync();
			var pruned = 0;
			foreach (var peerGroup in sessions.GroupBy(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey)))
			{
				if (peerGroup.Count() < 2) continue; // never touch a peer's only session

				// Keep the one the list would show (a live session over Closed, then most recent).
				var keep = peerGroup
					.OrderByDescending(s => s.State != ChatSessionState.Closed)
					.ThenByDescending(s => s.LastRatchetedAtUtc ?? s.CreatedAtUtc)
					.First();

				foreach (var candidate in peerGroup)
				{
					if (candidate.Id == keep.Id || candidate.State != ChatSessionState.Closed) continue;
					var messages = await messageRepository.GetBySessionAsync(candidate.Id);
					if (messages.Count > 0) continue; // has history — keep it
					await sessionRepository.DeleteAsync(candidate.Id);
					pruned++;
				}
			}

			if (pruned > 0)
				AppLog.Metric("sessions.pruned", pruned, "count", ("total", sessions.Count));
		}
		catch (Exception ex)
		{
			AppLog.Error(nameof(PruneChurnedSessionsAsync), "session prune failed", ex);
		}
	}

	private static async Task WarmUpDatabaseAsync()
	{
		try
		{
			var factory = IPlatformApplication.Current?.Services.GetService<ISecureDatabaseConnectionFactory>();
			if (factory is not null)
				await factory.EnsureInitializedAsync();
		}
		catch
		{
			// Best-effort — the first real DB access will open it (and surface any genuine error) anyway.
		}
	}

	/// <summary>Best-effort resolve-and-report, shared by both global exception handlers below — never throws, since a handler for "something already went catastrophically wrong" is the last place that can afford to introduce a NEW exception.</summary>
	private static void ReportFireAndForget(DiagnosticLogLevel level, string message, string context, Exception? exception)
	{
		// Durable local log first (2026-09-13) — works offline and survives a crash-on-exit, unlike the
		// relay reporter which needs a live connection. AppLog.Error never throws.
		AppLog.Error(context, $"[{level}] {message}", exception);

		try
		{
			var reporter = IPlatformApplication.Current?.Services.GetService<IDiagnosticsReporter>();
			if (reporter is not null)
				_ = reporter.ReportAsync(level, message, context, exception);
		}
		catch
		{
			// Truly nothing further to do — even resolving the reporter itself failed.
		}
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		var exception = e.ExceptionObject as Exception;
		ReportFireAndForget(DiagnosticLogLevel.Error,
			e.IsTerminating ? "Neošetřená výjimka — aplikace se ukončuje." : "Neošetřená výjimka.",
			nameof(OnUnhandledException), exception);
	}

	private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		ReportFireAndForget(DiagnosticLogLevel.Error, "Nepozorovaná výjimka v úloze na pozadí.", nameof(OnUnobservedTaskException), e.Exception);
		e.SetObserved(); // Prevents this from also crashing the process on runtimes that still enforce that.
	}

	private static readonly TimeSpan _supervisorTickInterval = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _staleSessionSweepInterval = TimeSpan.FromMinutes(3);

	/// <summary>
	/// A live-looking connection can be silently dead (2026-09-09, caught live — S9+ went 4+ hours
	/// with no successful reconnect, `IsConnected` presumably still reporting true the whole time):
	/// a WebSocket the client never explicitly closed can still die at the TCP level — a NAT/router
	/// timing out an idle mapping, the OS quietly dropping the socket while the app is backgrounded
	/// — with `ClientWebSocket.State` staying `Open` until an actual send/receive attempt fails.
	/// If nothing happens to be sent FROM this device for a while (the common case for whoever's
	/// mostly *receiving* during a test), that failure might never get triggered, and this device
	/// just silently stops receiving anything, with no error, no dropped-state event, nothing for
	/// the rest of this loop to react to. Forcing a full disconnect+reconnect on this fixed cadence,
	/// regardless of what `IsConnected` currently claims, bounds how long that kind of failure can
	/// go unnoticed — worst case <see cref="_forcedReconnectInterval"/>, not indefinitely.
	/// </summary>
	private static readonly TimeSpan _forcedReconnectInterval = TimeSpan.FromMinutes(5);

	/// <summary>
	/// Runs for the app's whole foreground lifetime (started once, from the constructor — honest
	/// scope: this is a foreground-process loop, not OS-level background execution, same as every
	/// other "best-effort" spot in this codebase). Every tick: if the relay isn't connected, try to
	/// connect; if it's been connected (however that's reported) for longer than
	/// <see cref="_forcedReconnectInterval"/>, force a fresh reconnect anyway (see that field's own
	/// remarks on why); either right after a reconnect just succeeded, or otherwise every
	/// <see cref="_staleSessionSweepInterval"/> regardless, also sweep for any pairwise session stuck
	/// mid-resync (see <see cref="RunStaleSessionSweepAsync"/>).
	/// </summary>
	private static async Task RunConnectionSupervisorLoopAsync()
	{
		var lastStaleSweep = DateTimeOffset.MinValue;
		var lastForcedReconnect = DateTimeOffset.UtcNow;

		while (true)
		{
			try
			{
				var services = IPlatformApplication.Current?.Services;
				var transport = services?.GetService<IMessageTransport>();

				if (services is not null && transport is not null)
				{
					var wasConnected = transport.IsConnected;

					if (wasConnected && DateTimeOffset.UtcNow - lastForcedReconnect >= _forcedReconnectInterval)
					{
						try { await transport.DisconnectAsync(); }
						catch { /* best-effort — TryConnectAsync below still runs regardless */ }
						wasConnected = false;
						lastForcedReconnect = DateTimeOffset.UtcNow;
					}

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
			// 2.5 (2026-09-14): log the automatic reconnect so a recovered connection (and thus why a
			// stuck-Pending message suddenly went through) is visible in the log.
			SecureApp.Presentation.Infrastructure.AppLog.Event("relay.reconnected");
		}
		catch (Exception ex)
		{
			// Best-effort — retried again next tick, ~10s later. Logged (not silent) so a persistently
			// failing reconnect — the reason messages aren't leaving — is diagnosable.
			SecureApp.Presentation.Infrastructure.AppLog.Error("App.TryConnect", "relay reconnect attempt failed", ex);
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

		// Fully automatic shared-library-key distribution (2026-09-11) — the app does this itself,
		// no user action, no manual "resend key" step (the user's explicit demand). Every sweep,
		// this device either pushes the key to its paired sessions (if it has it) or asks them for it
		// (if it doesn't); see SharedLibraryKeySync.AutoSyncAsync's own remarks. Best-effort and
		// independent of the stale-session resync below.
		try
		{
			var sharedLibraryService = scope.ServiceProvider.GetRequiredService<ISharedLibraryService>();
			var diagnosticsReporter = scope.ServiceProvider.GetRequiredService<IDiagnosticsReporter>();
			await SharedLibraryKeySync.AutoSyncAsync(sharedLibraryService, messagingService, transport, sessionRepository, diagnosticsReporter);
		}
		catch
		{
			// Best-effort — retried next sweep, ~3 min.
		}

		IReadOnlyList<ChatSession> sessions;
		try { sessions = await sessionRepository.GetAllAsync(); }
		catch { return; }

		var stalePeers = sessions
			.GroupBy(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey))
			.Where(group => group.All(s => s.State == ChatSessionState.Closed))
			.Select(group => group.OrderByDescending(s => s.ModifiedAtUtc).First())
				.ToList();

			if (stalePeers.Count > 0)
				AppLog.Event("sweep.stale-peers-resyncing", ("count", stalePeers.Count));
			// NOTE: this sweep is the path that kept recreating the ghost "Local User" — logging it is
			// exactly the visibility 2.5 is about; 2.2 will add a suppression so a user-removed peer
			// isn't auto-resynced here at all.

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
			catch (Exception ex)
			{
				// Best-effort — retried again next sweep. Reported at Warning (not Error) since
				// this is an expected, self-correcting retry loop, not a surprise.
				ReportFireAndForget(DiagnosticLogLevel.Warning, $"Pozadí: obnovení relace s {stale.PeerDisplayName} se nepodařilo, zkusí se znovu při dalším průchodu.", nameof(RunStaleSessionSweepAsync), ex);
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
			var message = await messagingService.ReceiveMessageAsync(envelope);

			// 2.5 (2026-09-14): receive logging — correlates with the sender's "msg.sent" (same corr id)
			// so a sent-but-never-received message (the ghost black-hole) is diagnosable from the logs
			// on either side, instead of being silent.
			AppLog.Event("msg.received", ("corr", envelope.OriginMessageId), ("group", envelope.GroupChatId), ("system", envelope.IsSystemPayload));

			// Delivery ack (2026-09-14): confirm back to the SENDER that this device's app received the
			// message, so their UI can show ✓✓ (delivery receipt, not read). Only for real messages —
			// acks and other system payloads are never themselves acked, so there is no ack loop. A group
			// message is acked by its GroupMessageId (shared across the sender's fan-out legs), a 1:1 by
			// its OriginMessageId. Best-effort: a failed ack just leaves the sender showing ✓ (sent).
			if (!envelope.IsSystemPayload && (envelope.GroupMessageId ?? envelope.OriginMessageId) is { } incomingCorr)
			{
				try
				{
					var ackTransport = scope.ServiceProvider.GetRequiredService<IMessageTransport>();
					var ackPayload = DeliveryAckSync.BuildAck(incomingCorr);
					var (_, ackEnvelope) = await messagingService.SendMessageAsync(message.ChatSessionId, ackPayload, isSystemPayload: true);
					if (ackTransport.IsConnected) await ackTransport.SendEnvelopeAsync(ackEnvelope);
					AppLog.Event("msg.ack-sent", ("corr", incomingCorr));
				}
				catch (Exception ackEx) { AppLog.Error("App.Receive", "sending delivery ack failed", ackEx); }
			}

			// Shared-library-key offer arriving (2026-09-10) — see SharedLibraryKeySync's own
			// remarks. Decrypt-and-import right here, unconditionally, regardless of whether any
			// chat page happens to be open — same "always-on persistence" reasoning this handler's
			// own subscription comment already established for ordinary messages. Best-effort: a
			// malformed/foreign system payload, or an import failure (e.g. this device somehow
			// already has a DIFFERENT key — see ImportSharedKeyAsync's own remarks on why that's
			// never silently overwritten... actually it just overwrites, which is fine, the whole
			// community shares exactly one key) never surfaces as an error to the user.
			if (envelope.IsSystemPayload)
			{
				var reporterForImport = scope.ServiceProvider.GetRequiredService<IDiagnosticsReporter>();
				try
				{
					var plaintext = await messagingService.DecryptMessageAsync(message.Id);
						// Delivery ack (2026-09-14): the recipient's app confirmed it received one of OUR
						// messages — mark our own outbound copy Delivered (UI ✓✓). Standalone check, independent
						// of the key/delete chain below (an ack matches none of those).
						if (DeliveryAckSync.TryParseAck(plaintext, out var ackCorrelationId))
						{
							// Mark only THIS member's leg Delivered (the ack arrived on their session), so a
							// group message reads ✓✓ only once every member has acked; a 1:1 has one leg.
							var ackRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
							var upgraded = await ackRepo.MarkDeliveredForSessionAsync(message.ChatSessionId, ackCorrelationId);
							AppLog.Event("msg.delivered-leg", ("corr", ackCorrelationId), ("upgraded", upgraded));
						}
					if (SharedLibraryKeySync.TryParseKeyOffer(plaintext, out var keyBlob))
					{
						var sharedLibraryService = scope.ServiceProvider.GetRequiredService<ISharedLibraryService>();
						await sharedLibraryService.ImportSharedKeyAsync(keyBlob);
						// Logged (2026-09-11) — this is the one line that actually proves the whole
						// mechanism worked end to end; its absence in the shared log is exactly what
						// distinguishes "never arrived" from "arrived but failed to import" from
						// "arrived and worked" when this gets reported broken again.
						_ = reporterForImport.ReportAsync(DiagnosticLogLevel.Info, "Klíč sdílené knihovny přijat a naimportován.", nameof(OnEnvelopeReceived));
					}
					else if (SharedLibraryKeySync.IsKeyRequest(plaintext))
					{
						// A paired peer that doesn't have the key is asking for it (the PULL half of
						// the automatic distribution — see SharedLibraryKeySync's own remarks). If we
						// have it, answer immediately over that same session, bypassing the push
						// cooldown since this is a direct, explicit request, not incidental traffic.
						var sharedLibraryService = scope.ServiceProvider.GetRequiredService<ISharedLibraryService>();
						var messageTransportForOffer = scope.ServiceProvider.GetRequiredService<IMessageTransport>();
						await SharedLibraryKeySync.OfferKeyAsync(sharedLibraryService, messagingService, messageTransportForOffer, message.ChatSessionId, reporterForImport, bypassCooldown: true);
						}
						else if (MessageDeletionSync.TryParseDeleteCommand(plaintext, out var correlationId))
						{
							// A peer deleted a message for everyone (2026-09-11) — remove our own copy
							// even if no chat page is open. Idempotent (the open ViewModel, if any, also
							// does this and updates its visible list). The deleter already made the RBAC
							// decision; we honor it, same as any other message from a paired peer.
							var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
							await messageRepository.DeleteByCorrelationAsync(correlationId);
						}
					}
				catch (Exception importEx)
				{
					// Best-effort — nothing further to do at this level, but logged rather than
					// silently swallowed (same reasoning as SharedLibraryKeySync's own catch blocks).
					_ = reporterForImport.ReportAsync(DiagnosticLogLevel.Error, "Přijatý klíč sdílené knihovny se nepodařilo naimportovat.", nameof(OnEnvelopeReceived), importEx);
				}
			}
		}
		catch (Exception ex)
		{
			// A genuine ratchet decrypt failure (not the idempotency guard's silent-duplicate
			// case, which never throws) — auto-heal the underlying session right here too, same
			// reasoning as ChatViewModel/GroupChatViewModel's own TryAutoHealAsync, so the NEXT
			// message has a real chance even with no page open to react to this one.
			// SessionRecoveryHelper.ResyncAsync's own cooldown makes it safe for a page-level
			// handler to also attempt this a moment later for the same peer.
			var reporter = scope.ServiceProvider.GetRequiredService<IDiagnosticsReporter>();
			_ = reporter.ReportAsync(DiagnosticLogLevel.Error, "Přijatou zprávu se nepodařilo dešifrovat — spouští se automatické obnovení spojení.", nameof(OnEnvelopeReceived), ex);
			AppLog.Error("App.Receive", "decrypt failed; auto-healing session", ex);
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

			var acceptedSession = await messagingService.AcceptSessionAsync(invite.InitiatorDisplayName, invite.InitiatorPublicKey, invite.InitiatorRelayDeviceId, invite.HandshakeCipherText);
			AppLog.Event("pairing.accepted", ("peer", invite.InitiatorDisplayName), ("peerDevice", invite.InitiatorRelayDeviceId));

			// Best-effort shared-library-key offer (2026-09-10) — see SharedLibraryKeySync's own
			// remarks; this is the auto-pairing counterpart of NewChatViewModel.AcceptInviteAsync's
			// own manual-paste call, so a fully automatic pairing gets the same treatment.
			// Awaited (not fire-and-forget) — this handler's own `using var scope` above disposes
			// at method exit, and OfferKeyAsync needs that scope's services to still be alive while
			// it runs; it's already internally best-effort (never throws), so awaiting costs nothing
			// but a few more milliseconds on this background event handler.
			var sharedLibraryService = scope.ServiceProvider.GetRequiredService<ISharedLibraryService>();
			var messageTransportForOffer = scope.ServiceProvider.GetRequiredService<IMessageTransport>();
			var reporterForOffer = scope.ServiceProvider.GetRequiredService<IDiagnosticsReporter>();
			await SharedLibraryKeySync.OfferKeyAsync(sharedLibraryService, messagingService, messageTransportForOffer, acceptedSession.Id, reporterForOffer);
		}
		catch (Exception ex)
		{
			// Best-effort, same reasoning as AutoConnectRelayAsync below — the manual QR/copy-paste
			// fallback the initiator's own screen still shows remains available either way.
			ReportFireAndForget(DiagnosticLogLevel.Warning, $"Automatické spárování s {invite.InitiatorDisplayName} selhalo — zůstává dostupný ruční QR/kopírovací postup.", nameof(OnPairingInviteReceived), ex);
			AppLog.Error("App.Pairing", $"auto-pair with {invite.InitiatorDisplayName} failed", ex);
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
			AppLog.Event("group.membership.synced", ("group", invite.GroupId), ("name", invite.GroupName), ("members", members.Count));

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
					var (newMemberSession, handshakeCipherText) = await messagingService.CreateSessionAsync(member.DisplayName, member.PublicKey, member.RelayDeviceId);
					var chatInvite = new ChatInviteBlob(ownCard.DisplayName, ownCard.PublicKey, ownCard.RelayDeviceId, handshakeCipherText);

					if (messageTransport.IsConnected)
						await messageTransport.SendPairingInviteAsync(member.RelayDeviceId, ContactCardCodec.Encode(chatInvite));
					AppLog.Event("group.member.pairing-initiated", ("member", member.DisplayName), ("memberDevice", member.RelayDeviceId), ("connected", messageTransport.IsConnected));
					// Not connected right now: the session still exists locally (PendingHandshake),
					// same "stays Pending, no error surfaced" policy this app already uses elsewhere
					// (ChatViewModel.SendAsync). No manual QR/copy fallback UI for this particular
					// gap in this pass — a later reconnect + a fresh group resync would recover it.

					// Best-effort shared-library-key offer (2026-09-10) — see SharedLibraryKeySync's
					// own remarks; a brand new group member is exactly the case the user's own
					// objection was about, so this new pairwise session gets the same offer too.
					var sharedLibraryServiceForMember = scope.ServiceProvider.GetRequiredService<ISharedLibraryService>();
					var reporterForMember = scope.ServiceProvider.GetRequiredService<IDiagnosticsReporter>();
					await SharedLibraryKeySync.OfferKeyAsync(sharedLibraryServiceForMember, messagingService, messageTransport, newMemberSession.Id, reporterForMember);
				}
				catch (Exception memberEx)
				{
					// Best-effort per member — one failed pairwise handshake must never abort
					// establishing sessions with the group's other members.
					AppLog.Error("App.GroupInvite", $"pairing with member {member.DisplayName} failed", memberEx);
				}
			}
		}
		catch (Exception ex)
		{
			// Best-effort, same reasoning as OnPairingInviteReceived above.
			AppLog.Error("App.GroupInvite", "group invite handling failed", ex);
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
			?? throw new InvalidOperationException("Nejprve se zaregistrujte u relay serveru v Nastavení.");
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