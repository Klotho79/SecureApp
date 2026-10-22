# Opicentrum board speed work (releases 1.80-1.91, shipped) + full-code review in progress (4 local commits, NOT pushed/deployed/released)

**Date:** 2026-10-22
**Status:** IN PROGRESS (feature work shipped; code review ~50% done; review changes unreleased)
**Bead(s):** none
**Epic:** Opicentrum admin board (Pozice / Volno) + code quality pass
**Chain:** `standalone-24caf3f8` seq `1`
**Parent:** `none — first in chain`
**Prior chain:** none — first in chain

---

## Related Handoffs

- `HANDOFF_nastenka-room-status-redesign_2026-10-15.md` — "Stav sálů" / "Stav mého sálu" tile redesign; background for the room-status fixes made in 1.81 (separate work stream, reference only)

## Reference Documents

- `plans/handoffs/HANDOFF_nastenka-room-status-redesign_2026-10-15.md` — design history of the room-status UI (RoomStatusPalette, RoomWorkplaceKeywords, Poznámka picker)
- Auto-memory index `C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md` — infra paths, adb technique, "never debug-deploy real devices" HARD RULE, self-healing requirement
- No CLAUDE.md in this repo.
- Temp review plan (session scratchpad, may vanish): `C:\Users\dvora\AppData\Local\Temp\claude\H--Visual-Studio-C--Aplikace\d24099f2-6702-4548-a5f6-41bbcd4578b5\scratchpad\REVIEW_PLAN.md` — contents are reproduced below under "Where We Are".

## The Goal

SecureApp (.NET MAUI + self-hosted ASP.NET relay on a Raspberry Pi) gained an Opicentrum admin board (position/room occupancy "Pozice" + leave "Volno", Admin/Modifier only, write-back to the real opicentrum.cz). This session the user complained the board felt slow ("zobrazování trvá strašně dlouho... někdy i více než 3 s") and asked for it to be MEASURED on their S23+ rather than guessed at. After the speed work shipped (12 releases), the user asked for a full code review (logic bugs, duplicate code, simplification — "ale ne na úkor zpomalení") WITHOUT creating a release. That review is half done; its commits exist locally only.

## Where We Are

Shipped and verified live on the relay (`/download/android/version` and `/download/windows/version` both report 94 / 1.91, commit `2b3aef1`, pushed to `pi` and `github`):

- **1.80 (83)** bundled the previous session's "Pozice" local-first cache (`OpicentrumRosterBoardLocalCache` in Preferences + optimistic patch-on-move).
- **1.81 (84)** — four user bugs: (1) "Stav mého sálu" row: whole-row tap (`BackgroundColor="Transparent"` on the Grid carrying the `TapGestureRecognizer` in `NotificationsPage.xaml`); (2) "Vše ›" button restored in the Nástěnka card header (`OnRoomStatusClicked`); (3) Poznámka could not be saved alone/propagated — fixed end to end: `RelayDatabase.UpsertRoomStatus(statusText?, noteText?)` with `COALESCE(@status, status_text)` / `COALESCE(@note, note_text)`, `Program.cs POST /room-status` accepts either field, `Contracts.SetRoomStatusRequest.StatusText` now `string?`, `IRoomStatusService.SetStatusAsync(string? statusText, ...)`, `HttpRoomStatusService`, `RoomStatusViewModel.SetRoomStatusAsync` normalizes blank→null and only bails if BOTH null; (4) stale-cache reconcile skip: `LoadPositionsAsync` skips the relay call while local cache < 5 min old, `LoadVolnoAsync` < 10 min. Relay redeployed for this one.
- **1.82-1.86** diagnostics only (see "What We Tried"): `AppLog.Metric` timings in `OpicentrumBoardViewModel.Positions.cs` (`opicentrum.positions.cache_load / apply / apply.build / apply.assign / relay_fetch / load_total`), `tti.opicentrum_board_page` in `OpicentrumBoardPage.xaml.cs`, temporary `opicentrum.ui_stall` 50 ms UI-thread heartbeat and `relay_fetch.steps` (both REMOVED again in 1.91).
- **1.84 (87)** FIX: `IsPositionsLoading` is cleared right after the local snapshot renders (it used to stay true during the 5 s stale-reconcile relay call, so the `DelayedActivityIndicator` sat on top of an already-rendered board). `IsPositionsLoading = PositionsRows.Count == 0` at start. Same for Volno.
- **1.86 (89)** `Task.Run(() => _rosterBoardCacheService.GetCachedAsync())` so no synchronous slice of the relay fetch runs on the UI thread.
- **1.87 (90)** FIX (the big one): `OpicentrumBoardViewModel.LoadAsync` no longer loads the hidden Volno tab on every open (58 people x ~30 days ≈ 1,700 cells built on the UI thread, ui_stall 3,500 ms). Volno loads lazily in `SelectSubTab` (`_volnoLoaded`).
- **1.88 (91)** board rows filled incrementally: first 8 rows immediately, rest in batches of 7 (30 ms apart) — via `ApplyPositionsSnapshot`. UI stall dropped 680 ms -> ~380 ms.
- **1.89 (92)** user request: sticky workplace-name column + sticky day header. One `ScrollView Orientation="Both"`; `OpicentrumBoardPage.xaml.cs` translates name labels by `ScrollX` and the header by `ScrollY` on `Scrolled` (`StickyBoard`, `ApplySticky`); labels registered via `Loaded`/`Unloaded`; `ZIndex` 1/2/3; backgrounds `White`/`Gray950` AppThemeBinding. NOT visually verified on a device (FLAG_SECURE/DLP blocks screenshots) — user said only "Volno se načítá divně" afterwards, no complaint about the sticky layout.
- **1.90 (93)** Volno: first batch 28 rows (covers a screen), rest in batches of 10 every 120 ms; Volno cells are a single `Label` with background (was `Border`+`Label`).
- **1.91 (94)** cleanup (committed + released): `HttpBoardCacheServiceBase<T>` (two near-identical Http*BoardService classes), generic `OpicentrumBoardLocalCache<T>` (file `Workplace/OpicentrumBoardLocalCache.cs`; old two files deleted), generic `RunBackgroundOpicentrumBoardPublishAsync<TCache,TSnapshot>` in `App.xaml.cs`, shared `Infrastructure/IncrementalFill.cs`, relay: `MapOpicentrumBoard(route,set,get)` + `SetOpicentrumBoard/GetOpicentrumBoard(table,...)` + unified `SetOpicentrumBoardRequest/OpicentrumBoardResponse` contracts (same JSON on the wire). Incremental fill applies ONLY to an empty board (otherwise a rebuild after a move/reconcile would shrink and re-grow the board and jump scroll). `GetTargetWorkplaceNames` now reads `_positionsAllRows` (full list), not the partially-filled `PositionsRows`. Removed temp metrics `ui_stall`, `post_layout`, `click_to_appearing`/`ClickStopwatch`, `relay_fetch.steps`; kept `tti.*` and `opicentrum.positions.*`.

