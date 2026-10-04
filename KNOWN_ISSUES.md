# Known Issues — real error types seen on real devices

Generated 2026-10-04 by querying the relay's own `device_app_logs` table directly (every device
uploads its own `AppLog` error/metric lines automatically — see `Diagnostics.AppLogUploader`) rather
than guessing from code alone. This is the **complete, deduplicated list of distinct error types**
across every device that has ever reported one, not a sample — so this list itself, kept updated,
is what should be checked first in the future instead of pulling a phone's log by hand again.

Query used (read-only, against the relay's SQLite DB):
```sql
SELECT COUNT(*), COUNT(DISTINCT device_id), MIN(received_at_utc), MAX(received_at_utc),
       SUBSTR(line, INSTR(line, CHAR(9))+1, 160) AS sig
FROM device_app_logs WHERE kind='errors' GROUP BY sig ORDER BY COUNT(*) DESC;
```

Each item below: what it is, how often/where it's actually been seen, current status, and the real
log line that proves it (not a paraphrase).

## Verifying a fix actually worked (don't just assume it from the diff)

**2026-10-04, user's own requirement:** every error occurrence must be checkable against which app
version was actually running at the time, and marking an issue "fixed" must never rest on "I changed
the code" alone — only on real field evidence that the signature actually stopped recurring on
versions at or after the fix. Two things make this checkable now:

- Every row in `device_app_logs` carries an `app_version` column (added 2026-10-04 — rows from
  before that date are `NULL`, i.e. "unknown version"; see `RelayDatabase.AppendAppLogLines`'s own
  remarks for why it's "version at upload time", not a per-line timestamp-matched guarantee). This is
  DIFFERENT from `directory_entries.app_version` (added 2026-10-01), which only ever holds a device's
  CURRENT version — it cannot answer "what version was this device running when THIS specific error
  happened three days ago."
- Once an issue below is marked **"Fixed in version: X (commit `abc1234`)"**, re-run its query but add
  `AND app_version IS NOT NULL AND app_version NOT LIKE 'X (%'` (adjust the comparison to "versions at
  or after X" using the version's own build number if several builds need excluding) — any row that
  still comes back means the fix did **not** actually work and the entry must be reopened, not left
  marked fixed. Example, once §2 (media3 crash) is believed fixed in, say, 1.44 (build 47):
  ```sql
  SELECT device_id, received_at_utc, app_version FROM device_app_logs
  WHERE kind='errors' AND line LIKE '%AbstractMethodError%onAudioSessionIdChanged%'
    AND app_version IS NOT NULL
  ORDER BY received_at_utc DESC LIMIT 20;
  -- if the newest row's app_version build number is >= 47, the fix did not hold.
  ```
- None of the issues below are marked fixed yet — none of them have shipped a fix. The first one that
  does should get this treatment immediately, not retroactively once someone wonders if it recurred.

---

## 1. Relay reconnect fails — `WebSocketException: net_webstatus_ConnectFailure`

- **Seen:** 1957×, across 3 devices (Vilém's S23+ ×2 registrations, Petr's S25), 2026-09-27 → 2026-10-03.
- **Status:** OPEN — not yet investigated. By far the single most common error in the whole log.
- **Likely nature:** could be routine mobile-network noise (the app force-reconnects every 5 minutes
  by design — see `App._forcedReconnectInterval` — so *some* transient connect failures are expected),
  but 1957 over ~1 week across only 3 devices is a lot to write off without checking whether it
  clusters around specific times/conditions (e.g. always right after the forced-reconnect tick, or
  tied to a specific network transition).
- **Next step:** check whether these failures reliably self-resolve on the very next attempt (the
  `_supervisorTickInterval` is 10s) or whether some stretches go unconnected for a while.

```
ERROR	App.TryConnect	relay reconnect attempt failed	WebSocketException: net_webstatus_ConnectFailure |
    at System.Net.WebSockets.WebSocketHandle.ConnectAsync(...)
    at System.Net.WebSockets.ClientWebSocket.ConnectAsyncCore(...)
    at SecureApp.Presentation.Transport.WebSocketMessageTransport.ConnectAsync(Uri endpoint, CancellationToken ct)
    at SecureApp.Presentation.App.TryConnectAsync(IServiceProvider services, IMessageTransport transport)
```

---

## 2. Video playback crashes the whole app — `AbstractMethodError` in MediaManager/media3

- **Seen:** 5×, Petr's S25 only, all within one minute (2026-10-03 11:18:13–11:19:01 local).
- **Status:** OPEN — root cause understood, not yet fixed.
- **Root cause:** `CommunityToolkit.Maui.MediaElement` is pinned to 7.0.0 (see git history/handoffs —
  a transitive dependency-floor conflict at the time), but the *native* `androidx.media3` library
  actually resolved on-device is a newer version whose `Player.Listener` interface added
  `onAudioSessionIdChanged` — a method the pinned wrapper's `MediaManager` class never implements.
  The native/managed boundary crossing here crashes the ENTIRE process (not a catchable .NET
  exception) every time it fires — same APPCRASH-class failure mode documented for the GCS calculator
  bug in `plans/handoffs/HANDOFF_standalone-be860f68_library-video-gcs-tools_2026-10-03.md`.
- **Open question:** whether this only fires while a video is actually loaded/playing, or from some
  lingering player instance even when nothing is open — the 5 crashes landing within one minute,
  with the app auto-restarting each time, suggests the latter (a leftover player kept emitting
  lifecycle events into a crash loop) but this isn't confirmed.
- **Next step:** find a `MediaElement`/media3 version pairing where the wrapper's listener actually
  matches what gets resolved at runtime, or disable video playback until one does.

```
ERROR	OnUnhandledException	[Error] Neošetřená výjimka — aplikace se ukončuje.	AbstractMethodError:
abstract method "void androidx.media3.common.Player$Listener.onAudioSessionIdChanged(int)" on
receiver java.lang.Class<crc64ceb75e76f4b66147.MediaManager> |
    at ... AndroidX.Media3.Common.IPlayerListener.OnAudioSessionIdChanged(...)
    at crc64ceb75e76f4b66147.MediaManager.onAudioSessionIdChanged(MediaManager.java:28)
    at androidx.media3.exoplayer.ExoPlayerImpl.lambda$onAudioSessionIdChanged$30(...)
    ... (full native Java stack in the relay's own device_app_logs table)
```

---

## 3. Decrypt failure triggers auto-heal — `RatchetStateException: Message decryption failed`

- **Seen:** 8×, 2 devices (Petr's S25, one of Vilém's S23+ registrations), 2026-09-29 → 2026-10-03.
- **Status:** EXPECTED, self-correcting by design — this is exactly the trigger
  `ChatViewModel`/`GroupChatViewModel`'s own `TryAutoHealAsync` exists for (see
  `SessionRecoveryHelper`'s own remarks): a broken pairwise ratchet is detected and a fresh resync is
  started automatically, no user action. Listed here for completeness/visibility, not as something
  that itself needs fixing — but a sudden SPIKE in this specific count would be worth investigating
  (it would mean sessions are breaking more often than they used to).

```
ERROR	App.Receive	decrypt failed; auto-healing session	RatchetStateException: Message decryption failed. |
    at SecureApp.Data.Messaging.RatchetService.RatchetDecryptAsync(...)
    at SecureApp.Data.Messaging.MessagingService.ReceiveMessageAsync(...)
    at SecureApp.Presentation.App.OnEnvelopeReceived(...)
```

---

## 4. Search bar crashes the app — `ObjectDisposedException` on a disposed `IServiceProvider`

- **Seen:** 6×, 2 devices (Petr's S25, one of Vilém's S23+ registrations), 2026-09-29 → 2026-10-03.
- **Status:** OPEN — not yet investigated.
- **Likely nature:** a MAUI `SearchBar`'s focus-change handler (`SearchBarHandler.FocusChangeListener`)
  fires AFTER its page's DI scope has already been disposed (e.g. navigating away from a search page
  the instant it loses focus) — a timing/lifecycle race, not anything about search content itself.
- **Next step:** find which page(s) have a `SearchBar` that can lose focus during navigation away
  from itself, and guard the focus-change path against a disposed scope.

```
ERROR	OnUnhandledException	[Error] Neošetřená výjimka — aplikace se ukončuje.	ObjectDisposedException:
ObjectDisposed_Generic ObjectDisposed_ObjectName_Name, IServiceProvider |
    at Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceProviderEngineScope.GetService(...)
    at Microsoft.Maui.Controls.InputView.MapIsFocused(...)
    at Microsoft.Maui.Handlers.SearchBarHandler.FocusChangeListener.OnFocusChange(View v, Boolean hasFocus)
    ... (full stack in device_app_logs)
```

---

## 5. Stale/historical — relay auth failure, raw connect failure, empty-email re-activation

These three share the exact same upload timestamp (`2026-10-02T03:53:45`) despite device-local
timestamps from **2026-09-18/09-23** — they are OLD lines that only got uploaded/flushed late, not
live/recent problems. Listed for completeness since the query surfaced them, not as active work.

- **5a. `InvalidOperationException: Ověření u relay serveru selhalo.`** (106×, "PC" test device only, 2026-09-18)
- **5b. `WebSocketException: Unable to connect to the remote server`** (2×, "PC" test device only, 2026-09-23)
- **5c. `ArgumentException` on empty `email` during silent re-activation** (1×, "PC" test device only, 2026-09-18)

All three are confined to the "PC" device (confirmed this session to be a dev/test machine, not a
real community member) and are over a week old — low priority, revisit only if the exact same
signatures reappear on a REAL member's device.

```
ERROR	App.TryConnect	relay reconnect attempt failed	InvalidOperationException: Ověření u relay serveru selhalo. |...
ERROR	App.TryConnect	relay reconnect attempt failed	WebSocketException: Unable to connect to the remote server |...
ERROR	App.TryConnect	silent re-activation failed	ArgumentException: ... (Parameter 'email') |...
```

---

## How to refresh this list later

Re-run the query at the top of this file against the relay (`ssh` + `sqlite3` against
`relay/SecureApp.Relay/data/relay.db3`'s `device_app_logs` table, `kind='errors'`) — it returns every
distinct error type that exists, with counts and real timestamps, with no need to pull any specific
device's log by hand first. Include `app_version` in the SELECT (it's a real column now, see "Verifying
a fix actually worked" above) whenever the question is "did this stop happening after a specific
release," not just "does this still happen at all."
