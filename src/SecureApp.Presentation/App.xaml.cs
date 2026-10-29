using Microsoft.Extensions.DependencyInjection;
using SecureApp.Data.Persistence;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Chat;
using SecureApp.Presentation.Infrastructure;
using SecureApp.Presentation.Notifications;

namespace SecureApp.Presentation;

public partial class App : Application
{
	/// <summary>Preference key for the user-chosen theme mode (2026-09-24, Phase 4 — easy customization). 0 = follow system, 1 = light, 2 = dark. Applied via <see cref="ApplySavedThemeMode"/> at startup and live from Settings.</summary>
	// 2026-10-05 — Windows multi-profile login: prefixed per-profile (property instead of const —
	// transparent to every existing App.ThemeModePreferenceKey caller), see ArchivedChatsStore's own
	// identical remarks and Profiles.ActiveProfile.
	public static string ThemeModePreferenceKey => Profiles.ActiveProfile.PrefKey("app_theme_mode");

	/// <summary>Maps the stored theme-mode preference onto MAUI's <see cref="Application.UserAppTheme"/>, which re-evaluates every AppThemeBinding live. Safe to call anytime (startup and on change).</summary>
	public static void ApplyThemeMode(int mode)
	{
		var theme = mode switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.Unspecified };
		if (Application.Current is { } app)
			app.UserAppTheme = theme;
		Infrastructure.ChatAppearance.Refresh();
	}

	private static void ApplySavedThemeMode()
	{
		try { ApplyThemeMode(Microsoft.Maui.Storage.Preferences.Default.Get(ThemeModePreferenceKey, 0)); }
		catch { /* best-effort — a bad/absent preference must never block startup */ }
	}

	/// <summary>Preference key for the user-chosen font-size scale (2026-09-24, Phase 4). 0 = normal; see the choices in SettingsViewModel.</summary>
	public static string FontScalePreferenceKey => Profiles.ActiveProfile.PrefKey("app_font_scale");

	private const double BaseFontSize = 11.0;

	/// <summary>Rescales app text live. Index 0..3 = 0.9 / 1.0 / 1.15 / 1.3. On Android every text control is scaled natively (see Infrastructure.FontScaling), so AppFontSize stays at its base there to avoid scaling twice.</summary>
	public static void ApplyFontScale(int index)
	{
		var factor = Infrastructure.FontScaling.FactorFor(index);
		if (Application.Current is not { } app) return;
#if ANDROID
		app.Resources["AppFontSize"] = BaseFontSize;
		Infrastructure.FontScaling.SetFactor(factor);
#elif WINDOWS
		// 2026-10-14, user's own ask: "zvetsi pismo na pc je skoro necitelne" — this app's literal
		// FontSize values throughout (9/9.5/10.5/11pt etc.) were all tuned against a PHONE's DPI, so
		// they render tiny on a desktop monitor. Stacking a Windows-only multiplier on top of the
		// user's own slider factor (rather than just raising BaseFontSize) keeps index 1 == "normal
		// for this platform" on every platform, while making Windows' "normal" noticeably bigger than
		// the phone's. Needs the SAME native per-control scaling Android already has (see
		// Infrastructure.FontScaling) since most pages set a literal FontSize the AppFontSize
		// resource alone never touches.
		app.Resources["AppFontSize"] = BaseFontSize;
		Infrastructure.FontScaling.SetFactor(factor * WindowsBaselineMultiplier);
#else
		app.Resources["AppFontSize"] = BaseFontSize * factor;
#endif
	}

	private const double WindowsBaselineMultiplier = 1.4;

	private static void ApplySavedFontScale()
	{
		try { ApplyFontScale(Microsoft.Maui.Storage.Preferences.Default.Get(FontScalePreferenceKey, 1)); }
		catch { /* best-effort */ }
	}

	public App()
	{
		InitializeComponent();

		// 2026-10-05, Windows multi-profile login (Profiles.ActiveProfile) — while no profile has
		// been chosen yet, CreateWindow below shows ONLY the login/picker page, never AppShell, so
		// none of this constructor's own background work (connection supervisor, DB warm-up, etc.)
		// has anything real to operate on yet — it would just touch the default/un-profiled data
		// directory for a session that's about to restart the moment a profile IS chosen anyway.
		// Skipped entirely in that case; once a profile is active (the normal case on every other
		// platform, and on Windows after login), everything below runs exactly as before this
		// feature existed.
		if (Profiles.ActiveProfile.RequiresLogin) return;

		// Phase 4 (easy customization) — apply the user's saved light/dark/system choice AND their
		// chosen accent colour before any window is built, so the app opens already themed rather
		// than flashing the default first.
		ApplySavedThemeMode();
		ApplySavedFontScale();
		Infrastructure.AccentPalette.ApplySaved();
		Infrastructure.ChatAppearance.ApplySaved();
		RequestedThemeChanged += (_, _) => Infrastructure.ChatAppearance.Refresh();

		// Shared diagnostics log (2026-09-10) — a genuine crash is exactly the class of failure
		// this log exists for (see IDiagnosticsReporter's own remarks): the one thing worse than an
		// error nobody can see remotely is a crash that kills the process before anything ELSE gets
		// a chance to report it. Both handlers are itself best-effort (the reporter never throws —
		// see HttpDiagnosticsReporter's own remarks — but resolving it from DI theoretically could
		// if the container itself is in a bad state, hence the outer try/catch here too).
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

		// Notification Hub (2026-09-20, Phase 8, spec §31): a tap on a real OS notification while
		// the app is already running (MainActivity.OnNewIntent, "warm" case — see
		// NativeNotificationRouter's own remarks) opens that notification's own detail, not just
		// the app's home screen. Subscribed here, in the constructor, so it's guaranteed to already
		// be listening well before any notification could possibly be tapped.
		Notifications.NativeNotificationRouter.NotificationTapped += OnNativeNotificationTapped;
		Notifications.PendingWidgetRouteRouter.RouteRequested += OnWidgetRouteRequested;

		// 2026-09-06 pairing simplification: whichever side is online when the other calls
		// CreateSessionAsync now gets the invite delivered automatically (see
		// NewChatViewModel.CreateSessionAsync's own remarks) instead of needing a second manual
		// QR/copy-paste round trip. Subscribed here (once, for the app's whole lifetime) rather
		// than only while NewChatPage happens to be open — the other person might complete pairing
		// at any time, not just while you're looking at that screen.
		var transport = IPlatformApplication.Current?.Services.GetService<IMessageTransport>();
		if (transport is not null)
		{
			// Awaited by the receive loop (2026-10-23), not a fire-and-forget event: the messages
			// queued right behind an invite must find the session it creates — see
			// IMessageTransport.PairingInviteProcessor's remarks.
			transport.PairingInviteProcessor = OnPairingInviteReceived;
			transport.GroupInviteReceived += OnGroupInviteReceived;

			// 2026-10-24: the relay pushes "board-changed" when another device replaces the shared Opicentrum
			// roster/volno snapshot — refresh the local copy in the background so opening the board never
			// waits on the relay. The connection state is tracked because a drop may have cost a push.
			transport.BoardChanged += (_, board) => Workplace.OpicentrumBoardSync.OnBoardChanged(board);
			transport.ConnectionStateChanged += (_, state) => Workplace.OpicentrumBoardSync.OnConnectionStateChanged(state);
			Workplace.OpicentrumBoardSync.OnConnectionStateChanged(transport.IsConnected ? TransportConnectionState.Connected : TransportConnectionState.Disconnected);

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
			{
				// Measured (2026-10-24) so the cost of the first open (vault key, SQLCipher key setup, migration
				// check) shows up in the metrics instead of being guessed at.
				var sw = System.Diagnostics.Stopwatch.StartNew();
				await factory.EnsureInitializedAsync();
				AppLog.Metric("db.open", sw.Elapsed.TotalMilliseconds);
			}
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
		switch (level)
		{
			case DiagnosticLogLevel.Critical: AppLog.Critical(context, message, exception); break;
			case DiagnosticLogLevel.Warning: AppLog.Warning(context, message, exception); break;
			default: AppLog.Error(context, $"[{level}] {message}", exception); break;
		}

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
		if (e.IsTerminating)
		{
			// 2026-10-16 (user's ask: crashes into the system chat, once, with a count): the process is
			// dying, so a relay call from here usually never completes. Park the crash in Preferences
			// (local, synchronous) and let the NEXT launch report it exactly once — see
			// ReportPendingCrashAsync — instead of firing a request that may or may not survive.
			AppLog.Critical(nameof(OnUnhandledException), "Neošetřená výjimka — aplikace se ukončuje.", exception);
			try
			{
				var first = (exception?.Message ?? string.Empty).ReplaceLineEndings(" ");
				var summary = $"{exception?.GetType().Name ?? "Neznámá výjimka"}: {(first.Length > 160 ? first[..160] : first)}";
				Microsoft.Maui.Storage.Preferences.Default.Set(PendingCrashPreferenceKey, summary);
			}
			catch { /* best-effort — the local errors.log above already has it */ }
			return;
		}
		ReportFireAndForget(DiagnosticLogLevel.Error, "Neošetřená výjimka.", nameof(OnUnhandledException), exception);
	}

	private const string PendingCrashPreferenceKey = "pending_crash_summary";

	/// <summary>Reports a crash parked by <see cref="OnUnhandledException"/> on the previous run as ONE Critical entry (with app version and OS), then forgets it. Runs from the supervisor sweep, i.e. only once the relay is connected.</summary>
	private static async Task ReportPendingCrashAsync(IServiceProvider services)
	{
		try
		{
			var prefs = Microsoft.Maui.Storage.Preferences.Default;
			var summary = prefs.Get(PendingCrashPreferenceKey, string.Empty);
			if (string.IsNullOrEmpty(summary)) return;

			var reporter = services.GetService<IDiagnosticsReporter>();
			if (reporter is null) return;

			var info = Microsoft.Maui.ApplicationModel.AppInfo.Current;
			await reporter.ReportAsync(DiagnosticLogLevel.Critical,
				$"Pád aplikace — {summary} | verze {info.VersionString} ({info.BuildString}) | {Microsoft.Maui.Devices.DeviceInfo.Platform} {Microsoft.Maui.Devices.DeviceInfo.VersionString}",
				AppCrashContext);
			prefs.Remove(PendingCrashPreferenceKey);
		}
		catch
		{
			// Best-effort — stays parked and is retried next sweep.
		}
	}

	private const string AppCrashContext = "AppCrash";

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
						// Make-before-break (2026-10-24): the old connection used to be torn down first, and the relay
						// still addressed it for the ~0.3 s until the new one authenticated — a frame handed to it in
						// that gap was gone for good (the relay does not queue a frame it "delivered" live). Now the new
						// socket connects and authenticates while the old one keeps serving, and ConnectAsync swaps them.
						// Only a successful swap restarts the timer; a failed one is retried on the next tick.
						if (await TryConnectAsync(services, transport))
							lastForcedReconnect = DateTimeOffset.UtcNow;
					}
					else if (!wasConnected)
					{
						await TryConnectAsync(services, transport);
					}

					// A periodic swap above is not a reconnect after an outage — only a real one triggers the extra sweep.
					var justReconnected = !wasConnected && transport.IsConnected;
					var sweepDue = DateTimeOffset.UtcNow - lastStaleSweep >= _staleSessionSweepInterval;

					if (transport.IsConnected && (justReconnected || sweepDue))
					{
						await RunStaleSessionSweepAsync(services, transport);
						await RunPeerIdentityReconciliationAsync(services, transport);
						await ApplyDevicePolicyAsync(services);
						await SyncBoardToNotificationsAsync(services);
						await ReportPendingCrashAsync(services);
						await SyncAdminAlertsAsync(services);
						// Both feed only the Android widget (PiccRosterStore / ContactPhoneCache are read nowhere else), and
						// change rarely — so not on Windows at all, and on Android not on every ~3-minute sweep.
#if ANDROID
						if (SweepStepDue(nameof(SyncPiccStatusAsync), TimeSpan.FromMinutes(10)))
							await SyncPiccStatusAsync(services);
#endif
						await Diagnostics.AppLogUploader.UploadAsync(services);
#if ANDROID
						if (SweepStepDue(nameof(Contacts.ContactPhoneCache), TimeSpan.FromMinutes(30))
							&& services.GetService<ISharedContactService>() is { } sharedContactService)
							await Contacts.ContactPhoneCache.RefreshAsync(sharedContactService);
#endif
						lastStaleSweep = DateTimeOffset.UtcNow;
					}
				}

				// Detached from the supervisor tick (2026-10-22): an Opicentrum scrape (login + several
				// pages, tens of seconds) used to run inline here, so a connection that dropped DURING it
				// wasn't re-established until it finished. Single-flight — a slow run just means the next
				// tick skips this block; each job still throttles itself via its own preference timestamp.
				if (services is not null && Interlocked.CompareExchange(ref _backgroundJobsRunning, 1, 0) == 0)
					_ = RunBackgroundJobsAsync(services);
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

	private static bool _profileSeeded;

	/// <summary>
	/// Windows profile registration (2026-10-26): the Jméno/Příjmení/E-mail typed when the profile was created are
	/// copied once into this profile's own User — only into fields that are still empty, so anything the user has
	/// since edited in Nastavení → Účet is never overwritten. No-op for profiles without registration data.
	/// </summary>
	private static async Task SeedProfileFromRegistrationAsync(IServiceProvider services)
	{
		if (_profileSeeded) return;
		if (Profiles.ActiveProfile.Current is not { } profile
			|| (string.IsNullOrWhiteSpace(profile.Email) && string.IsNullOrWhiteSpace(profile.FirstName)))
		{
			_profileSeeded = true;
			return;
		}

		using var scope = services.CreateScope();
		var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
		await currentUser.InitializeAsync();
		var user = currentUser.Current;
		await currentUser.UpdateProfileAsync(
			string.IsNullOrWhiteSpace(user.FirstName) ? profile.FirstName : null,
			string.IsNullOrWhiteSpace(user.LastName) ? profile.LastName : null,
			null,
			string.IsNullOrWhiteSpace(user.Email) ? profile.Email : null);
		_profileSeeded = true;
	}

	private const string PublishedEmailPreferenceKey = "published_person_email";

	/// <summary>
	/// One person, several devices (2026-10-26): reports this device's e-mail (Nastavení → Účet / the PC registration) to
	/// the relay, which links every device with the same e-mail and lets a new one start with the person's role. Sent once
	/// per e-mail value; a failure just means the next tick tries again. Per profile on a shared PC.
	/// </summary>
	private static async Task PublishOwnEmailAsync(IServiceProvider services)
	{
		try
		{
			using var scope = services.CreateScope();
			var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
			await currentUser.InitializeAsync();
			var email = currentUser.Current.Email?.Trim();
			if (string.IsNullOrEmpty(email)) return;

			var key = Profiles.ActiveProfile.PrefKey(PublishedEmailPreferenceKey);
			if (string.Equals(Microsoft.Maui.Storage.Preferences.Default.Get(key, string.Empty), email, StringComparison.OrdinalIgnoreCase)) return;

			var policyService = scope.ServiceProvider.GetService<IDevicePolicyService>();
			if (policyService is not null && await policyService.PublishMyEmailAsync(email))
				Microsoft.Maui.Storage.Preferences.Default.Set(key, email);
		}
		catch (Exception ex)
		{
			SecureApp.Presentation.Infrastructure.AppLog.Warning(nameof(App), "publishing the person e-mail failed", ex);
		}
	}

	private static int _backgroundJobsRunning;

	private static async Task RunBackgroundJobsAsync(IServiceProvider services)
	{
		try
		{
			await SeedProfileFromRegistrationAsync(services);
			await PublishOwnEmailAsync(services);
			await RunBackgroundOpicentrumSyncAsync(services);
			await RunBackgroundStaffingGapCheckAsync(services);
			await RunBackgroundOpicentrumRosterBoardPublishAsync(services);
			await RunBackgroundOpicentrumVolnoBoardPublishAsync(services);
			await RunBackgroundUpdateCheckAsync(services);
			await RunBackgroundCmsAnnotationCheckAsync(services);
		}
		catch
		{
			// Each job already logs its own failure; one bad run is simply retried on a later tick.
		}
		finally
		{
			Volatile.Write(ref _backgroundJobsRunning, 0);
		}
	}

	private const string LastBackgroundOpicentrumSyncPreferenceKey = "opicentrum_last_bg_sync_utc";
	private static readonly TimeSpan _backgroundOpicentrumSyncInterval = TimeSpan.FromMinutes(10);
	private static readonly TimeSpan _backgroundOpicentrumFullSyncInterval = TimeSpan.FromMinutes(60);
	private const string LastBackgroundOpicentrumFullSyncPreferenceKey = "opicentrum_last_bg_full_sync_utc";

	/// <summary>22:00–05:00 local time: the background jobs that only exist to surface a change quickly slow down or pause.</summary>
	private static bool IsNightHour() => DateTime.Now.Hour is >= 22 or < 5;

	/// <summary>
	/// Opicentrum sync without opening the Rozpis (2026-09-26): with the background service keeping the
	/// process alive, this is what lets a schedule change reach the user as a notification on its own,
	/// and keeps the widget's duty list fresh. Every ~10 minutes (2026-09-29: tightened from 3h so a
	/// schedule change surfaces promptly), last week through the end of next month.
	/// </summary>
	private static async Task RunBackgroundOpicentrumSyncAsync(IServiceProvider services)
	{
		try
		{
			var prefs = Microsoft.Maui.Storage.Preferences.Default;
			var lastTicks = prefs.Get(LastBackgroundOpicentrumSyncPreferenceKey, 0L);
			if (lastTicks > 0 && DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero) < (IsNightHour() ? _backgroundOpicentrumNightSyncInterval : _backgroundOpicentrumSyncInterval)) return; // night (22–05): nobody reads a schedule change then, so the portal is asked hourly

			var sync = services.GetService<IOpicentrumSyncService>();
			if (sync is null || !await sync.HasCredentialsAsync()) return;
			prefs.Set(LastBackgroundOpicentrumSyncPreferenceKey, DateTimeOffset.UtcNow.UtcTicks);

			var today = DateOnly.FromDateTime(DateTime.Today);
			// Two tiers (2026-10-29, "zkontroluj zda se data nestahuji moc krat"): the near window — last week to three weeks ahead, ~4 weekly
				// pages — every tick; the whole range through the end of next month (~9 weekly pages plus the monthly duty and leave
				// pages) only once an hour. A change far ahead surfaces within the hour.
				var fullLastTicks = prefs.Get(LastBackgroundOpicentrumFullSyncPreferenceKey, 0L);
				var fullDue = fullLastTicks <= 0 || DateTimeOffset.UtcNow - new DateTimeOffset(fullLastTicks, TimeSpan.Zero) >= _backgroundOpicentrumFullSyncInterval;
				var rangeEnd = today.AddDays(21);
				if (fullDue)
				{
					rangeEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(2).AddDays(-1);
					prefs.Set(LastBackgroundOpicentrumFullSyncPreferenceKey, DateTimeOffset.UtcNow.UtcTicks);
				}
			var result = await sync.SyncAsync(today.AddDays(-7), rangeEnd);
			AppLog.Event("opicentrum.bg-sync", ("ok", result.Success), ("full", fullDue), ("created", result.CreatedCount), ("updated", result.UpdatedCount));
		}
		catch (Exception ex)
		{
			AppLog.Error(nameof(RunBackgroundOpicentrumSyncAsync), "background Opicentrum sync failed", ex);
		}
	}

	private const string LastStaffingGapCheckPreferenceKey = "staffing_gap_last_check_utc";
	private static readonly TimeSpan _staffingGapCheckInterval = TimeSpan.FromMinutes(60);
	// Night sync is hourly, so the doctors' weeks the check reuses are up to an hour old — and a hole found at 3 a.m. helps nobody.
	private static readonly TimeSpan _backgroundOpicentrumNightSyncInterval = TimeSpan.FromMinutes(60);

	/// <summary>
	/// Unstaffed-position alerts (2026-10-28, user's ask): on an Admin/Modifier device, every 30 min look at the doctors' and
	/// nurses' rosters for an empty afternoon shift (7 days ahead) and a workplace with a doctor but no nurse or the reverse
	/// (2 days ahead). Notifies once per hole and flags the day in the widget calendar. The checker itself ignores other roles.
	/// </summary>
	private static async Task RunBackgroundStaffingGapCheckAsync(IServiceProvider services)
	{
		try
		{
			var prefs = Microsoft.Maui.Storage.Preferences.Default;
			if (IsNightHour()) return;
				var key = Profiles.ActiveProfile.PrefKey(LastStaffingGapCheckPreferenceKey);
			var lastTicks = prefs.Get(key, 0L);
			if (lastTicks > 0 && DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero) < _staffingGapCheckInterval) return;

			var checker = services.GetService<Workplace.StaffingGapChecker>();
			if (checker is null) return;
			prefs.Set(key, DateTimeOffset.UtcNow.UtcTicks);

			var gaps = await checker.CheckAsync();
			AppLog.Event("staffing-gaps.check", ("gaps", gaps));
		}
		catch (Exception ex)
		{
			AppLog.Error(nameof(RunBackgroundStaffingGapCheckAsync), "staffing gap check failed", ex);
		}
	}

	/// <summary>
	/// Keeps a relay-cached Opicentrum board (roster 2026-10-21, volno same day) fresh WITHOUT every
	/// device scraping opicentrum.cz itself — the user's own explicit efficiency ask ("nevytěžovat"
	/// the external site). Checks the relay's own cached snapshot first; only actually logs in and
	/// scrapes when that snapshot is missing or older than <paramref name="staleAfter"/>, so on most
	/// ticks, most devices, this does nothing at all. The per-device throttle on top keeps a device
	/// that can't reach the relay (not configured yet) from retrying every ~10 s tick.
	/// </summary>
	private static async Task RunBackgroundOpicentrumBoardPublishAsync<TCache, TSnapshot>(
		IServiceProvider services, string name, string lastAttemptPreferenceKey, TimeSpan staleAfter,
		Func<TCache, Task<TSnapshot?>> getCached, Func<TCache, TSnapshot, Task> publish,
		Func<IOpicentrumSyncService, DateOnly, Task<TSnapshot>> fetch, Func<TSnapshot, DateTimeOffset> updatedAt,
		Action<TSnapshot> saveLocal, Action<TSnapshot> logPublished)
		where TCache : class where TSnapshot : class
	{
		try
		{
			var prefs = Microsoft.Maui.Storage.Preferences.Default;
			var lastTicks = prefs.Get(lastAttemptPreferenceKey, 0L);
			if (lastTicks > 0 && DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero) < TimeSpan.FromMinutes(5)) return;

			var sync = services.GetService<IOpicentrumSyncService>();
			var cache = services.GetService<TCache>();
			if (sync is null || cache is null || !await sync.HasCredentialsAsync()) return;
			prefs.Set(lastAttemptPreferenceKey, DateTimeOffset.UtcNow.UtcTicks);

			var cached = await getCached(cache);
			if (cached is not null && DateTimeOffset.UtcNow - updatedAt(cached) < staleAfter) return;

			var snapshot = await fetch(sync, DateOnly.FromDateTime(DateTime.Today));
			await publish(cache, snapshot);
			saveLocal(snapshot);
			logPublished(snapshot);
		}
		catch (Exception ex)
		{
			AppLog.Error(nameof(RunBackgroundOpicentrumBoardPublishAsync), $"background {name} board publish failed", ex);
		}
	}

	private static Task RunBackgroundOpicentrumRosterBoardPublishAsync(IServiceProvider services) =>
		RunBackgroundOpicentrumBoardPublishAsync<IOpicentrumRosterBoardService, OpicentrumRosterBoardSnapshot>(
			services, "roster", "opicentrum_roster_board_last_bg_attempt_utc", TimeSpan.FromMinutes(15),
			c => c.GetCachedAsync(), (c, s) => c.PublishAsync(s),
			// The relay's one roster snapshot is the week the board opens on (from Saturday the next one, 2026-10-24):
			// the previous week's counts as stale at once, so the flip happens on the first tick after midnight.
			(sync, today) => sync.FetchFullRosterBoardAsync(Workplace.WeekMath.DefaultViewWeek(today)),
			s => s.WeekOf == Workplace.WeekMath.DefaultViewWeek(DateOnly.FromDateTime(DateTime.Today)) ? s.UpdatedAtUtc : DateTimeOffset.MinValue,
			Workplace.OpicentrumRosterBoardLocalCache.Save,
			s => AppLog.Event("opicentrum.roster-board.bg-publish", ("weekOf", s.WeekOf.ToString()), ("days", s.Days.Count)));

	// Longer staleness window than the roster: leave changes less often than room assignments.
	private static Task RunBackgroundOpicentrumVolnoBoardPublishAsync(IServiceProvider services) =>
		RunBackgroundOpicentrumBoardPublishAsync<IOpicentrumVolnoBoardService, OpicentrumVolnoSnapshot>(
			services, "volno", "opicentrum_volno_board_last_bg_attempt_utc", TimeSpan.FromMinutes(30),
			c => c.GetCachedAsync(), (c, s) => c.PublishAsync(s),
			(sync, today) => sync.FetchVolnoForMonthAsync(today), s => s.UpdatedAtUtc,
			Workplace.OpicentrumVolnoBoardLocalCache.Save,
			s => AppLog.Event("opicentrum.volno-board.bg-publish", ("year", s.Year), ("month", s.Month), ("people", s.People.Count)));

	private const string LastBackgroundUpdateCheckPreferenceKey = "update_last_bg_check_utc";
	private const string AutoHandledUpdateVersionPreferenceKey = "update_auto_handled_version_code";
	private static readonly TimeSpan _backgroundUpdateCheckInterval = TimeSpan.FromMinutes(30);

	/// <summary>
	/// Automatic background update download (2026-10-19, user's own ask: "pokud je na wifi rovnou
	/// stahni v pozadi aktualizaci a po stazeni posli jednorazove upozorneni" — every user, not just
	/// whoever remembers to open Settings and tap "Zkontrolovat aktualizaci"). Checked at most every
	/// 30 min, and only ever acted on over WiFi — a sideloaded APK or a Windows build can be a sizeable
	/// download, never spent on someone's mobile data automatically. <see cref="AutoHandledUpdateVersionPreferenceKey"/>
	/// records the newest version code already downloaded/notified so the SAME release is never
	/// re-downloaded or re-notified on every later tick — only a genuinely NEWER version code triggers
	/// this again.
	///
	/// Android hands straight off to <see cref="INativeUpdateDownloader"/> (the existing foreground
	/// Service from 2026-09-24) — it already owns its own progress notification and posts exactly one
	/// "ready to install" notification on completion, so nothing more is needed here. Everywhere else
	/// (Windows — no such service exists there) this downloads the update itself, right in this
	/// background tick, and posts a single <see cref="NotificationPublisher.PublishUpdateReadyAsync"/>
	/// — deliberately NOT auto-installing: on Windows that closes and relaunches the whole app with no
	/// warning (see <c>SettingsViewModel.DownloadAndInstallUpdateAsync</c>'s own remarks), which is fine
	/// for an explicit user tap but not for something firing unattended in the background.
	/// </summary>
	private static async Task RunBackgroundUpdateCheckAsync(IServiceProvider services)
	{
		try
		{
			var prefs = Microsoft.Maui.Storage.Preferences.Default;
			var lastTicks = prefs.Get(LastBackgroundUpdateCheckPreferenceKey, 0L);
			if (lastTicks > 0 && DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero) < _backgroundUpdateCheckInterval) return;

			if (!Microsoft.Maui.Networking.Connectivity.Current.ConnectionProfiles.Contains(Microsoft.Maui.Networking.ConnectionProfile.WiFi))
				return;

			var updateService = services.GetService<IUpdateService>();
			if (updateService is null) return;

			prefs.Set(LastBackgroundUpdateCheckPreferenceKey, DateTimeOffset.UtcNow.UtcTicks);

			var result = await updateService.CheckForUpdateAsync();
			if (!result.IsUpdateAvailable || result.LatestVersionCode is not { } versionCode) return;

			var alreadyHandled = prefs.Get(AutoHandledUpdateVersionPreferenceKey, 0);
			if (alreadyHandled >= versionCode) return;
			prefs.Set(AutoHandledUpdateVersionPreferenceKey, versionCode);

			var versionName = result.LatestVersionName ?? string.Empty;
			var nativeDownloader = services.GetService<INativeUpdateDownloader>();
			if (nativeDownloader is not null)
			{
				var url = await updateService.GetDownloadUrlAsync();
				nativeDownloader.StartBackgroundDownload(url, versionCode, versionName);
				AppLog.Event("update.bg-download.started", ("platform", "native-service"), ("versionCode", versionCode));
				return;
			}

			await updateService.DownloadUpdateAsync();
			AppLog.Event("update.bg-download.done", ("platform", "in-process"), ("versionCode", versionCode));

			using var scope = services.CreateScope();
			var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
			var native = services.GetService<INativeNotificationService>();
			await NotificationPublisher.PublishUpdateReadyAsync(notifications, versionName, native);
		}
		catch (Exception ex)
		{
			AppLog.Error(nameof(RunBackgroundUpdateCheckAsync), "background update check/download failed", ex);
		}
	}

	private const string LastBackgroundCmsAnnotationCheckPreferenceKey = "cms_annotation_last_bg_check_utc";
	private const string LastCmsAnnotationStatesPreferenceKey = "cms_annotation_last_seen_states";
	private static readonly TimeSpan _backgroundCmsAnnotationCheckInterval = TimeSpan.FromMinutes(30);

	/// <summary>
	/// CMS_PLAN.md phase 5's "notifikace autorovi": no cross-device push exists anywhere in this app
	/// (see <see cref="NotificationPublisher.PublishCmsAnnotationModeratedAsync"/>'s own remarks), so the
	/// author's own device notices a moderation decision on its own annotations the same way
	/// <see cref="RunBackgroundOpicentrumSyncAsync"/> notices its own schedule changed: poll, diff
	/// against the last-seen state per annotation id (stored as JSON in Preferences, since there can be
	/// several), and notify only on an actual transition away from "pending".
	/// </summary>
	private static async Task RunBackgroundCmsAnnotationCheckAsync(IServiceProvider services)
	{
		try
		{
			var prefs = Microsoft.Maui.Storage.Preferences.Default;
			var lastTicks = prefs.Get(LastBackgroundCmsAnnotationCheckPreferenceKey, 0L);
			if (lastTicks > 0 && DateTimeOffset.UtcNow - new DateTimeOffset(lastTicks, TimeSpan.Zero) < _backgroundCmsAnnotationCheckInterval) return;

			var annotations = services.GetService<ICmsAnnotationService>();
			if (annotations is null) return;
			prefs.Set(LastBackgroundCmsAnnotationCheckPreferenceKey, DateTimeOffset.UtcNow.UtcTicks);

			var mine = await annotations.ListMyModeratedAsync();

			var storedJson = prefs.Get(LastCmsAnnotationStatesPreferenceKey, "{}");
			var previouslySeen = System.Text.Json.JsonSerializer.Deserialize<Dictionary<Guid, string>>(storedJson) ?? [];

			if (mine.Count > 0)
			{
				using var scope = services.CreateScope();
				var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
				var native = services.GetService<INativeNotificationService>();

				foreach (var a in mine)
				{
					var seenBefore = previouslySeen.TryGetValue(a.AnnotationId, out var previous) ? previous : "none";
					if (seenBefore == "pending" && a.Moderation is "incorporated" or "declined" or "kept")
						await NotificationPublisher.PublishCmsAnnotationModeratedAsync(notifications, a.DocumentTitle, a.Moderation, native);
				}
			}

			// Rebuilt from scratch every run (not merged into the old dict) so an annotation that has
			// fallen out of ListMyModeratedAsync's own top-200-by-recency window stops being tracked here
			// too — otherwise this JSON blob in Preferences would grow forever, one entry per annotation
			// ever seen, never shrinking.
			var freshSeen = mine.ToDictionary(a => a.AnnotationId, a => a.Moderation);
			prefs.Set(LastCmsAnnotationStatesPreferenceKey, System.Text.Json.JsonSerializer.Serialize(freshSeen));
		}
		catch (Exception ex)
		{
			AppLog.Error(nameof(RunBackgroundCmsAnnotationCheckAsync), "background CMS annotation check failed", ex);
		}
	}

	/// <summary>
	/// The actual connect attempt, called from every supervisor tick that finds the transport
	/// disconnected. Respects <c>IsAutoConnectEnabled</c>. On a <see cref="Transport.RelayUnauthorizedException"/>
	/// (relay explicitly rejected this device's credentials — registration was deleted or rotated)
	/// the credentials are cleared and a silent re-activation request is submitted automatically;
	/// no error is ever shown to the user, everything goes to the log.
	/// </summary>
	private static async Task<bool> TryConnectAsync(IServiceProvider services, IMessageTransport transport)
	{
		using var scope = services.CreateScope();
		var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
		var configuration = await transportSettings.GetAsync();
		if (configuration is not { AssignedDeviceId: not null, IsAutoConnectEnabled: true, EndpointUri: { } endpoint })
			return false;

		try
		{
			await transport.ConnectAsync(endpoint);
			var failedBefore = Interlocked.Exchange(ref _consecutiveConnectFailures, 0);
			if (failedBefore > 0)
				SecureApp.Presentation.Infrastructure.AppLog.Event("relay.reconnected", ("afterFailures", failedBefore));
			else
				SecureApp.Presentation.Infrastructure.AppLog.Event("relay.reconnected");
			return true;
		}
		catch (SecureApp.Presentation.Transport.RelayUnauthorizedException)
		{
			// Credentials were explicitly rejected — device registration was deleted or rotated.
			// Clear stored credentials and silently self-register a fresh one (SilentReactivateAsync
			// below already bypasses admin approval entirely). No user-visible error.
			SecureApp.Presentation.Infrastructure.AppLog.Event("relay.unauthorized.reregistering");
			try { await transport.ClearCredentialsAsync(); }
			catch (Exception clearEx) { SecureApp.Presentation.Infrastructure.AppLog.Error("App.TryConnect", "failed to clear stale credentials", clearEx); }
			try { await SilentReactivateAsync(services, transport); }
			catch (Exception reactivateEx) { SecureApp.Presentation.Infrastructure.AppLog.Error("App.TryConnect", "silent re-activation failed", reactivateEx); }
			return false;
		}
		catch (Exception ex)
		{
			// Transient network failure — retried next tick (~10s). Logged so a persistently failing
			// reconnect (why messages aren't leaving) is diagnosable — but the first failure and then at most
			// once per _connectFailureLogInterval (2026-10-24): a phone outside the VPN failed this ~7 000
			// times a day with a full stack trace each, which filled the 512 KB error log in two hours, pushed
			// every other error out of it and was then uploaded line by line.
			var failures = Interlocked.Increment(ref _consecutiveConnectFailures);
			var now = DateTimeOffset.UtcNow;
			if (failures == 1 || now - _lastConnectFailureLoggedUtc >= _connectFailureLogInterval)
			{
				_lastConnectFailureLoggedUtc = now;
				SecureApp.Presentation.Infrastructure.AppLog.Error("App.TryConnect", $"relay reconnect attempt failed ({failures} in a row)", ex);
			}
			return false;
		}
	}

	private static int _consecutiveConnectFailures;
	private static DateTimeOffset _lastConnectFailureLoggedUtc;
	private static readonly TimeSpan _connectFailureLogInterval = TimeSpan.FromMinutes(10);

	private static readonly Dictionary<string, DateTimeOffset> _sweepStepLastRunUtc = new();

	/// <summary>Per-process throttle for the sweep steps that do not need to run on every ~3-minute pass; the first call after launch always runs. The sweep itself is single-threaded (the supervisor loop), so no locking is needed.</summary>
	private static bool SweepStepDue(string step, TimeSpan every)
	{
		var now = DateTimeOffset.UtcNow;
		if (_sweepStepLastRunUtc.TryGetValue(step, out var last) && now - last < every)
			return false;
		_sweepStepLastRunUtc[step] = now;
		return true;
	}

	/// <summary>
	/// Called when the relay explicitly rejected stored credentials (registration was deleted).
	/// Self-registers instantly via <see cref="IMessageTransport.SelfRegisterAsync"/> — WireGuard is
	/// the trust boundary so no admin approval is required. The next supervisor tick after this
	/// returns will find valid credentials and connect normally. No UI touched; everything to log.
	/// </summary>
	private static async Task SilentReactivateAsync(IServiceProvider services, IMessageTransport transport)
	{
		using var scope = services.CreateScope();
		var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
		await currentUser.InitializeAsync();

		var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
		var configuration = await transportSettings.GetAsync();
		if (configuration?.EndpointUri is not { } endpoint) return;

		await transport.SelfRegisterAsync(endpoint, currentUser.Current.DisplayName);
		SecureApp.Presentation.Infrastructure.AppLog.Event("relay.auto.reregistered");
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
		var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

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

		// Invites a resync could not hand to the relay (it was down at that moment) go out now — before
		// anything below, so they reach the peer ahead of any message retried in this same sweep.
		try { await SessionRecoveryHelper.FlushPendingInvitesAsync(transport, sessionRepository); }
		catch { /* retried next sweep */ }

		IReadOnlyList<ChatSession> sessions;
		try { sessions = await sessionRepository.GetAllAsync(); }
		catch { return; }

		ReportChatHealth(sessions);
		ReportStuckPairings(sessions);

		// 2026-10-04: no longer excludes a peer in RemovedPeersStore — see OnPairingInviteReceived's
		// own remarks on why that gate is gone entirely (closed, personally-vetted community; nothing
		// requires manual consent to resume talking to someone already in it).
		// 2026-10-16 ("chat musi byt automaticky navazany... uzivatel nesmi poznat ze se neco deje"):
		// a peer whose newest session has sat in PendingHandshake for over an hour is retried too, not
		// only all-Closed peers — live case: Vilém's S23+ waited 7+ hours on a handshake the peer never
		// answered (peer's app was crash-looping), and nothing ever re-sent the invite afterwards. Each
		// retry mints a new session (fresh ModifiedAtUtc), so this fires at most once an hour per peer.
		var pendingRetryAfter = TimeSpan.FromHours(1);
		var stalePeers = sessions
			.GroupBy(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey))
			.Where(group => group.All(s => s.State == ChatSessionState.Closed)
				|| (!group.Any(s => s.State == ChatSessionState.Active)
					&& group.Where(s => s.State == ChatSessionState.PendingHandshake).Any()
					&& DateTimeOffset.UtcNow - group.Where(s => s.State == ChatSessionState.PendingHandshake).Max(s => s.ModifiedAtUtc) >= pendingRetryAfter))
			.Select(group => group.OrderByDescending(s => s.ModifiedAtUtc).First())
			.ToList();

		if (stalePeers.Count > 0)
			AppLog.Event("sweep.stale-peers-resyncing", ("count", stalePeers.Count));

		foreach (var stale in stalePeers)
		{
			if (stale.PeerRelayDeviceId is not { } relayDeviceId)
				continue;

			try
			{
				await SessionRecoveryHelper.ResyncAsync(
					messagingService, transport, transportSettings, currentUserService, messageRepository, sessionRepository,
					stale.PeerDisplayName, stale.PeerIdentityPublicKey, relayDeviceId);
			}
			catch (Exception ex)
			{
				// Best-effort — retried again next sweep. Reported at Warning (not Error) since
				// this is an expected, self-correcting retry loop, not a surprise.
				ReportFireAndForget(DiagnosticLogLevel.Warning, $"Pozadí: obnovení relace s {stale.PeerDisplayName} se nepodařilo, zkusí se znovu při dalším průchodu.", nameof(RunStaleSessionSweepAsync), ex);
			}
		}

		// Messages that never left the device (relay was down when they were written, or the live send
		// threw) used to sit at 🕓 forever — nothing retried them (2026-10-22, "apka musi vyresit sama").
		try
		{
			await SessionRecoveryHelper.RetryUnsentOutboundAsync(messagingService, messageRepository, sessionRepository, transport, _unsentRetryMinAge);
		}
		catch (Exception ex)
		{
			ReportFireAndForget(DiagnosticLogLevel.Warning, "Pozadí: opakované odeslání neodeslaných zpráv se nepodařilo, zkusí se znovu při dalším průchodu.", nameof(RunStaleSessionSweepAsync), ex);
		}
	}

	/// <summary>A message younger than this is probably being sent by an open chat page right now — the unsent-retry sweep leaves it alone.</summary>
	private static readonly TimeSpan _unsentRetryMinAge = TimeSpan.FromSeconds(30);

	/// <summary>
	/// Structured chat-pairing health, emitted every stale-sweep tick (2026-10-04, user's own ask: "aby
	/// server vedel ze je vse ok a funguje bez nutnosti sahat do ciziho telefonu" — the admin/server
	/// should be able to tell whether 1:1 pairing is actually healthy without needing physical access
	/// to a peer's phone). Reuses the exact pipeline that already carries every other AppLog.Event to
	/// the relay (Diagnostics.AppLogUploader.UploadAsync, called right after this sweep) — no new
	/// transport, just a signal that was missing. Before this, diagnosing a stuck pairing meant
	/// grepping raw session.resync.* lines by hand across both devices' uploaded logs and reconstructing
	/// the timeline (exactly what surfaced the real 16+-hour stuck-pairing bug this method exists to
	/// make visible going forward); now "chat.health" alone tells the whole story every ~3 minutes: how
	/// many sessions are Active, how many are PendingHandshake, and for how long the oldest one has
	/// been stuck there without ever reaching Active — a healthy handshake settles in seconds, so
	/// anything still pending after several sweeps is a real signal.
	///
	/// Originally also reported <c>PendingInvitesStore</c>'s own consent-pending queue — removed the
	/// same day this was written, once that store itself became permanently empty: the "hold a
	/// removed peer's re-invite for consent" behavior it backed was the actual root cause of the real
	/// stuck pairing that motivated this diagnostic in the first place, and is now gone entirely (see
	/// <c>App.OnPairingInviteReceived</c>'s own remarks) rather than something to keep reporting on.
	/// </summary>
	private static void ReportChatHealth(IReadOnlyList<ChatSession> sessions)
	{
		try
		{
			var now = DateTimeOffset.UtcNow;
			var active = sessions.Count(s => s.State == ChatSessionState.Active);
			var pending = sessions.Where(s => s.State == ChatSessionState.PendingHandshake).ToList();
			var closed = sessions.Count(s => s.State == ChatSessionState.Closed);
			var oldestPendingMinutes = pending.Count > 0
				? (int)(now - pending.Min(s => s.ModifiedAtUtc)).TotalMinutes
				: 0;

			AppLog.Event("chat.health",
				("active", active),
				("pendingHandshake", pending.Count),
				("pendingHandshakeOldestMinutes", oldestPendingMinutes),
				("closedOnly", closed));
		}
		catch
		{
			// Best-effort — a diagnostics signal failing to compute must never block the real
			// resync work right after it.
		}
	}

	/// <summary>
	/// Applies the admin-assigned policy this device just fetched from the relay (2026-09-24): its
	/// role and which tabs it may not show. Runs on every reconnect/sweep so a change an admin makes
	/// takes effect on the member's next connection with no action on their part. Best-effort — a
	/// null role (unmanaged) or any failure leaves the device's existing role/tabs untouched, so a
	/// network blip can never lock a member out.
	/// </summary>
	private static async Task ApplyDevicePolicyAsync(IServiceProvider services)
	{
		try
		{
			var policyService = services.GetService<IDevicePolicyService>();
			if (policyService is null) return;
			var policy = await policyService.GetMyPolicyAsync();

			// Role: only overwrite when the admin actually assigned one.
			if (policy.Role is { } assignedRole)
			{
				var currentUser = services.GetService<ICurrentUserService>();
				if (currentUser is not null)
				{
					await currentUser.InitializeAsync();
					if (currentUser.Current.Role != assignedRole)
						await currentUser.SetCurrentUserAsync(currentUser.Current.DisplayName, assignedRole);
				}
			}

			// Tab visibility: a tab is visible unless the policy lists its key as hidden. Applied on
			// the UI thread because ApplyTabVisibility rebuilds the live TabBar.
			var hideable = new[]
			{
				AppShell.ChatsTabVisibilityPreferenceKey,
				AppShell.FilesTabVisibilityPreferenceKey,
				AppShell.ContactsTabVisibilityPreferenceKey,
				AppShell.NotificationsTabVisibilityPreferenceKey,
				AppShell.LogbookVisibilityPreferenceKey,
			};
			// Redakce on a phone/tablet is for the Admin only (2026-10-26, user: "na mobilu kromě admina nikdo redakci
			// nemá") — hidden for everyone else there, whatever their role elsewhere. The device's own show/hide
			// preference is only touched while this rule applies (and given back once it stops), so it never
			// overrides a choice made in Nastavení on a PC or by an admin.
			var effectiveRole = policy.Role ?? services.GetService<ICurrentUserService>()?.Current.Role;
			var redakceBlockedHere = DeviceInfo.Idiom != DeviceIdiom.Desktop && effectiveRole != Role.Admin;
			MainThread.BeginInvokeOnMainThread(() =>
			{
				foreach (var key in hideable)
				{
					var visible = !policy.HiddenTabs.Contains(key);
					Microsoft.Maui.Storage.Preferences.Default.Set(key, visible);
					(Shell.Current as AppShell)?.ApplyTabVisibility(key, visible);
				}

				var prefs = Microsoft.Maui.Storage.Preferences.Default;
				var forcedKey = Profiles.ActiveProfile.PrefKey("redakce_forced_hidden");
				var forcedBefore = prefs.Get(forcedKey, false);
				if (redakceBlockedHere)
				{
					prefs.Set(forcedKey, true);
					(Shell.Current as AppShell)?.ApplyTabVisibility(AppShell.RedakceTabVisibilityPreferenceKey, false);
				}
				else if (forcedBefore)
				{
					prefs.Set(forcedKey, false);
					(Shell.Current as AppShell)?.ApplyTabVisibility(AppShell.RedakceTabVisibilityPreferenceKey, true);
				}
			});
		}
		catch
		{
			// Best-effort — see this method's own remarks; keep whatever the device already had.
		}
	}

	/// <summary>Today's on-call PICC doctor's name, cached for the Android widget's duty list (2026-10-12) — see <see cref="Workplace.PiccRosterStore"/>'s own remarks. Open to every device, same trust level as the board; best-effort like every other step in this loop.</summary>
	private static async Task SyncPiccStatusAsync(IServiceProvider services)
	{
		try
		{
			var piccRosterService = services.GetService<IPiccRosterService>();
			if (piccRosterService is null) return;

			var status = await piccRosterService.GetCurrentStatusAsync();
			Workplace.PiccRosterStore.Save(status?.DoctorName, status?.AsOfDate);
		}
		catch
		{
			// Best-effort — see this method's own remarks.
		}
	}

	// A device's first-ever board sync establishes a baseline of already-seen posts WITHOUT
	// notifying, so the whole history doesn't arrive as a burst of notifications; only posts that
	// appear after this point raise one. Stored as a CSV of post GUIDs in Preferences.
	private const string SeenBoardPostsPreferenceKey = "board_seen_ids";

	/// <summary>Turns any not-yet-seen notice-board post into a Nástěnka notification (2026-09-24). Runs on the same reconnect/sweep cadence as everything else, so a member sees board messages without opening any special screen.</summary>
	private static async Task SyncBoardToNotificationsAsync(IServiceProvider services)
	{
		try
		{
			var board = services.GetService<ICommunityBoardService>();
			if (board is null) return;

			var posts = await board.ListAsync(50);
			if (posts.Count == 0) return;

			var prefs = Microsoft.Maui.Storage.Preferences.Default;
			var seenRaw = prefs.Get(SeenBoardPostsPreferenceKey, string.Empty);
			var firstRun = string.IsNullOrEmpty(seenRaw);
			var seen = new HashSet<string>(seenRaw.Split(',', StringSplitOptions.RemoveEmptyEntries));

			using var scope = services.CreateScope();
			var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
			var native = services.GetService<INativeNotificationService>();

			foreach (var post in posts)
			{
				// Not decryptable yet (2026-10-12, real bug caught from a user report: "proč mi chodí
				// do zpráv, že [chybí] shared library key" — this device had no/the wrong shared
				// library key, so HttpCommunityBoardService.ListAsync came back with its own "🔒
				// (chybí klíč sdílené knihovny)" placeholder as post.Text, and THAT literal placeholder
				// was going out as a real Nástěnka notification, over and over, once per new post,
				// for as long as the key stayed missing). Skip it entirely — don't mark seen, don't
				// notify — so the id stays un-seen and gets reconsidered on every sweep; the moment
				// SharedLibraryKeySync's own automatic distribution lands the key, the very next sweep
				// sees real text and raises a genuine, correct notification instead of having already
				// permanently burned this id on a meaningless placeholder.
				if (!post.IsReadable) continue;

				var id = post.Id.ToString();
				if (!seen.Add(id)) continue;                 // already known
				if (firstRun) continue;                      // baseline only — don't notify for history
				await NotificationPublisher.PublishBoardMessageAsync(notifications, post.AuthorDisplayName, post.Text, native);
			}

			// Cap the stored set so it can't grow without bound; the newest 200 ids is plenty to
			// dedupe against given the board only ever serves the most recent posts.
			var trimmed = seen.Count > 200 ? seen.Skip(seen.Count - 200) : seen;
			prefs.Set(SeenBoardPostsPreferenceKey, string.Join(',', trimmed));
		}
		catch
		{
			// Best-effort — a failed board sync must never disturb the connection supervisor.
		}
	}

	private const string PairingWatchdogContext = "PairingWatchdog";
	private static readonly HashSet<string> _reportedStuckPairings = new();

	/// <summary>
	/// Pairing watchdog (2026-10-16, user's ask: "pokud nedojde ke sparovani po zadosti, posli me zpravu"):
	/// a handshake still pending after 15 min is reported as a Warning and after 60 min as an Error to the
	/// shared diagnostics log, once per session per tier. Nothing is shown to the affected user — the
	/// admin's own device turns these entries into a notification (see <see cref="SyncAdminAlertsAsync"/>).
	///
	/// 2026-10-19 bugfix (live report: "porad chodi upozorneni ikdyz jsem psal ze stacji 1 a pak za 3
	/// dny" — the 3-day repeat cooldown wasn't holding): the message used to embed the LIVE elapsed
	/// minute count ("...už 37 min."), which changes every single time this runs (and <see cref="_reportedStuckPairings"/>
	/// resets on every app restart, so a long-stuck pairing re-logs often). <see cref="SyncAdminAlertsAsync"/>'s
	/// cooldown groups by exact message text, so a different number every time meant it never recognized
	/// two reports of the SAME stuck pairing as the same problem — the cooldown never actually engaged.
	/// Now logs a fixed per-tier label ("15+"/"60+") instead of the exact minute count, so repeats of the
	/// same pairing/tier always produce byte-identical text and group correctly.
	/// </summary>
	private static void ReportStuckPairings(IReadOnlyList<ChatSession> sessions)
	{
		try
		{
			var now = DateTimeOffset.UtcNow;
			foreach (var s in sessions.Where(s => s.State == ChatSessionState.PendingHandshake))
			{
				var minutes = (int)(now - s.ModifiedAtUtc).TotalMinutes;
				var level = minutes >= 60 ? DiagnosticLogLevel.Error : minutes >= 15 ? DiagnosticLogLevel.Warning : (DiagnosticLogLevel?)null;
				if (level is null) continue;
				if (!_reportedStuckPairings.Add($"{s.Id}:{level}")) continue;
				var thresholdLabel = minutes >= 60 ? "60+" : "15+";
				ReportFireAndForget(level.Value,
					$"Párování s {s.PeerDisplayName} se nedokončilo už {thresholdLabel} min.",
					PairingWatchdogContext, null);
			}
		}
		catch
		{
			// Best-effort — a watchdog failing must never disturb the sweep it runs in.
		}
	}

	private const string AdminAlertsWatermarkPreferenceKey = "admin_alerts_last_utc_ticks";
	private const string AdminAlertGroupsPreferenceKey = "admin_alert_groups";

	/// <summary>A repeat of an already-notified problem is still written to the shared diagnostics log
	/// every time (2026-10-19, user's ask) — re-notifying the admin about it that often would just be
	/// noise, so a repeat gets a fresh notification at most once per this window; in between, the count
	/// on the existing notification's group just keeps growing silently.</summary>
	private static readonly TimeSpan AdminAlertRepeatCooldown = TimeSpan.FromDays(3);

	private sealed record AdminAlertGroup(Guid NotificationId, int Count, DateTimeOffset LastNotifiedUtc);

	private static Dictionary<string, AdminAlertGroup> LoadAdminAlertGroups(Microsoft.Maui.Storage.IPreferences prefs)
	{
		try
		{
			var json = prefs.Get(AdminAlertGroupsPreferenceKey, string.Empty);
			return string.IsNullOrEmpty(json)
				? new()
				: System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, AdminAlertGroup>>(json) ?? new();
		}
		catch { return new(); }
	}

	private static void SaveAdminAlertGroups(Microsoft.Maui.Storage.IPreferences prefs, Dictionary<string, AdminAlertGroup> groups)
	{
		try
		{
			var trimmed = groups.Count > 100 ? groups.Skip(groups.Count - 100).ToDictionary(p => p.Key, p => p.Value) : groups;
			prefs.Set(AdminAlertGroupsPreferenceKey, System.Text.Json.JsonSerializer.Serialize(trimmed));
		}
		catch { /* best-effort */ }
	}

	/// <summary>
	/// Admin only: turns new Error/Critical entries (and pairing-watchdog warnings) from the shared
	/// diagnostics log into a Nástěnka notification, so a problem on ANY member's device reaches the
	/// admin without anyone touching that device. First run only sets the watermark (no history burst).
	/// At most 5 notifications per sweep; identical device+message repeats collapse into one.
	/// </summary>
	private static async Task SyncAdminAlertsAsync(IServiceProvider services)
	{
		try
		{
			using var scope = services.CreateScope();
			var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
			await currentUser.InitializeAsync();
			if (currentUser.Current.Role != Role.Admin) return;

			var reporter = scope.ServiceProvider.GetRequiredService<IDiagnosticsReporter>();
			var entries = await reporter.GetRecentAsync(100);

			var prefs = Microsoft.Maui.Storage.Preferences.Default;
			var lastTicks = prefs.Get(AdminAlertsWatermarkPreferenceKey, 0L);
			var newest = entries.Count > 0 ? entries.Max(e => e.CreatedAtUtc) : DateTimeOffset.UtcNow;
			if (lastTicks == 0)
			{
				prefs.Set(AdminAlertsWatermarkPreferenceKey, newest.UtcTicks);
				return;
			}

			var watermark = new DateTimeOffset(lastTicks, TimeSpan.Zero);
			// One notification per distinct problem (device + exact message — the message of a crash
			// already carries app version and OS), however often it recurs: a repeat archives the
			// earlier notification and posts a fresh one titled with the running count ("2×"), so the
			// system chat shows each problem once, newest and unread. Counts survive restarts in Preferences.
			var fresh = entries
				.Where(e => e.CreatedAtUtc > watermark
					&& (e.Level >= DiagnosticLogLevel.Error || e.Context == PairingWatchdogContext))
				.OrderBy(e => e.CreatedAtUtc)
				.GroupBy(e => (e.DeviceDisplayName, e.Message))
				.ToList();

			var groups = LoadAdminAlertGroups(prefs);
			var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
			var native = services.GetService<INativeNotificationService>();
			var now = DateTimeOffset.UtcNow;
			foreach (var g in fresh.Take(8))
			{
				var last = g.Last();
				var key = $"{g.Key.DeviceDisplayName}|{g.Key.Message}";
				var previous = groups.GetValueOrDefault(key);
				var total = (previous?.Count ?? 0) + g.Count();

				// Same problem, still within the cooldown since it was last actually notified: it's
				// already in the shared diagnostics log, so just keep the running count — no new
				// notification, no archiving the one still showing.
				if (previous is not null && now - previous.LastNotifiedUtc < AdminAlertRepeatCooldown)
				{
					groups[key] = previous with { Count = total };
					continue;
				}

				if (previous is not null && await notifications.GetByIdAsync(previous.NotificationId) is { } old)
				{
					old.Archive();
					await notifications.UpdateAsync(old);
				}
				var id = await NotificationPublisher.PublishAdminAlertAsync(notifications, last.Level, g.Key.DeviceDisplayName, g.Key.Message, total, native);
				groups[key] = new AdminAlertGroup(id, total, now);
			}
			if (fresh.Count > 8)
				await NotificationPublisher.PublishAdminAlertAsync(notifications, DiagnosticLogLevel.Warning, "více zařízení", $"Dalších {fresh.Count - 8} různých upozornění v Nastavení → Diagnostika.", 1, native);
			SaveAdminAlertGroups(prefs, groups);

			if (newest.UtcTicks > lastTicks)
				prefs.Set(AdminAlertsWatermarkPreferenceKey, newest.UtcTicks);
		}
		catch
		{
			// Best-effort — retried next sweep.
		}
	}

	/// <summary>
	/// Background identity-migration sweep (2026-09-19, the user's own hard standing requirement —
	/// see <see cref="PeerIdentityReconciler"/>'s own remarks and the "secureapp-self-healing-requirement"
	/// memory: nobody should ever have to notice or manually fix a broken chat/group member). Runs on
	/// the SAME cadence as <see cref="RunStaleSessionSweepAsync"/> — every reconnect and every
	/// <see cref="_staleSessionSweepInterval"/> — independent of which screen, if any, is open. For
	/// every 1:1 peer and every group member this device knows about, checks the relay's own member
	/// directory for a same-name-but-different-key match — the shape a peer's identity reset (vault
	/// wipe, local DB reset) leaves behind — and silently re-pairs under the new key, carrying this
	/// device's own still-pending outbound history onto the fresh session and resending it right away
	/// (see <see cref="SessionRecoveryHelper.ResyncAsync"/>'s <c>migratingFromPublicKey</c> remarks).
	///
	/// Deliberately does NOT recover content already stuck in the RELAY's own outbox addressed to a
	/// now-dead identity (an admin/ops concern, not a runtime one) — only this device's own locally-held
	/// copy of what it tried to send.
	/// </summary>
	private static async Task RunPeerIdentityReconciliationAsync(IServiceProvider services, IMessageTransport transport)
	{
		using var scope = services.CreateScope();
		var contactDirectoryService = scope.ServiceProvider.GetRequiredService<IContactDirectoryService>();
		var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
		var groupChatRepository = scope.ServiceProvider.GetRequiredService<IGroupChatRepository>();
		var groupMemberRepository = scope.ServiceProvider.GetRequiredService<IGroupMemberRepository>();
		var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
		var transportSettings = scope.ServiceProvider.GetRequiredService<ITransportSettingsRepository>();
		var currentUserService = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
		var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

		IReadOnlyList<DirectoryMember> directory;
		try { directory = await contactDirectoryService.ListMembersAsync(); }
		catch { return; } // best-effort — relay/directory unreachable right now, next sweep tries again
		if (directory.Count == 0) return;

		byte[] localPublicKey;
		try { localPublicKey = await messagingService.GetLocalIdentityPublicKeyAsync(); }
		catch { return; }

		// --- 1:1 sessions ---
		IReadOnlyList<ChatSession> sessions;
		try { sessions = await sessionRepository.GetAllAsync(); }
		catch { sessions = []; }

		var latestByPeer = sessions
			.GroupBy(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey))
			.Select(g => g.OrderByDescending(s => s.ModifiedAtUtc).First());

		foreach (var session in latestByPeer)
		{
			// Stays gone unless the user explicitly re-invites/accepts — same policy as everywhere
			// else RemovedPeersStore is checked (never silently resurrect a chat the user removed).
			if (RemovedPeersStore.Contains(session.PeerIdentityPublicKey))
				continue;

			var target = PeerIdentityReconciler.TryResolveMigrationTarget(directory, session.PeerDisplayName, session.PeerIdentityPublicKey);
			if (target is null) continue;

			try
			{
				await SessionRecoveryHelper.ResyncAsync(
					messagingService, transport, transportSettings, currentUserService, messageRepository, sessionRepository,
					target.DisplayName, target.PublicKey, target.RelayDeviceId, migratingFromPublicKey: session.PeerIdentityPublicKey);
				AppLog.Event("peer.identity.migrated", ("peer", target.DisplayName), ("scope", "1:1"));
			}
			catch (Exception ex)
			{
				AppLog.Error("App.PeerIdentityReconciliation", $"failed to migrate 1:1 peer {target.DisplayName}", ex);
			}
		}

		// --- Group members ---
		IReadOnlyList<GroupChat> groups;
		try { groups = await groupChatRepository.GetAllAsync(); }
		catch { groups = []; }

		foreach (var group in groups)
		{
			IReadOnlyList<GroupMember> members;
			try { members = await groupMemberRepository.GetByGroupAsync(group.Id); }
			catch { continue; }

			var migrations = new List<(GroupMember Old, DirectoryMember New)>();
			foreach (var member in members)
			{
				if (member.PublicKey.AsSpan().SequenceEqual(localPublicKey)) continue; // that's me
				if (RemovedPeersStore.Contains(member.PublicKey)) continue;

				var target = PeerIdentityReconciler.TryResolveMigrationTarget(directory, member.DisplayName, member.PublicKey);
				if (target is not null) migrations.Add((member, target));
			}
			if (migrations.Count == 0) continue;

			var migratedById = migrations.ToDictionary(m => m.Old.Id, m => m.New);
			var updatedMembers = members
				.Select(m => migratedById.TryGetValue(m.Id, out var newTarget)
					? new GroupMember(group.Id, newTarget.DisplayName, newTarget.PublicKey, newTarget.RelayDeviceId, m.CanInvite)
					: m)
				.ToList();

			try
			{
				await groupMemberRepository.ReplaceAllAsync(group.Id, updatedMembers);
				AppLog.Event("group.membership.identity-migrated", ("group", group.Id), ("count", migrations.Count));
			}
			catch (Exception ex)
			{
				AppLog.Error("App.PeerIdentityReconciliation", $"failed to persist migrated membership for group {group.Id}", ex);
				continue; // don't re-pair against a roster we failed to actually persist
			}

			foreach (var (oldMember, target) in migrations)
			{
				try
				{
					await SessionRecoveryHelper.ResyncAsync(
						messagingService, transport, transportSettings, currentUserService, messageRepository, sessionRepository,
						target.DisplayName, target.PublicKey, target.RelayDeviceId, migratingFromPublicKey: oldMember.PublicKey);
					AppLog.Event("peer.identity.migrated", ("peer", target.DisplayName), ("scope", "group"), ("group", group.Id));
				}
				catch (Exception ex)
				{
					AppLog.Error("App.PeerIdentityReconciliation", $"failed to re-pair migrated group member {target.DisplayName}", ex);
				}
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
		var handlerSw = System.Diagnostics.Stopwatch.StartNew();

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
					var (_, ackEnvelope) = await messagingService.SendMessageAsync(envelope.SessionId, ackPayload, isSystemPayload: true); // envelope.SessionId: a repeat of an already-stored message resolves to a row of an older, closed session
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
					// This covers EVERY kind of system payload (delivery ack, key offer/request, delete command), not
					// just the key import the message used to name, and it now also lands in the local error log
					// (2026-10-24: an ack that failed here left no trace on the device at all).
					AppLog.Error("App.Receive", "processing a received system payload failed", importEx);
					_ = reporterForImport.ReportAsync(DiagnosticLogLevel.Error, "Přijatou systémovou zprávu (potvrzení, klíč sdílené knihovny nebo smazání) se nepodařilo zpracovat.", nameof(OnEnvelopeReceived), importEx);
				}
			}
			else
			{
				// Notification Hub (2026-09-20, see NOTIFICATION_HUB_SPEC.md) — a genuine, visible
				// message, unconditionally turned into a Notification Hub row the same way it's
				// unconditionally persisted above, regardless of whether any chat page is open.
				try
				{
					// A re-delivery of a message that is already stored (the sender resent it, or the relay handed the same
					// frame over twice) makes ReceiveMessageAsync return that very row — the delivery ack above is still
					// wanted (it is what stops the resending), a second notification is not (2026-10-24: one message had
					// been announced up to six times).
					if (!RememberNotified(message.Id))
						return;

					var notificationRepository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
					var nativeNotificationService = scope.ServiceProvider.GetRequiredService<INativeNotificationService>();
					var plaintext = await messagingService.DecryptMessageAsync(message.Id);
					var preview = System.Text.Encoding.UTF8.GetString(plaintext);
					var sessionRepositoryForNotify = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
					var session = await sessionRepositoryForNotify.GetByIdAsync(message.ChatSessionId);
					// The user is looking at this exact thread right now — nothing to notify about.
					if (session is not null && !Notifications.ActiveChatThread.IsOpen(envelope.GroupChatId, session.PeerIdentityPublicKey))
					{
						if (envelope.GroupChatId is { } groupChatId)
						{
							var groupChatRepositoryForNotify = scope.ServiceProvider.GetRequiredService<IGroupChatRepository>();
							var group = await groupChatRepositoryForNotify.GetByIdAsync(groupChatId);
							if (group is not null)
								await NotificationPublisher.PublishGroupMessageAsync(notificationRepository, group.Name, session.PeerDisplayName, preview, message.ChatSessionId, groupChatId, nativeNotificationService);
						}
						else
						{
							await NotificationPublisher.PublishDirectMessageAsync(notificationRepository, session.PeerDisplayName, preview, message.ChatSessionId, nativeNotificationService);
						}
					}
				}
				catch
				{
					// Best-effort — see NotificationPublisher's own class-level remarks; never let a
					// notification-row failure affect the real message handling above it.
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
			_ = reporter.ReportAsync(DiagnosticLogLevel.Warning, "Přijatou zprávu se nepodařilo dešifrovat — spouští se automatické obnovení spojení.", nameof(OnEnvelopeReceived), ex);
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
					var messageRepositoryForHeal = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
					await SessionRecoveryHelper.ResyncAsync(
						messagingService, messageTransport, transportSettings, currentUserService, messageRepositoryForHeal, sessionRepository,
						session.PeerDisplayName, session.PeerIdentityPublicKey, relayDeviceId);
				}
			}
			catch
			{
				// Best-effort — nothing further to do at this level.
			}
		}
		finally
		{
			// Normally a few ms; a long one (the ack seen leaving ~100 s after its message arrived) says WHERE
			// the time went when read next to ws.send.slow.
			if (handlerSw.ElapsedMilliseconds >= 2000)
				AppLog.Metric("msg.receive.slow", handlerSw.Elapsed.TotalMilliseconds, "ms", ("system", envelope.IsSystemPayload));
		}
	}

	// Ids of the messages this process already raised a notification for (bounded; a restart forgets them, which at
	// worst announces one old resend again).
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _notifiedMessages = new();

	private static bool RememberNotified(Guid messageId)
	{
		if (_notifiedMessages.Count > 500)
			_notifiedMessages.Clear();
		return _notifiedMessages.TryAdd(messageId, 0);
	}

	private static async Task OnPairingInviteReceived(string inviteBlob)
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
		var sessionRepository = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
		try
		{
			// A fresh handshake invite from a peer we already have a session with (2026-09-07) now
			// means THAT SIDE determined a resync was needed (see SessionRecoveryHelper.ResyncAsync)
			// and is trying to fix things without any action required here — so always accept it,
			// closing this device's own stale copy first, rather than silently ignoring it as before.
			// (The old behavior assumed "already paired" could only mean a redundant duplicate scan
			// of the initiator's own QR — accepting-and-replacing is harmless in that case too, just
			// a wasted extra handshake.)
			//
			// 2026-10-04 — the 2.2 (2026-09-14) "hold a removed peer's re-invite for explicit consent"
			// behavior is REMOVED, per the user's own explicit, direct reversal of that earlier policy:
			// this is a closed community where every member is personally vetted before joining at
			// all, so there is no scenario where a fresh invite from someone already IN the community
			// needs a second manual gate — it directly contradicted this project's own standing
			// "nothing requires the user to manually approve anything" rule (see the
			// secureapp-self-healing-requirement memory) and was the root cause of a real, found-live
			// bug: a message to a peer the user had locally deleted the chat with silently went
			// nowhere for 16+ hours because nobody ever saw (or thought to look for) the consent
			// banner this used to create. RemovedPeersStore.Add from deleting a chat still runs (see
			// ChatListViewModel.DeleteSessionAsync) — it only ever did LOCAL declutter housekeeping
			// elsewhere (group-member filtering); it just no longer blocks 1:1 re-pairing here.
			RemovedPeersStore.Remove(invite.InitiatorPublicKey);

				// Both sides resynced at the same moment (2026-10-23): each side's accepted session is then
				// built from the OTHER's handshake, so the two never match and every message fails to
				// decrypt until the cooldown allows another round — which can collide again. The same
				// deterministic tie-break the group mesh uses decides: the side that would initiate keeps
				// its own fresh session and ignores this invite; the other side accepts the winner's.
				if (SessionRecoveryHelper.ResyncedWithin(invite.InitiatorPublicKey, TimeSpan.FromSeconds(60))
					&& ShouldInitiateTo(await messagingService.GetLocalIdentityPublicKeyAsync(), invite.InitiatorPublicKey))
				{
					AppLog.Event("pairing.glare-ignored", ("peer", invite.InitiatorDisplayName));
					return;
				}

			var existingSession = await messagingService.FindExistingSessionAsync(invite.InitiatorPublicKey);
			byte[]? historySourceKey = null;
			if (existingSession is not null)
			{
				await messagingService.CloseSessionAsync(existingSession.Id);
				historySourceKey = invite.InitiatorPublicKey;
			}
			else
			{
				// Identity-migration accept (2026-09-19, see PeerIdentityReconciler's own remarks): this
				// device has never seen invite.InitiatorPublicKey before, but if it has exactly one OTHER
				// local session (any state) under the SAME display name and a DIFFERENT key, the peer
				// most likely just reset their identity and this is really a resync in disguise, not a
				// first-ever pairing — carry that old session's still-pending outbound history onto the
				// fresh one below instead of silently stranding it forever (the exact gap that left this
				// device's own undelivered messages stuck addressed to a peer's dead identity). Ambiguous
				// (more than one distinct old key sharing that name) deliberately does nothing here — same
				// "never guess" discipline as PeerIdentityReconciler.TryResolveMigrationTarget.
				try
				{
					var allSessions = await sessionRepository.GetAllAsync();
					var sameNameOtherKeys = allSessions
						.Where(s => s.PeerDisplayName == invite.InitiatorDisplayName && !s.PeerIdentityPublicKey.AsSpan().SequenceEqual(invite.InitiatorPublicKey))
						.Select(s => Convert.ToHexStringLower(s.PeerIdentityPublicKey))
						.Distinct()
						.ToList();
					if (sameNameOtherKeys.Count == 1)
						historySourceKey = Convert.FromHexString(sameNameOtherKeys[0]);
				}
				catch
				{
					// Best-effort — a lookup failure here just means no history migration this time.
				}
			}

			var acceptedSession = await messagingService.AcceptSessionAsync(invite.InitiatorDisplayName, invite.InitiatorPublicKey, invite.InitiatorRelayDeviceId, invite.HandshakeCipherText);
			AppLog.Event("pairing.accepted", ("peer", invite.InitiatorDisplayName), ("peerDevice", invite.InitiatorRelayDeviceId));

			// 2026-09-15 (user: a message that failed to reach the peer before a resync "musí být
			// vyhledána a vložena do chatu" — must be found and put back in the chat): this device is
			// the one accepting a FRESH invite for a peer it already knew, i.e. exactly the resync
			// scenario — the peer's own decrypt broke and it rebuilt the session from scratch. Whatever
			// THIS device had sent on the old, now-closed session but never got a delivery ack for is
			// migrated onto the new session (so it's visible again) and re-sent over it (so it actually
			// arrives) — see SessionRecoveryHelper's own remarks for why this is safe to do HERE
			// (session fully established both ways by this point) but not from the resyncing side itself.
			// 2026-09-19: also covers the identity-migration case above, via historySourceKey.
			if (historySourceKey is not null)
			{
				var messageRepositoryForHeal = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
				var messageTransportForHeal = scope.ServiceProvider.GetRequiredService<IMessageTransport>();
				try
				{
					await SessionRecoveryHelper.ReassignAllHistoryAsync(sessionRepository, messageRepositoryForHeal, historySourceKey, acceptedSession.Id);
					await SessionRecoveryHelper.ResendUndeliveredAsync(messagingService, messageRepositoryForHeal, messageTransportForHeal, acceptedSession.Id);
				}
				catch (Exception reassignEx)
				{
					// Best-effort — the pairing itself already succeeded above; losing the ability to
					// migrate/resend old history must never undo that.
					AppLog.Error("App.Pairing", $"history migration/resend after re-pairing with {invite.InitiatorDisplayName} failed", reassignEx);
				}
			}

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
			else
			{
				var renamed = existingGroup.Name != invite.GroupName;
				if (renamed) existingGroup.Rename(invite.GroupName);

				// 2.1 (2026-09-17): a real gap this closes — this branch only ever checked the NAME for
				// a change, never the founder, so an incoming snapshot after a TransferFounder/claim
				// (see GroupChat.TransferFounder's own remarks) silently left this device's local copy
				// pointing at the OLD founder forever, even though every other member's device applied
				// it correctly. Same full-snapshot-replace reasoning as the name check right above.
				var founderChanged = !existingGroup.FounderPublicKey.AsSpan().SequenceEqual(invite.FounderPublicKey);
				if (founderChanged)
				{
					existingGroup.TransferFounder(invite.FounderPublicKey);
					AppLog.Event("group.founder.synced", ("group", invite.GroupId));
				}

				if (renamed || founderChanged)
					await groupChatRepository.UpsertAsync(existingGroup);
			}

			var members = invite.Members
				.Select(m => new GroupMember(invite.GroupId, m.DisplayName, m.PublicKey, m.RelayDeviceId))
				.ToList();
			await groupMemberRepository.ReplaceAllAsync(invite.GroupId, members);
			AppLog.Event("group.membership.synced", ("group", invite.GroupId), ("name", invite.GroupName), ("members", members.Count));

			// 2.3 (2026-09-14): a group that has lost every OTHER member is moved to the local archive
			// (read-only history) instead of hanging in the main list; if members later return, it's
			// un-archived. See ArchivedChatsStore.
			var othersRemain = invite.Members.Any(m => !m.PublicKey.AsSpan().SequenceEqual(localPublicKey));
			if (!othersRemain)
			{
				ArchivedChatsStore.Add(invite.GroupId);
				AppLog.Event("group.auto-archived", ("group", invite.GroupId), ("reason", "no-members-left"));
			}
			else
			{
				ArchivedChatsStore.Remove(invite.GroupId);
			}

			foreach (var member in invite.Members)
			{
				if (member.PublicKey.AsSpan().SequenceEqual(localPublicKey))
					continue; // that's me

				if (await messagingService.FindExistingSessionAsync(member.PublicKey) is not null)
					continue; // already paired with this member from an earlier group/1:1 chat

				// 2026-09-16 (user: "porad se objevuje chat lokal user zjisti proc a oprav to") — the
				// real cause: a group's own membership sync runs on every incoming group/invite event,
				// completely unattended, and this loop used to auto-initiate a fresh 1:1 pairing with
				// EVERY unpaired member unconditionally — including one the user had explicitly deleted
				// (RemovedPeersStore). As long as that peer stayed in a shared group's roster (a stale
				// test/dev member still called "Local User" is exactly this shape), every subsequent
				// membership sync silently recreated the 1:1 chat the user just removed. 2.2 already
				// closed this gap on the RECEIVING side (OnPairingInviteReceived, below) and in the
				// stale-session sweep — this INITIATING side was the one path still missing it.
				if (RemovedPeersStore.Contains(member.PublicKey))
					continue; // stays gone unless the user explicitly re-invites/accepts, same policy as everywhere else

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
		// 2026-10-05, Windows multi-profile login — see this class' own constructor remarks. The
		// rest of this method's window-event wiring (DLP, notification/widget routing, background-
		// run prompt) all assume a real, profile-backed session, so none of it applies to the login
		// page either; it's a plain page with nothing pushed onto it.
		if (Profiles.ActiveProfile.RequiresLogin)
			return new Window(new Views.ProfileLoginPage());

		var window = new Window(new AppShell());

		// Backgrounded with a chat still open must still notify; returning to it clears what's now seen.
		window.Stopped += (_, _) => Notifications.ActiveChatThread.OnAppStopped();
		window.Resumed += (_, _) =>
		{
			Notifications.ActiveChatThread.OnAppResumed();
			ScreenCaptureGuard.Refresh(); // the OS window may have been recreated without our flag
		};

		// Milestone 4 (DLP): applied once, app-wide, as soon as the native platform window
		// actually exists — CreateWindow itself returns before that's true (Handler/PlatformView
		// are still null here), so this waits for Window.Created rather than calling it inline.
		window.Created += (_, _) =>
		{
			Infrastructure.AppStartup.Mark("window_created");

			// Capture protection is chat-only (2026-10-18): follow the visible page instead of blocking
			// app-wide. The admin override in Settings still switches it off everywhere.
			if (window.Page is Shell shell)
				shell.Navigated += (_, _) =>
				{
					Infrastructure.AppStartup.Mark("first_navigated");
					ScreenCaptureGuard.Refresh();
				};
			ScreenCaptureGuard.Refresh();

			// Milestone 5 (E2EE Chat) connect: handled entirely by RunConnectionSupervisorLoopAsync
			// now, started from the constructor above — its very first tick fires almost immediately,
			// so launch-time connect behavior is unchanged; nothing further to kick off here.

			// Notification Hub (2026-09-20, Phase 8, spec §31), COLD case: the app was launched
			// fresh by tapping a notification — MainActivity.OnCreate ran (and stashed the id)
			// before the constructor's own subscription above ever had a chance to listen. Window.Created
			// is the earliest point Shell.Current is safe to navigate through, same reasoning the DLP
			// call right above already relies on.
			if (Notifications.NativeNotificationRouter.ConsumePendingNotificationId() is { } pendingId)
				NavigateToNotificationDetail(pendingId);

			// Phase 7 widget's weekly-rozpis card (2026-09-22) — same cold-start timing reasoning as
			// the notification-detail consume right above.
			if (Notifications.PendingWidgetRouteRouter.ConsumePendingRoute() is { } pendingRoute)
				NavigateToRoute(pendingRoute);

			// Background messaging (2026-09-26): explain + offer the battery setting if it's still off.
			// Delayed so it doesn't collide with the first-launch notification-permission prompt.
			window.Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(4), async () =>
			{
				try
				{
					if (window.Page is { } page)
						await Infrastructure.BackgroundRun.PromptIfNeededAsync(page);
				}
				catch (Exception ex)
				{
					AppLog.Error(nameof(Infrastructure.BackgroundRun), "background-run prompt failed", ex);
				}
			});
		};

		return window;
	}

	private static void OnNativeNotificationTapped(Guid notificationId) => NavigateToNotificationDetail(notificationId);
	private static void OnWidgetRouteRequested(string route) => NavigateToRoute(route);

	private static void NavigateToRoute(string route)
	{
		try
		{
			Shell.Current?.GoToAsync(WorkplaceRoute(route));
		}
		catch
		{
			// Best-effort — same reasoning as NavigateToNotificationDetail's own catch right below.
		}
	}

	/// <summary>
	/// WorkplacePage (Rozpis) isn't a tab of its own — it's pushed on top of whatever tab happens to
	/// be current (see PendingWidgetRouteRouter's own remarks). A bare relative GoToAsync therefore
	/// pushes it onto whatever tab was active when the widget/notification fired, NOT necessarily
	/// Nástěnka (2026-09-29, user's own bug report: opened the widget's Rozpis card while on Kontakty
	/// — Rozpis is what showed, but "Kontakty" stayed lit in the tab bar and tapping it again did
	/// nothing, since Shell still considered Kontakty the active tab). Prefixing with Nástěnka's own
	/// absolute route switches the tab AND pushes the page in one navigation (same "//ContactsTab"
	/// absolute-route pattern SmartSearchViewModel already uses), so the tab bar's highlight always
	/// matches what's actually on screen, and the ORIGINATING tab's own stack (e.g. Kontakty) is left
	/// untouched underneath, exactly as the user left it, once they switch back to it.
	/// </summary>
	private static string WorkplaceRoute(string route)
		=> route == nameof(Views.WorkplacePage) ? $"//NotificationsTab/{route}" : route;

	/// <summary>A chat/group message notification (widget ticker or Android shade) opens the conversation itself (2026-09-26, user's ask); anything else opens the notification's detail.</summary>
	private static async void NavigateToNotificationDetail(Guid notificationId)
	{
		try
		{
			var route = await ResolveNotificationRouteAsync(notificationId)
				?? $"{nameof(Views.NotificationDetailPage)}?notificationId={notificationId}";
			if (Shell.Current is { } shell)
				await shell.GoToAsync(route, animate: false);
		}
		catch
		{
			// Best-effort — a navigation failure here (e.g. Shell not fully ready yet on a very
			// fast cold start) just means the app opens to its normal home screen instead, same as
			// tapping the app icon would have.
		}
	}

	private static async Task<string?> ResolveNotificationRouteAsync(Guid notificationId)
	{
		var services = IPlatformApplication.Current?.Services;
		if (services is null) return null;
		using var scope = services.CreateScope();
		var notification = await scope.ServiceProvider.GetRequiredService<INotificationRepository>().GetByIdAsync(notificationId);
		if (notification is null) return null;

		// Same tab-highlight bug/fix as the widget's own Rozpis card — see WorkplaceRoute's own remarks.
		if (notification.Category == NotificationCategory.Schedule)
			return WorkplaceRoute(nameof(Views.WorkplacePage));

		if (notification.RelatedGroupChatId is { } groupChatId)
		{
			AppShell.GroupChatPageFactory.PendingKey = groupChatId.ToString();
			return $"{nameof(Views.GroupChatPage)}?groupChatId={groupChatId}";
		}

		if (notification.RelatedChatSessionId is not { } chatSessionId) return null;

		// The notification may point at a session a resync has since replaced — open the peer's live one.
		var sessions = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
		var original = await sessions.GetByIdAsync(chatSessionId);
		if (original is null) return null;
		chatSessionId = (await sessions.GetAllByPeerPublicKeyAsync(original.PeerIdentityPublicKey))
			.OrderByDescending(s => s.State != ChatSessionState.Closed)
			.ThenByDescending(s => s.LastRatchetedAtUtc ?? s.CreatedAtUtc)
			.First().Id;

		AppShell.ChatPageFactory.PendingKey = chatSessionId.ToString();
		return $"{nameof(Views.ChatPage)}?chatSessionId={chatSessionId}";
	}
}