Code review (note: the OpicentrumSyncService.cs refactor was committed locally by the closing session commit; user: "Nevytvarej release rozdrl kod na casti... docasny plan a uskutecni ho") — status by part (4 LOCAL commits, unpushed; plus 1 uncommitted file):

| Part | Scope | Status | Commit |
|---|---|---|---|
| P1 | `relay/SecureApp.Relay` | DONE | `7ef7311` |
| P2 | `SecureApp.Domain` | DONE (read, no changes) | in `2e8f628` |
| P3 | `SecureApp.Data` | DONE | `2e8f628` |
| P4 | `App.xaml.cs`, root | DONE for App.xaml.cs; MauiProgram/AppShell/Infrastructure/Diagnostics/Profiles/Updates/Identity/Controls NOT read | `6fd358e` |
| P5 | Transport + Chat | DONE (SessionRecoveryHelper, SharedLibraryKeySync, WebSocketMessageTransport read) | `6fd358e`, `1b21a73` |
| P6 | Workplace + Logbook | PARTIAL: `OpicentrumSyncService.cs` refactored but UNCOMMITTED; Logbook, other Workplace files unread | working tree |
| P7 | Library, Cms, Contacts, Rendering, Translation, Notifications | NOT STARTED (only the Http*Service plumbing touched) | — |
| P8 | Platforms (Android/Windows) | NOT STARTED | — |
| P9 | ViewModels (51 files, 11.4k lines) | PARTIAL: `ChatViewModel` read; `GroupChatViewModel`, `SettingsViewModel*`, others unread | `1b21a73` |
| P10 | Views (70 files, 6.3k lines) | PARTIAL: thread-view attach picker deduped | `1b21a73` |
| P11 | Cross-cutting Http plumbing | DONE except 5 services with variant error handling | `6fd358e` |

Concrete review results (all verified to BUILD: Windows `net10.0-windows10.0.19041.0`, Android `net10.0-android`, relay — 0 errors; NOT run on any device):

- **Relay outbox message loss (real bug):** `RelayDatabase.DequeueOutbox` deleted every queued row BEFORE sending; a connection dropping mid-flush lost the rest permanently. Now `PeekOutbox(recipient)` + `DeleteOutboxFrame(id)` after each successful send (`Program.cs` /ws flush loop).
- **Relay unlocked socket sends (real bug):** `WebSocket.SendAsync` is not concurrent-safe; two senders to one recipient (or live delivery during outbox flush) threw `InvalidOperationException`, tore down the SENDER's connection and lost the message. New `ConnectionRegistry` holds `Connection(Socket, SendLock)`; `TrySendAsync(deviceId, json)` serializes, has a 10 s timeout, returns false on any failure → caller enqueues to outbox. Three copy-pasted live-or-outbox branches in `/ws` → local function `DeliverOrQueueAsync`. `RelayProtocol.SendRawAsync` removed (unused).
- Relay dedupe: `IsSafeFileName`, `FormatMegabytes`, `ServeJsonFile`, `WriteVersionManifestAsync`, `WriteFileAtomicAsync` (upload to `.uploading` temp then `File.Move(overwrite)` so a device downloading during an APK/zip upload never gets a truncated file), `IsAdminOrModifier`, `IsPiccTeamOrAdmin`, `TryGetAdminOrDevice`. `/library/files/{id}` now streams via `Results.File` instead of `File.ReadAllBytesAsync` (up to 200 MB in RAM before).
- **Ratchet race (real bug):** `RatchetService` had no per-session lock; two concurrent sends in one chat (e.g. a delivery ack racing a user message) read the same chain key and message number, and the receiver dropped the second as a "duplicate" header. Added static `ConcurrentDictionary<Guid,SemaphoreSlim> SessionLocks` (service is Scoped → must be static) around `RatchetEncryptAsync`/`RatchetDecryptAsync`.
- **Receive race (real bug):** both `App.OnEnvelopeReceived` (app-wide) and an open chat page handler call `MessagingService.ReceiveMessageAsync` for the same envelope; duplicate check ran before any lock → both passed, loser's decrypt failed (one-time key spent) → pointless auto-heal. Now dedupe + decrypt + store are one critical section per session (static `ReceiveLocks`).
- **Receive dedupe perf:** `ReceiveMessageAsync` loaded and materialized ALL messages of the session per incoming message → new `IMessageRepository.FindInboundByHeaderAsync(sessionId, dhPublicKey, messageNumber)` (SQL on `header_dh_public_key`/`header_message_number`, uses `ix_messages_chat_session_id`).
- **Schema v18** (`SqlCipherConnectionFactory.CurrentSchemaVersion = 18`, `ApplyV18SchemaAsync`): `CREATE INDEX IF NOT EXISTS ix_messages_origin_message_id / ix_messages_group_message_id` — every delivery ack/delete ran `WHERE origin_message_id = ? OR group_message_id = ?` as a full table scan.
- `ChatSessionRepository.GetByPeerPublicKeyAsync/GetAllByPeerPublicKeyAsync` now filter in SQL instead of loading all sessions.
- `EntityMaterializer`: cached `PropertyInfo`/`ConstructorInfo` (was `typeof(T).GetProperty` per property per row, ~17x per message row).
- Dedupes: `ActiveKeyHelper.GetOrCreateAsync` (3 copies of get-or-create vault key), `BouncyCastleCryptoService.AesGcmEncrypt/AesGcmDecrypt` (2x2 copies), `Persistence/SqlTime` (`Format`/`Parse` copied into 16 repositories via `using static`).
- **`ChatViewModel.LoadAsync`** now uses `IMessageRepository.GetVisibleBySessionAsync` (SQL: `group_chat_id IS NULL AND is_system_payload = 0`). System payloads (every delivery ack is stored as a message row on BOTH sides, plus key offers/requests) typically outnumber real messages and were being materialized then discarded in memory.
- **Client WebSocket (real bug):** `WebSocketMessageTransport.SendFrameAsync` unlocked → `SendFrameLockedAsync` with `_sendLock`; replaced socket + `CancellationTokenSource` are now disposed in `ConnectAsync`; receive-loop `finally` only raises `Disconnected` if `ReferenceEquals(_socket, socket)`.
- **Supervisor starvation (real bug):** `App.RunConnectionSupervisorLoopAsync` awaited the Opicentrum scrape (tens of seconds) inline, so a connection dropping DURING a scrape wasn't re-established until it finished. Background jobs now run via `RunBackgroundJobsAsync` single-flight (`_backgroundJobsRunning` + `Interlocked.CompareExchange`); orphaned duplicate `<summary>` doc comment fixed.
- Shared helpers: `Transport/RelayHttp` (`ToHttpUri`, `GetHttpEndpointAsync`, `AddDeviceAuthAsync`, `CreateAuthorizedRequestAsync`) replaces 8 endpoint + 10 auth + 3 `ToHttpUri` + 2 `CreateAsync` copies; `Transport/RelayConnection.EnsureConnectedAsync` replaces 4 identical `EnsureConnectedAsync` copies in `ChatViewModel/GroupChatViewModel/NewChatViewModel/NewGroupViewModel`; `Transport/SharedKeyWire.Combine/Split` replaces 3 copies; `Views/ChatAttachmentPicker` replaces ~55 duplicated lines in `ChatThreadView`/`GroupChatThreadView`.
- **UNCOMMITTED (working tree) `Workplace/OpicentrumSyncService.cs`:** 5 read-only fetch methods (`FetchStaffNamesAsync`, `FetchWorkplaceNamesAsync`, `FetchWorkplaceNamesForDateAsync`, `FetchFullRosterBoardAsync`, `FetchVolnoForMonthAsync`) now use new `WithSessionAsync<T>` (credentials → fresh cookie `HttpClient` → `LoginGate` → login → work → release; throws `InvalidOperationException(NotConfiguredMessage/LoginFailedMessage)`); `FetchVolnoForMonthAsync` uses ONE precompiled `LeaveGridRowRegex()` instead of constructing a `new Regex` per person (58x) each rescanning the page. `SyncAsync`, `PushNoteAsync`, `MoveRosterPersonAsync` keep their own preamble (they return result objects, not exceptions). Built OK (Windows, Android, relay). Regex equivalence NOT tested against real HTML (no credentials in tooling, by project rule).

Relay deployment/state right now: Pi relay runs the 1.91-era code (redeployed 2026-10-22 ~11:2x UTC after commit `2b3aef1`, both `/opicentrum-*-board` routes returned 401 unauthenticated = alive). The review's relay changes (outbox, ConnectionRegistry, uploads) are NOT deployed.

## What We Tried (Chronological)

1. **Ask: measure on the S23+.** Added `AppLog.Metric` calls + `tti.opicentrum_board_page` (release 1.82). Metrics auto-upload to the relay (`AppLogUploader`) and are read with `GET /admin/applog/{deviceId}?kind=metrics&limit=N` + `X-Admin-Secret`. First data (1.82, 08:54): cache hit path `cache_load 1.5 ms`, `apply 157.2 ms` (29 rows x 5 days), `load_total 161.8 ms`, `tti 163.1 ms` — FAST. Second open 09:03 (cache older than 5 min): `relay_fetch 5188.7 ms`, `load_total 5388.6 ms`, `tti 5388.9 ms` — this reproduced "3+ s". Hypothesis (spinner stays until relay reconcile ends) confirmed.
2. **1.84 fix (spinner off after local render).** Result: `tti 200.8 / 292.8 ms` even when `relay_fetch` still took 4840.5 / 6361.1 ms in the background. User still: "Přijde ti že to je lepší? Mě moc ne... najdi proč to tak trvá, jestli vykreslení nebo přímo dat".
3. **1.85 split data vs render:** `apply.build 5.6 ms`, `apply.assign 124.9 ms` (BindableLayout builds child views synchronously on assignment), `tti 199.8`, `tti.post_layout 199.9` (two `MainThread.BeginInvokeOnMainThread` hops — turned out to land immediately, so useless as a paint-complete proxy; removed in 1.91), `relay_fetch 4583.8 ms`. Data shaping is negligible; assign is the biggest C# slice.
4. **User: "zobrazení nemá čekat na relay... přesto se zobrazí až za 5 s"** → suspected UI-thread freeze (C# stopwatches stop before native layout/paint). 1.86: UI-thread heartbeat (`opicentrum.ui_stall`), `relay_fetch.steps`, `Task.Run`. Result: relay request itself only 90.4 ms total (`endpoint 19, auth 2, headers 36, body 29, deserialize 2`, `blobChars 21776`); `relay_fetch 916 ms` includes ~800 ms thread-pool/scheduling because the UI thread was busy; `ui_stall` max gap **3,499.9 ms**, 3 gaps > 300 ms. The 5 s relay number from before was NOT network time — the UI thread was blocked, delaying the continuation. Cause found by reading `OpicentrumBoardViewModel.LoadAsync`: it ran `Task.WhenAll(LoadPositionsAsync(), LoadVolnoAsync())` — the hidden Volno tab built ~1,700 cells on every open.
5. **1.87 lazy Volno.** `ui_stall` 679.9 / 679.9 / 678.9 / 668.8 / 768 ms; `relay_fetch` 507-709 ms; `tti` 191-293 ms. User: "rychlejší ale ne o moc".
6. **1.88 incremental fill (8 then 7 rows/30 ms).** `ui_stall` 393.1 / 392.8 / 358.7 ms; `apply.assign` 53.1 / 81.1 / 193.0 ms (first batch only). User: "Ok lepší ale vždy je co zlepšovat... ještě to prověř, asi stejný problém bude i se zobrazováním volna... okno s přehledem sálů rozdělit, názvy sálů jen nahoru/dolů synchronizovaně se jmény".
7. **Sticky column design (1.89).** Considered two synchronized ScrollViews (rejected: row heights differ per row because chip wrapping varies, so left and right panes drift) → chose ONE `ScrollView(Both)` and translate the name labels (`TranslationX = ScrollX`) + header (`TranslationY = ScrollY`). Also added incremental fill for Volno (12 rows, 12/batch, 30 ms).
8. **User: "Volno se načítá divně, neříkej mi že to nejde zobrazit celé... načti část aby nebylo poznat že se v pozadí načítá dále".** First batch of 12 rows did not fill a screen so rows visibly popped in. 1.90: first batch 28 rows, then 10/batch with 120 ms delay; Label instead of Border+Label per cell. Alternative NOT pursued: real virtualization (`CollectionView` rows inside a horizontal `ScrollView`) — would be the proper fix if pop-in/jank persists; sticky name sync via ScrollX would then need `BindingContextChanged`-based registration because recycled templates may not fire `Unloaded`.
9. **Code review.** Method: map sizes (~40k lines .cs/.xaml), temp plan in scratchpad, per part: read → fix behaviour-preserving issues → build → local commit (no push). Duplicate detection by 6-line sliding window across all files (after cleanup only small remaining clusters: `ToggleExpanded` x3 VMs, `OnRequestNavigate` x4 pages, `OnViewModelPropertyChanged` x4 pages, `Reset` x3, NotificationCategory→label switch x2, `[ObservableProperty] IsLoading` pattern).
10. **Faltus tunnel (earlier in session, brief).** User asked "Co upravit v tunelu aby Faltusovi chodily zprávy?" Diagnosis from prior session: his WireGuard peer has `PersistentKeepalive = 0` (mobile NAT silently drops idle mapping; handshake days old). Relay `/admin/devices` at 08:52 showed his `pendingOutboxCount: 0` (was 8, then 12). Told the user to add `PersistentKeepalive = 25` under `[Peer]` in the WireGuard app (field was missing in their UI → "dopsat"). Not verified with `wg show`; server-side `WG_PERSISTENT_KEEPALIVE=25` for wg-easy offered but NOT done (restarts all tunnels).

## Key Decisions

- **Measure, don't guess** (user's explicit ask) — every speed claim above is from the S23+'s own uploaded `metrics.log`.
- **Local-first display, silent reconcile** (user: "zobraz uložené A pak kontroluj změny") — reconcile must never gate `IsPositionsLoading`/`IsVolnoLoading` once local data is rendered.
- **Incremental fill only for an empty board.** Rejected: always incremental (board would shrink/re-grow and jump scroll after a move or relay update).
- **One ScrollView + translate** for the sticky column (rejected two synced scrollers; rejected `Grid` with fixed row heights — chips wrap).
- **Do not merge `ChatViewModel`/`GroupChatViewModel` logic.** `ChatThreadViewModelBase` already documents "deliberately NOT pulled up — forcing them into one class adds risk to the crypto-adjacent path". Respected.
- **Keep duplicated `SyncAsync/PushNoteAsync/MoveRosterPersonAsync` preambles** in `OpicentrumSyncService` — they return result objects with their own failure semantics; forcing them through the throwing `WithSessionAsync` would change behavior.
- **No data-retention change for system-payload messages** (acks/key requests accumulate forever in `messages`). Flagged below, not decided.
- **Do not set SQLite `journal_mode=WAL` on the relay** without testing on the Pi's volume — production DB mode change; recommended but not applied.
- **Review commits local only** (user: "Nevytvarej release"). Tried/respected: no push, no deploy, no version bump for review work.
- Compatibility: relay JSON wire format unchanged by the contract unification; the DB schema bump (v18) is additive `CREATE INDEX IF NOT EXISTS`.

## Evidence & Data

### Release / commit log (all 2026-10-22)

| Release | Build | Commit | What |
|---|---|---|---|
| 1.80 | 83 | `0d3e5a4` | Pozice local cache bundled |
| 1.81 | 84 | `4ad9464` | row tap, Vše button, Poznámka save, stale-skip (relay redeployed) |
| 1.82 | 85 | `6e04032` | profiling metrics |
| 1.83 | 86 | `7dc589c` | click→appearing metric (later removed) |
| 1.84 | 87 | `ec7634c` | spinner off after local render |
| 1.85 | 88 | `5c199c1` | apply.build/assign split, post_layout |
| 1.86 | 89 | `b1cd2ab` | ui_stall monitor, relay_fetch.steps, Task.Run |
| 1.87 | 90 | `0211bed` | lazy Volno (root cause) |
| 1.88 | 91 | `a794b33` | incremental Pozice fill |
| 1.89 | 92 | `3885e3a` | sticky names/header, Volno incremental |
| 1.90 | 93 | `c192a41` | Volno first batch 28, lighter cells |
| 1.91 | 94 | `2b3aef1` | cleanup/dedupe, relay redeployed |
| review P1 | — | `7ef7311` | local only |
| review P2/P3 | — | `2e8f628` | local only |
| review P4/P5/P11 | — | `6fd358e` | local only |
| review P5–P10 partial | — | `1b21a73` | local only |

### Pozice open timings on S23+ (device `6b4bf8e9-131b-4304-8368-c831a32c5a2b`)

| Build | Time | path | apply (ms) | relay_fetch (ms) | load_total (ms) | tti (ms) | ui_stall max (ms) |
|---|---|---|---|---|---|---|---|
| 1.82 | 08:54 | local_only | 157.2 | — | 161.8 | 163.1 | n/a |
| 1.82 | 09:03 | reconciled | 194.9 | 5188.7 | 5388.6 | 5388.9 | n/a |
| 1.82 | 09:11 | local_only | 235.1 | — | 239.3 | 239.6 | n/a |
| 1.84 | 10:32 #1 | reconciled | 132.8 | 4840.5 | 4992.9 | 200.8 | n/a |
| 1.84 | 10:32 #2 | reconciled | 280.5 | 6361.1 | 6645.0 | 292.8 | n/a |
| 1.85 | 10:53 | reconciled | 134.6 (build 5.6 + assign 124.9) | 4583.8 | 4745.6 | 199.8 | n/a |
| 1.86 | 11:14 | reconciled | 152.1 (assign 145.1) | 916.0 | 1090.7 | 233.3 | 3499.9 |
| 1.87 | 11:46 (x4) | reconciled | 129.1 / 221.6 / 182.8 / 179.4 | 507.4 / 621.7 / 693.0 / 709.9 | 657.6 / 846.4 / 878.3 / 892.1 | 201.6 / 236.3 / 194.1 / 191.6 | 679.9 / 679.9 / 678.9 / 668.8 |
| 1.87 | 12:21 | local_only | 203.0 | — | 206.6 | 238.4 | 768.0 |
| 1.88 | 12:37 | reconciled | 62.3 / 84.6 / 195.1 (first batch 8) | — | 474.3 / 489.1 / 563.3 | — | 393.1 / 392.8 / 358.7 |

### Relay fetch breakdown (1.86/1.87, `opicentrum.positions.relay_fetch.steps`)

`total 54-138 ms` = endpoint 0-19 + auth 0-5 + headers 25-52 + body 14-105 + deserialize 0-7; payload `blobChars=21776` (≈21 KB). The network/relay is NOT the bottleneck; earlier "5 s" figures were UI-thread contention.

### Root-cause ladder (Pozice "3+ s")

1. spinner over rendered board while stale-reconcile relay call ran → fixed 1.84.
2. hidden Volno tab (≈1,700 cells) built on every open → ui_stall 3,500 ms → fixed 1.87.
3. all 29 rows laid out at once (≈145 cells with Border+FlexLayout+chips) → ui_stall ≈ 0.7 s → incremental fill 1.88 → ≈ 0.36-0.39 s.
4. remaining: sync BindableLayout construction `apply.assign` 53-220 ms for the first batch.

### Review-period facts

- Repo size: ViewModels 51 files / 11,397 lines; Views 70 / 6,271; relay 7 / 4,553 (`Program.cs` 1,628, `RelayDatabase.cs` 2,561); Presentation root 5 / 2,194; Workplace 18 / 2,062; Data Repositories 16 / 1,533; Domain Interfaces 51 / 1,290; Transport 9 / 1,140; Platforms 19 / 1,126.
- Net effect of the 1.91 cleanup commit: 16 files, +289 / -487 lines.
- Faltus device `c1b2f0ab-b6c9-4b20-a86b-94cac376b13c` ("Zařízení S25 uživatele Petr", directory name "Petr Faltus"): `pendingOutboxCount` 8 → 12 → **0** (2026-10-22 08:52), `lastActiveAtUtc 08:48:18Z`. Other devices all 0 pending: S23+ `6b4bf8e9-...`, S9+ `a2197eb1-5778-49eb-80dc-c84adfad6f80`, "PC v praci" `b28193a8-...`, `DESKTOP-F8AS2U6` `744abac3-...`.

### Data files (scratchpad, may vanish)

- `...\scratchpad\m4.json`, `m5.json`, `m6.json`, `m7.json`, `metrics2.json`, `metrics3.json` — raw `/admin/applog` pulls (JSON arrays of tab-separated lines).
- `...\scratchpad\SecureApp-1.83..1.91-windows.zip` — release zips (1.91 is the live one).
- Full transcript: `C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\d24099f2-6702-4548-a5f6-41bbcd4578b5.jsonl`.

### Review findings: applied vs deliberately NOT applied

| # | Area | Finding | Severity | Action |
|---|---|---|---|---|
| 1 | relay `/ws` | outbox rows deleted before send → loss on mid-flush drop | message loss | fixed (`PeekOutbox`/`DeleteOutboxFrame`) |
| 2 | relay `/ws` | concurrent `SendAsync` on one recipient socket → exception kills SENDER connection | message loss | fixed (`ConnectionRegistry.TrySendAsync`) |
| 3 | `RatchetService` | no per-session lock → same message number for two sends → receiver drops 2nd as duplicate | message loss | fixed (`SessionLocks`) |
| 4 | `MessagingService.ReceiveMessageAsync` | App handler + chat-page handler race past dedupe → spurious decrypt failure → auto-heal | spurious resync | fixed (`ReceiveLocks`) |
| 5 | `WebSocketMessageTransport` | unlocked client sends; leaked replaced socket; stale loop flips state | message loss / UI state | fixed |
| 6 | `App` supervisor | scrape (tens of s) inline in the reconnect loop | delayed reconnect | fixed (single-flight background) |
| 7 | `ReceiveMessageAsync` | loads all session rows for dedupe | perf O(n) per message | fixed (SQL lookup) |
| 8 | `messages` table | no index on `origin_message_id`/`group_message_id` | perf (scan per ack) | fixed (schema 18) |
| 9 | `ChatViewModel.LoadAsync` | materializes ack/system rows then filters | perf on long chats | fixed (SQL filter) |
| 10 | `EntityMaterializer` | `GetProperty` per field per row | perf | fixed (cache) |
| 11 | `FetchVolnoForMonthAsync` | `new Regex` per person (58) | perf | fixed (uncommitted) |
| 12 | relay `/library/files/{id}` | whole file read into RAM | memory | fixed (stream) |
| 13 | relay uploads | `File.Create` truncated the live APK/zip during upload | broken downloads | fixed (temp + rename) |
| 14 | relay SQLite | no WAL | concurrency/perf | NOT applied (needs Pi test) |
| 15 | relay `DeregisterDevice` | leaves `device_policy`, wrapped keys, pending rows orphaned | minor | NOT applied |
| 16 | relay `Program.cs` | `TryGetDeviceAuth ... Unauthorized()` boilerplate ~100x | style | NOT applied (idiomatic minimal API; an endpoint filter would change behavior risk) |
| 17 | `SqlCipherConnectionFactory` | migrations not in a transaction | robustness | NOT applied |
| 18 | `messages` | system payloads (acks/key offers/requests) never pruned | growth | NOT applied (product decision) |
| 19 | `Message.MarkDelivered/MarkSent` | can downgrade status in theory | none in practice (callers only use it at creation / Pending→Sent; repo upgrade query uses `status < Delivered`) | NOT applied |
| 20 | Room status | a note can only be changed, not cleared (blank → null = "leave untouched", so clearing needs an explicit empty option) | UX limitation | NOT applied, mention to user if asked |
| 21 | `ChatViewModel` vs `GroupChatViewModel` | 15 methods with the same names | duplication | NOT merged (documented design decision; crypto-adjacent) |
| 22 | `NotificationRepository.MarkThreadReadAsync` | one UPDATE per row | minor perf | NOT applied |
| 23 | `App.PruneChurnedSessionsAsync` | loads all messages of a session just to test "empty" | one-off at launch | NOT applied |

### Residual duplicate clusters (6-line window scan after cleanup)

- `HttpSharedContactService`/`HttpRoomStatusService`/`HttpWorkplaceCatalogService`: `await AddDeviceAuthAsync; return response.IsSuccessStatusCode; catch { return false; }` wrapper bodies
- `ToggleExpanded` in `ContactsViewModel`, `LogbookViewModel`, `NotificationsViewModel`
- `OnRequestNavigate(string route)` in `NotificationDetailPage`, `NotificationsPage`, `RedakcePage`, `SmartSearchPage`
- `OnViewModelPropertyChanged` in `DocumentViewerPage`, `LibraryPage`, `LibraryReviewQueuePage`, `OpicentrumBoardPage` (different bodies, same shape)
- NotificationCategory → Czech label `switch` in `NotificationDetailViewModel` and `NotificationsViewModel`
- `[ObservableProperty] IsLoading` + `StatusErrorMessage/HasStatusError` pairs repeated across `CmsAnnotationQueueViewModel`, `LibraryReviewQueueViewModel`, `LogbookChecklistViewModel`, `LogbookViewModel`
- Translation start block duplicated between `LibrarySubcategoryDetailViewModel` and `LibraryViewModel.Actions`
- `GroupChatViewModel` and `NewGroupViewModel` both loop "find existing session for member"

### What was verified vs NOT verified

| Item | Verified how | Result |
|---|---|---|
| Windows/Android/relay builds after each batch | `dotnet build ... -c Release` | 0 errors |
| Release artifacts 1.91 on relay | `GET /download/{android,windows}/version` | 94 / 1.91 both |
| Relay alive after redeploy | unauthenticated GET of both board routes | 401 (route exists) |
| S23+ measurements for 1.82-1.88 | `/admin/applog` pull | table above |
| Sticky column behavior (1.89) | NOT verified (screen capture blocked) | user did not complain |
| Review changes at runtime | NOT verified | never installed on a device |
| New Opicentrum regex vs live HTML | NOT verified | no credentials by project rule |
| Faltus keepalive applied | NOT verified | ambiguous reply |

## Code Analysis

- `OpicentrumBoardViewModel` (partials `.cs`, `.Positions.cs`, `.Volno.cs`): `LoadCommand` → `LoadAsync` (role gate `IsAccessDenied`, then `LoadPositionsAsync`; Volno only via `SelectSubTab(1)` once). `ApplyPositionsSnapshot` builds `OpicentrumRosterRowItem` list (LINQ, ~1-6 ms for 29 rows) then `PositionsRows = IncrementalFill.Start(rows, firstBatch: 8, nextBatch: 7, delayMs: 30, isCurrent: version check)` only if `PositionsRows.Count == 0`; `_positionsFillVersion` cancels stale fills. `MovePersonAsync` patches the snapshot locally (`PatchMove`), saves local cache, publishes best-effort.
- `IncrementalFill.Start<T>` (`Infrastructure/IncrementalFill.cs`): creates `ObservableCollection<T>` with first batch, then `Task.Delay` + `MainThread.BeginInvokeOnMainThread` per batch, abort when `isCurrent()` false.
- Board XAML (`Views/OpicentrumBoardPage.xaml`): nested `BindableLayout` (rows → cells → chips) inside `ScrollView Orientation="Both"`; `HasPositionsData`/`HasVolnoData` gate visibility, `DelayedActivityIndicator` bound to `IsPositionsLoading`/`IsVolnoLoading`. Chip tap → `OnPersonChipTapped` → two `DisplayActionSheet` calls (move vs also-assign, target workplace) → `MovePersonAsync`.
- Opicentrum write-back mechanics (from the prior session): `pracoviste.php?akce=<0|2|666>&datum=yyyyMMdd&sal1=<src>&idl=<personId>&sal2=<target>`; `akce=0` move, `akce=2` also-assign, `akce=666` + `sal2=999` = NEZAŘAZENÍ; result confirmed by re-fetching the week.
- `ConnectionRegistry.TrySendAsync(Guid, string)`: `SemaphoreSlim` per connection, 10 s `CancellationTokenSource`, catches `WebSocketException/OperationCanceledException/ObjectDisposedException/InvalidOperationException`.
- `MessagingService.ReceiveMessageAsync`: `ReceiveLocks[sessionId]` → `FindInboundByHeaderAsync` → `RatchetDecryptAsync` → `EncryptAsync(storage key)` → `AddAsync`; then (outside the lock) `session.NoteRatcheted()` + audit.
- `RatchetService`: Double-Ratchet adaptation = symmetric HKDF chains only (no DH ratchet); `MaxSkippedMessages = 1000`; skipped keys stored in the vault as `ratchet:{session}:skipped:{n}`.
- `App.xaml.cs` supervisor: 10 s tick; forced reconnect every 5 min; stale-session sweep every 3 min (stale peers, identity reconciliation, device policy, board→notifications, crash report, admin alerts, PICC status, AppLog upload, contact phone cache); background jobs now single-flight and off-tick.
- Relay `/ws`: first frame must be `auth`; then flush outbox; then `send` / `pairing` / `group-invite` frames routed via `DeliverOrQueueAsync`.

## Files Changed

### Source code — shipped (≤ 1.91)
- `src/SecureApp.Presentation/ViewModels/OpicentrumBoardViewModel.cs`, `.Positions.cs`, `.Volno.cs` — lazy Volno, local-first load, stale-skip, incremental fill, metrics
- `src/SecureApp.Presentation/Views/OpicentrumBoardPage.xaml(.cs)` — sticky names/header, `StickyBoard`, TTI metric; Volno cell Label
- `src/SecureApp.Presentation/Views/NotificationsPage.xaml(.cs)` — Vše button, row tap target
- `src/SecureApp.Presentation/ViewModels/RoomStatusViewModel.cs`, `Workplace/HttpRoomStatusService.cs`, `Domain/Interfaces/Services/IRoomStatusService.cs`, `relay/.../RelayDatabase.cs|Program.cs|Contracts.cs` — Poznámka-only save
- `src/SecureApp.Presentation/Infrastructure/IncrementalFill.cs` (new), `Workplace/HttpBoardCacheServiceBase.cs` (new), `Workplace/OpicentrumBoardLocalCache.cs` (new; replaces `OpicentrumRosterBoardLocalCache.cs` + `OpicentrumVolnoBoardLocalCache.cs`), `Workplace/HttpOpicentrum{Roster,Volno}BoardService.cs` (now 7 lines each)
- `src/SecureApp.Presentation/App.xaml.cs` — generic board publish job
- `src/SecureApp.Presentation/SecureApp.Presentation.csproj` — version 1.91 / 94

### Source code — review, LOCAL COMMITS ONLY (not pushed, not released)
- `relay/SecureApp.Relay/ConnectionRegistry.cs`, `Program.cs`, `RelayDatabase.cs` (`PeekOutbox`, `DeleteOutboxFrame`), `RelayProtocol.cs`
- `src/SecureApp.Data/Cryptography/ActiveKeyHelper.cs` (new), `BouncyCastleCryptoService.cs`, `Import/DocumentImportService.cs`, `Messaging/MessagingService.cs`, `Messaging/RatchetService.cs`, `Persistence/EntityMaterializer.cs`, `Persistence/SqlCipherConnectionFactory.cs` (schema 18), `Persistence/SqlTime.cs` (new), all 16 `Repositories/*.cs` (`using static SqlTime`), `Repositories/MessageRepository.cs` (`FindInboundByHeaderAsync`, `GetVisibleBySessionAsync`), `Repositories/ChatSessionRepository.cs`
- `src/SecureApp.Domain/Interfaces/Repositories/IMessageRepository.cs` (two new methods)
- `src/SecureApp.Presentation/Transport/RelayHttp.cs`, `RelayConnection.cs`, `SharedKeyWire.cs` (new), `WebSocketMessageTransport.cs`, 8+ `Http*Service.cs` files, `Views/ChatAttachmentPicker.cs` (new), `Views/ChatThreadView.xaml.cs`, `Views/GroupChatThreadView.xaml.cs`, `ViewModels/ChatViewModel.cs`, `GroupChatViewModel.cs`, `NewChatViewModel.cs`, `NewGroupViewModel.cs`, `App.xaml.cs`

### Source code — committed locally in the closing 'session:' commit (not pushed)
- `src/SecureApp.Presentation/Workplace/OpicentrumSyncService.cs` (+55 / -111) — `WithSessionAsync`, `LeaveGridRowRegex`

### Data & results
- Scratchpad JSON pulls listed above; `REVIEW_PLAN.md` (temp plan)

## User Feedback & Preferences (REQUIRED)

All in Czech; quotes preserved.

- "Ok udelej release" / earlier standing pattern: after a fix, the user usually wants a signed Release built, pushed (`pi` + `github`), uploaded, and verified — but NOT for the review: **"Nevytvarej release rozdrl kod na casti abys ho mohl komplet projet se stejnymy pozadavky udelej si docasny plan a uskutecni ho..."**
- "Uspi pc." — put the PC to sleep (done with `rundll32.exe powrprof.dll,SetSuspendState 0,1,0`).
- Efficiency: "bylo by dobre kdyby se to nemuselo nacitat zbytecne... nevytezovat" (don't hammer opicentrum.cz; carried from the earlier feature spec).
- "zobraz ulozene A pak kontroluj zda nejsou zmeny a ty kdyztak oprav ale to zobrazovani trava strasne dlouho...nekdy i vice nez 3 s..." — show cached data immediately, verify in background.
- "Promer na s 23+ proc trva zobrazeni rozpisu pracovis..." — profile on the user's own phone.
- "Zkus to znovu je to rohodne drive zmer od kliknuti na otevrit po zobrazeni..." — measure from the tap, not from `OnAppearing`.
- "Prijde ti ze to je lepsi ? me moc ne... najdi proc to tak trva jestli vykresleni nebo prim dat nebo co..." — wants root cause (rendering vs data), not a plausible-sounding guess.
- "Ano rozeber... ale zobrazeni nema cekat na relay... presto se zobrazi az za 5 s...." — insisted display must not wait on the relay; this pushed the investigation from network to UI-thread blocking.
- "Ok lepsi ale vzdy je co zlepsovat... jeste to prover, asi stejny problem bude i se zobrazovanim volna... taky bych chtel aby se okno s prehledem salu rozdelilo a nazvy salu se mohly pohybovat jen nahoru a dolu synchronizovane se jmeny ale do boku by se hybaly jen jmena aby se neztracel prehled na ktery sal se koukam..." — sticky workplace-name column.
- "Volno se nacita divne, nerikej mi ze to nede zobrazit cele... kdyz tak nacti cast aby nebylo poznat ze se v pozadi nacita dale...." — don't tell him something "can't be fully shown"; make progressive loading invisible.
- "Projed kod zkontroluj logiku duplicitni kody, moznosti zjednoduseni ...slouceni, ale ne na ukor zpomaleni..." — merge/simplify only if not slower.
- Style: terse Czech replies, honest status ("zatím jsem nic nezměřil"), short end-of-turn summaries. He tests builds himself ("Hotovo", "Aktualizovano...") and then asks me to pull metrics from the relay.
- The last tool call (a heuristic dead-code scan script) was **rejected by the user**, who then invoked `/handoff` — treat as "stop and wrap up", do not resume that script without asking.

## Smoke-test checklist for the unreleased review work (do on S23+ and PC after a 1.92 build)

1. Fresh start: app launches, schema migrates (user_version 17 → 18) without error; `errors.log` has no migration entries.
2. 1:1 chat A↔B: 10 messages each way quickly; all arrive once, in order; delivery ticks (✓ → ✓✓) update.
3. Same chat: send while the peer's ack is arriving (type fast right after receiving) — no "[Tuto zprávu se nepodařilo dešifrovat]", no `session.resync.start` in metrics.
4. Group chat with ≥3 members: send, receive, ack aggregation (✓✓ only after every leg is delivered).
5. Kill the phone's network for ~1 min while the PC sends 5 messages; reconnect; all 5 arrive exactly once (outbox path); repeat while toggling the network during the flush.
6. Open a chat with >1,000 messages: compare `chat.open.load` (`bg` value) before/after; expect lower.
7. Relay: `docker logs` has no `InvalidOperationException` from `/ws`; upload a new APK while a device downloads — download completes.
8. Opicentrum: open Pozice (`tti.opicentrum_board_page` < ~300 ms, `opicentrum.positions.apply.assign` for the first batch), open Volno (≈58 people), move one person (write-back + local patch, no scroll jump).
9. Settings → Aktualizace shows the new version; background update check still works (jobs now run off the supervisor tick — look for `update.bg-download.started`).

If something regresses and a quick rollback is needed: the safe boundary is commit `2b3aef1` (release 1.91, what users have). `git revert` the four review commits individually; the relay and the app are independent (the relay changes do not require a new client). Schema v18 is additive — an older app version simply ignores the extra indexes.

## Infra notes learned/used this session

- Relay base `http://192.168.50.8:8080` (LAN) — admin endpoints use header `X-Admin-Secret`; device endpoints use `X-Device-Id` + `X-Device-Secret`.
- Pi git remote `pi` = `ssh://192.168.50.8/home/dvorakv1/secureapp-repo.git`; GitHub remote `github` = `git@github.com:Klotho79/SecureApp.git`.
- `POST /admin/deploy` only drops a marker file; a host watcher rebuilds the container (~60-90 s window where the relay is down → `curl` exit 7). Needed only when `relay/` code changed.
- `GET /admin/devices` (admin secret) lists devices with `pendingOutboxCount`, `lastActiveAtUtc`, `platform` — handy health check for the outbox bug.
- ADB: `adb connect 10.8.0.3:5555` for the S23+ over WireGuard; locked screen blocks UI automation; package `com.companyname.secureapp.presentation`; launch via `monkey -p ... -c android.intent.category.LAUNCHER 1` (activity class name is CRC-hashed per build, do not guess it).
- Metrics pipeline: `AppLog.Metric(name, value, "ms", ("tag", value)...)` — tags must be passed POSITIONALLY (a named `tags:` argument does not compile for the `params` array). Lines are TAB-separated: `[version (build)] timestamp<TAB>name<TAB>value<TAB>k=v...`.

## Where We're Going

1. **Decide what to do with the unreleased review work.** All four local commits + the uncommitted `OpicentrumSyncService.cs` build cleanly on Windows/Android/relay but were never run on a device. Suggested order: commit `OpicentrumSyncService.cs` (git status first), `git push pi main` + `git push github main` ONLY after the user agrees.
2. **If the user wants it shipped:** redeploy the relay first (`POST /admin/deploy`, wait ~60-90 s until `/opicentrum-roster-board` returns 401), then release **1.92 (build 95)**: bump `ApplicationDisplayVersion`/`ApplicationVersion` with the Edit tool (NOT Get-Content/Set-Content — it corrupts UTF-8 comments, learned the hard way), Android + Windows `dotnet publish -c Release`, commit, push, upload both artifacts, verify versions. Watch after rollout: first launch runs schema v18 migration on every device (additive indexes only).
3. **Smoke-test on devices before/after release:** send 1:1 + group messages in both directions, concurrent send while an ack arrives, a reconnect mid-outbox-flush, open a long chat (check `chat.open.load` metric vs before), confirm delivery ticks (✓✓) still update (`MarkDeliveredByCorrelationAsync` now index-backed).
4. **Finish the review (not started / partial):** P7 Library/Cms/Contacts/Rendering/Translation/Notifications; P8 Platforms; P9 ViewModels (`GroupChatViewModel` 1,158 lines, `SettingsViewModel` 831 + `.Community` 551 + `.Updates` 370, `WorkplaceViewModel` 477, `LibraryViewModel` 448, `ContactsViewModel` 415); P10 Views (big code-behind: `ChatListPage` 322, `DocumentViewerPage` 320, `CmsEditorPage` 296); P4 leftovers (`MauiProgram.cs`, `AppShell.xaml.cs`, `Infrastructure/*`, `Diagnostics/*`); P6 leftovers (Logbook, `OpicentrumParsing`, `PiccRosterParser`). Same rules: behavior-preserving only, no slowdowns, build, local commit.
5. **Verify the 1.89/1.90 UI on the S23+:** sticky names stay pinned and opaque (dark mode background `Gray950` vs actual page background may differ slightly), chip tap still works, Volno no longer pops in. No metrics were collected for 1.89-1.91; pull `GET /admin/applog/6b4bf8e9-131b-4304-8368-c831a32c5a2b?kind=metrics&limit=400` after the user opens the board.
6. **Faltus:** confirm his tunnel via `wg show` handshake time (needs the Pi) or `/admin/devices` `lastActiveAtUtc`/`pendingOutboxCount`; decide on server-side `WG_PERSISTENT_KEEPALIVE=25`.

## Risks & Blockers

- **Unreleased, unrun review changes touch the message path** (ratchet lock, receive critical section, relay outbox, WebSocket send lock). Logic is straightforward but zero on-device testing. A deadlock risk was considered and rejected on paper: `ReceiveLocks` → `SessionLocks` is the only nesting order; `SendMessageAsync` takes only `SessionLocks`.
- `FindInboundByHeaderAsync` relies on sqlite-net binding a `byte[]` parameter as BLOB for `header_dh_public_key = ?` — untested; if it never matches, duplicates would no longer be recognized (decrypt of a replayed envelope would then fail like before and auto-heal). Check first.
- Schema v18 migration is not transactional (existing design: `ApplyPendingMigrationsAsync` has no transaction; `ALTER TABLE ADD COLUMN` steps from older versions would fail on re-run after a mid-migration crash). v18 itself is idempotent.
- Duplicate-ack storage growth: every received real message makes an outbound ack row AND every received ack an inbound system row; key offers (every 6 h/session) and requests (every 15 min/keyless session) too. Nothing prunes them. Product decision needed (e.g. delete system payloads older than N days).
- `OpicentrumSyncService` regex change cannot be validated against live HTML in this tooling (credentials must never pass through chat/dev tooling — project rule). Validate by opening Volno on a device after release (58 people expected, `opicentrum.volno-board.bg-publish ... people=58` log line already exists).
- S23+ is the user's Remote Control phone with PIN/biometric: adb UI automation stops when it locks (see earlier attempt); `adb connect 10.8.0.3:5555` works but only the user can unlock. Don't adb-tap it blindly.
- HARD RULE (memory): only signed Release builds on real devices, never Debug.

## Repo / tooling gotchas hit this session

- `Get-Content | Set-Content` on a UTF-8 file without BOM (e.g. the csproj) re-saved it as ANSI-misread text with a BOM: em dashes became `â€”` in comments. Recovered with `git checkout -- <file>` (safe only because the sole uncommitted change in it was mine). Use `[IO.File]::ReadAllText/WriteAllText(..., new UTF8Encoding($bom))` or the Edit tool.
- Regex-replacing a method by `(?s)...\n    \}` can swallow the preceding blank line/comment (glued `}    private ...` on one line, or double indentation). Always re-read the touched area afterwards.
- PowerShell here-strings inside `-replace`/`.Replace` calls with `@"` break when text follows the header on the same line; use the Edit tool instead.
- `dotnet build` output is Czech: errors show as `Počet chyb: N` and ` error `; grep for both.
- CRLF/LF warnings from git are cosmetic (`LF will be replaced by CRLF`); working tree files are LF.
- The Windows publish zip must be flat (`Compress-Archive -Path publish\*`), not the folder itself.
- `Task.Run(() => service.GetCachedAsync())` was added only as a safety net; the 1.86 data showed it adds ≈ 800 ms scheduling latency while the UI thread is busy, which was a symptom of the Volno freeze, not a cost of the fetch itself.

## Open Questions

- Should the system-payload retention (acks/key offers/requests) be capped, and how (age vs count)? It affects the `messages` table growth and `is_system_payload` rows.
- Enable WAL (`PRAGMA journal_mode=WAL`) on the relay SQLite after testing on the Pi? Would help concurrent reads during writes.
- Is the Pozice first-paint now good enough (≈ 0.36-0.39 s stall), or does the user want true virtualization (`CollectionView`) for both boards?
- Remaining 5 Http services with different failure semantics (`HttpDocumentDownloadLogService`, `HttpPiccRosterService`/`HttpCommunityBoardService` own endpoint code, `HttpDevicePolicyService`, `AppLogUploader`) — fold into `RelayHttp` too, or leave?
- Faltus: did he actually apply the keepalive change (his "Aktualizováno" reply was ambiguous between that and the app update)?

## Quick Start for Next Session

```bash
# Verify git state first (git.exe is NOT on PATH):
#   C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe
#   expect: branch main, HEAD = the 'session: opicentrum-perf-code-review' commit on top of 1b21a73 (it contains OpicentrumSyncService.cs + this handoff), remotes pi/github still at 2b3aef1 (nothing pushed)
git status -s ; git log --oneline -6

# Memory index
# C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md

# Key files to read first
# src/SecureApp.Presentation/ViewModels/OpicentrumBoardViewModel.Positions.cs   (incremental fill, local-first load)
# src/SecureApp.Presentation/Workplace/OpicentrumSyncService.cs                  (uncommitted WithSessionAsync refactor)
# relay/SecureApp.Relay/ConnectionRegistry.cs  + Program.cs "/ws" handler         (new send lock / outbox flush)
# src/SecureApp.Data/Messaging/MessagingService.cs + RatchetService.cs            (new locks)
# src/SecureApp.Presentation/Transport/WebSocketMessageTransport.cs               (send lock)

# Build verification (all were 0 errors at handoff time)
dotnet build src\SecureApp.Presentation\SecureApp.Presentation.csproj -f net10.0-windows10.0.19041.0 -c Release
dotnet build src\SecureApp.Presentation\SecureApp.Presentation.csproj -f net10.0-android -c Release
dotnet build relay\SecureApp.Relay\SecureApp.Relay.csproj -c Release

# Read device metrics (admin secret is NOT written here because handoffs are committed/pushed;
# ask the user or read SECUREAPP_RELAY_ADMIN_SECRET from the Pi's docker env)
curl.exe -s -H "X-Admin-Secret: <secret>" "http://192.168.50.8:8080/admin/applog/6b4bf8e9-131b-4304-8368-c831a32c5a2b?kind=metrics&limit=400"
#   parse: (Get-Content -Raw file.json | ConvertFrom-Json) | Where-Object { $_ -match 'opicentrum|tti\.' }

# Release recipe gotchas
#  - git commit message: single-quoted here-string, NO double-quote characters inside (they split the native arg)
#  - GIT_SSH_COMMAND must use forward slashes: "ssh -F C:/Users/dvora/.ssh/config"
#  - edit csproj version only with the Edit tool; PowerShell 5.1 Get/Set-Content corrupts UTF-8
#  - Android upload: multipart fields apk=@file, versionCode, versionName (all form fields); Windows: --data-binary body + ?versionCode=&versionName=
#  - relay redeploy: POST /admin/deploy, then poll /opicentrum-roster-board until 401
#  - cd changes the tool's working dir; prefer absolute paths

# Next action
# Ask the user: ship the review (push + relay deploy + release 1.92) or keep reviewing? First commit
# OpicentrumSyncService.cs if shipping. Do NOT resume the rejected dead-code scan without asking.
```

## Session Closed
**Closed at:** 2026-10-22 15:43
**Commit:** 6f3fbd8
**Session status:** Handed off to next session

