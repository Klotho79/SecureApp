# SecureApp: tab-bar bug marathon, chat/schedule notifications, background messaging, per-device admin logs, contacts phone parsing — releases 1.10→1.17

**Date:** 2026-09-28
**Status:** IN PROGRESS
**Bead(s):** none
**Epic:** none
**Chain:** `standalone-80b5bcbd` seq `1`
**Parent:** none — first in chain
**Prior chain:** none — first in chain

---

## Reference Documents

- `DEVELOPMENT_PLAN.md` (repo root) — canonical milestone/task tracker for the whole project (Milestones 1–6). Milestones 1–4 done; Milestone 5 (E2EE chat) done + shared library follow-up; Milestone 6 (assembly) essentially complete for document+chat+library.
- `IMPROVEMENT_PLAN.md` (repo root) — the living "Phase 0–4" plan this session's work maps onto: Phase 1 (speed) mostly done, Phase 2 (robustness/lifecycle) mostly done, **Phase 2b (systematic functionality audit) NOT STARTED — explicitly deferred this session**, **Phase 3 (deliberate visual design) NOT STARTED — explicitly deferred this session**, Phase 4 (easy customization: theme/accent/font size/tab visibility) now essentially complete after this session's font-size fix + chat appearance.
- `NOTIFICATION_HUB_SPEC.md` (repo root) — Notification Hub spec; Phase 9 (archive virtualization/scale) and the §26 Notification↔Workplace↔Calendar cross-link were both listed "not started" as of 2026-09-24 — **the §26 cross-link is now DONE** as of this session (NotificationCategory.Schedule), not yet reflected in that file.
- `keystore/README.md` — release keystore facts (git-ignored dir, restore-don't-regenerate warning).
- `keystore/PRISTUPY.md` — **NEW this session**, git-ignored, Czech-language overview of every credential/access path in the project. No secret values, only what/where/why. See "Files Changed" below.
- Memory files (persist across sessions, at `C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\`): `adb-wireless-technique.md` (updated this session — fixed port 5555, WiFi-then-WireGuard order), `feedback-batch-builds.md` (created this session — do not build/install/release after every single fix), `secureapp-infra-paths.md`, `project-original-spec.md`.

## The Goal

SecureApp is a .NET MAUI (Android/iOS/Windows) Clean-Architecture app for a hospital anesthesiology/ICU team: E2EE chat (Double Ratchet over a self-hosted relay on a Raspberry Pi), a shared encrypted document/procedure library, a duty logbook, a company phone directory, and — the most actively-developed area right now — a personal work-schedule module ("Rozpis") that syncs against the hospital's own legacy phpRS staff portal (Opicentrum/ARO), plus a "Nástěnka" (notification hub) home screen and an Android home-screen widget. This multi-day stretch (2026-09-26 through 2026-09-28) covers: a real, previously-invisible Opicentrum data-completeness bug; a labeling improvement (weekends/holidays instead of a bare "no record"); a three-attempt bug hunt for a tab-bar navigation defect that was actively breaking the app for the user in production; a batch of UX asks (notifications open the chat directly, per-thread notification clearing, app-wide font size that actually works, chat-specific font/bubble colour, a widget mode switch for duty roster, phone-number classification with tap-to-call); and — the largest single piece — background messaging (a real production outage this exact session's own log data proved was happening) plus per-device admin-only diagnostic logs shipped automatically from every phone to the relay. The user (a physician, sole developer-by-proxy, admin of a ~4-device community) drives every feature from their own real usage and explicit Czech-language asks; the working pattern is: implement, compile-check, batch several changes, ask before building/installing/releasing (an explicit, saved preference — see "User Feedback & Preferences").

## Where We Are

- **Version history this session:** 1.9(build 10) → 1.10(11) → 1.11(12) → 1.12(13, broken fix) → 1.13(14, broken fix) → 1.14(15, real fix) → 1.15(16) → 1.16(17) → **1.17(18) — current, installed on S23+, relay redeployed and confirmed serving new code.**
- **Latest commit:** `f9800f4` "Rozpis notifications: tell the user when a shift changes, sync in the background (1.17)", on `main`, pushed to BOTH remotes (`github` = `Klotho79/SecureApp`, `pi` = bare repo on the Raspberry Pi at `192.168.50.8`).
- **Working tree:** clean except `.claude/settings.local.json` (unrelated tooling config, not app code).
- **Relay:** confirmed redeployed at `2026-09-27T16:59:20Z` (container `secureapp-relay` restarted, "Up 10 seconds" at check time); new route `GET /admin/applog/{id}` correctly returns `401` (auth required) not `404` — proves the new relay code is genuinely live, not just pushed.
- **S23+ (device id `c0b7bfbd-7383-455c-b53c-aa0a0b4b7569`):** running 1.17, background service confirmed started and actively reconnecting/receiving messages (see Evidence & Data).
- **Three OTHER registered devices are stale / on old builds:** Petr Faltus's S25 (`c1b2f0ab-b6c9-4b20-a86b-94cac376b13c`, last active 2026-09-27T14:49, has 1 message stuck in the relay outbox, uploaded 0 error-log lines = pre-AppLogUploader build), PC (`9c7082b9-5b4d-4366-920c-e4d6c25feb93`, last active 2026-09-24T21:39 — not run in days), "Local User"/S9+ (`c77520c9-3cdd-4880-ba3d-6efe34c7c5b4`, last active 2026-09-23T11:32).
- `OpicentrumParsing.cs` (new file, `Workplace/`) — pure-HTML `pracoviste.php` parser, no I/O/no MAUI, directly console-testable. `ParsePracovisteWeek(html)`, `NamesMatch(a,b)` (diacritic/title/order-tolerant), `NormalizeName`, `StripTags`. Fixes the real Faltus bug: `onmousedown="datumupravovany=…;osoba=…"` attributes only render for roster-EDITOR accounts, not regular members — the old parser silently found nothing for non-editors.
- `OpicentrumSyncService.cs` (`Workplace/`) — `MergePracovisteAsync` now takes `myName` + `myId?` and matches by name OR id (was id-only, which blanked non-editor accounts entirely). Also now collects per-day duty-roster entries into `onDuty`/`shifted` dictionaries (`DutyEntry(Position,Name)`), feeding `DutyRosterStore.Save`. `WriteBackAsync` now batches every genuinely-CHANGED day (not newly-seen days) into one `PublishScheduleChangesAsync` notification call per sync.
- `CzechCalendar.cs` (new file) — 11 fixed statutory holidays (Dictionary keyed `(Month,Day)`) + Velký pátek/Velikonoční pondělí via Meeus/Jones/Butcher Easter algorithm; `DayKindLabel(date)` returns holiday name, "Víkend", or "". Verified with a 13-case console test (2025–2027), all passed.
- `WorkplaceViewModel.cs` — `AssignmentDayItem.DisplayTypeLabel` rewritten as a computed property combining `DayKindText` with the type label; `EmptyTodayText` property added for the Dnes card's empty state.
- `AppShell.xaml.cs` `RebuildTabBar()` — **THE tab-bar bug, fixed on the THIRD attempt (see "What We Tried")**: now diffs desired-vs-current tab set and only inserts/removes what actually changed, instead of unconditionally removing-then-readding every hideable tab on every call.
- `App.xaml.cs` — `ResolveNotificationRouteAsync` (new, async) replaces the old synchronous `NavigateToNotificationDetail` body: Schedule category → `WorkplacePage`; group notification → `GroupChatPage` (sets `AppShell.GroupChatPageFactory.PendingKey`); 1:1 chat notification → re-resolves the peer's CURRENT live session (by public-key hex, since a resync replaces session ids) before opening `ChatPage`; everything else still opens `NotificationDetailPage`. New `RunBackgroundOpicentrumSyncAsync` (every 3h, `Preferences["opicentrum_last_bg_sync_utc"]` throttle, range = today−7d .. end-of-next-month) called from the connection-supervisor loop's 3-min sweep branch. New `Diagnostics.AppLogUploader.UploadAsync(services)` call added to the same sweep branch.
- `ActiveChatThread.cs` (new file, `Notifications/`) — static, lock-protected tracker of which thread (1:1 by peer-public-key-hex, or group by id) is on screen right now AND whether the app is foregrounded (`OnAppStopped`/`OnAppResumed`, wired to `Window.Stopped`/`Window.Resumed` in `App.xaml.cs`). Opening a thread or resuming with one open calls `MarkThreadReadAsync` (new on `INotificationRepository`) and `INativeNotificationService.CancelNotification` (new method, both platform implementations updated). `App.xaml.cs`'s message-received handler now checks `ActiveChatThread.IsOpen(...)` before creating a notification at all.
- `ChatViewModel.cs` / `GroupChatViewModel.cs` — `StartListening`/`StopListening` now also call `ActiveChatThread.EnterDirect`/`EnterGroup`/`LeaveDirect`/`LeaveGroup`.
- `ChatAppearance.cs` (new file, `Infrastructure/`) — `ApplyFontSize(index)` sets the `ChatMessageFontSize` resource; `ApplyBubbleColor(hex?)` sets `ChatOwnBubbleColor`/`ChatOwnBubbleTextColor`/`ChatOwnBubbleMetaColor`, following the app accent (light/dark aware) when no custom hex is set, else computing contrasting text colour via relative luminance (`0.2126R+0.7152G+0.0722B > 0.55` → black text, else white). `Refresh()` re-derives on theme change (hooked to `Application.RequestedThemeChanged`).
- `FontScaling.cs` (new file, `Infrastructure/`) — Android-only: `Register()` (called once from `MauiProgram.CreateMauiApp`, BEFORE `MauiApp.CreateBuilder()`) appends a mapper to `LabelHandler`/`ButtonHandler`/`EntryHandler`/`EditorHandler`/`PickerHandler`/`DatePickerHandler`/`TimePickerHandler` that multiplies the native `TextView`'s size by `Factor` right after MAUI applies its own font — this is what makes literal (non-`AppFontSize`-bound) `FontSize` values on individual controls actually scale, which the OLD font-size setting never did (it only overwrote the `AppFontSize` resource, which almost nothing in the app actually references).
- `ContactDirectoryData.cs` / new `PhoneNumberFormat.cs` (`Contacts/`) — `PhoneNumberPart(PhoneKind, Display, Dial)`, `PhoneKind{Extension,Mobile,Landline}`. `ParsePart`: ≤5 digits or a hyphenated range → Extension; 9-digit Czech (optional `+420`/`00420` prefix) starting `6`/`7` → Mobile; other 9-digit or already-international → Landline. `Describe(raw)` → "kl. 2280" / "mob. 777 123 456" / "tel. 577 552 280"; `FirstDialable(raw)` → `+420777123456` or null.
- `ContactsViewModel.cs` — new `CallAsync(raw)` command opens `tel:` via `Launcher.Default.OpenAsync` (never auto-dials); `ToItem` now uses `PhoneNumberFormat.Describe`.
- `NotificationsWidgetProvider.cs` (`Platforms/Android/`) — new `ApplyMode` (replaces the earlier always-on `ApplyDutyToday`): `OnReceive` override handles a new `ActionToggleMode` broadcast (mode flag in `Preferences["widget_show_duty"]`), flips between the 5-day rozpis rows and a full `widget_duty_list` TextView ("Position: Name" per line). Toggle button label names the OTHER view ("Služby ›" / "Rozpis ›"). Message-ticker tap still opens the app (unchanged); the toggle never does.
- `DutyRosterStore.cs` (new file, `Workplace/`) — `DutyEntry(Position,Name)` records, saved as JSON in `Preferences["opicentrum_duty_roster"]`, 7-day retention window.
- `NotificationCategory.cs` — new `Schedule` value **appended LAST** (critical: category is persisted as a raw `int`, so inserting it anywhere else would silently reclassify every already-stored notification of a higher-numbered category).
- `NotificationPublisher.cs` — new `PublishScheduleChangeAsync(title, body, isImportant, native, ...)`.
- `RelayConnectionService.cs` (new file, `Platforms/Android/`) — foreground service, `ForegroundServiceType = TypeRemoteMessaging` (chosen specifically because, unlike `TypeDataSync`, it has no daily execution-time cap on Android 14+), min-priority ongoing "SecureApp je připojen" notification (channel `secureapp.connection`), started from `MainActivity.OnCreate`. `RelayConnectionBootReceiver` restarts it after `BOOT_COMPLETED`/`MY_PACKAGE_REPLACED`.
- `BackgroundRun.cs` (new file, `Infrastructure/`) — `IsAllowed()` via `PowerManager.IsIgnoringBatteryOptimizations`; `OpenSettings()` fires `ActionRequestIgnoreBatteryOptimizations` (falls back to app-details settings on failure); `PromptIfNeededAsync(page)` shows an explanation dialog on launch, throttled to once per 3 days via `Preferences["bg_run_last_prompt_utc"]`.
- `SettingsViewModel.Community.cs` — `BackgroundRunStatusText`/`IsBackgroundRunAllowed`/`RefreshBackgroundRunStatus()`/`OpenBackgroundRunSettingsAsync` command; `ChatFontSizeIndex`/`ChatBubblePreviewColor`/`SelectChatBubbleColor` command.
- `AppLogUploader.cs` (new file, `Diagnostics/`) — ships new COMPLETE lines of `errors.log`/`metrics.log` on every 3-min supervisor sweep. Tracks a byte offset per kind in `Preferences["applog_uploaded_bytes_{kind}"]`, splits on the last `\n` (keeps a partial tail for next run), follows `AppLog`'s own rotation into the `.1` backup file, first-ever run backfills only the last 64KB (not the whole history).
- `relay/SecureApp.Relay/RelayDatabase.cs` — new `device_app_logs` table (`device_id, kind, line, received_at_utc`), capped at 5000 lines per device+kind (`DELETE ... WHERE id NOT IN (SELECT ... ORDER BY id DESC LIMIT @max)` on every append). `AppendAppLogLines`/`GetAppLogLines`.
- `relay/SecureApp.Relay/Program.cs` — `POST /diagnostics/applog` (device-authed), `GET /admin/applog/{id}?kind=&limit=` (admin-secret-authed).
- `SettingsViewModel.cs` — per-device `ShowDeviceErrorsAsync`/`ShowDeviceEventsAsync`/`ShowDeviceLogAsync`/`CloseDeviceLog`, session-scoped `_devicesAdminSecret` field (cleared in `ClearMemberManagement`, called from `SettingsPage.OnDisappearing`). The previously-shared "Diagnostický log" card in `SettingsPage.xaml` is now `IsVisible="{Binding IsAdmin}"` only.
- `keystore/PRISTUPY.md` (new, git-ignored — verified via `git check-ignore -v`) — full credential/access-path inventory, Czech, no secret values.
- **Global (outside this repo):** `C:\Users\dvora\.claude\skills\handoff\` and `\handoffplan\` installed from `REMvisual/claude-handoff` (this very skill). Available in every Claude Code session/project now, not just SecureApp.

## What We Tried (Chronological)

1. **Faltus's missing workplaces (root cause hunt).** Hypothesis: the parser relied on `onmousedown="datumupravovany=…;osoba=…"` attributes on `pracoviste.php` table cells. Confirmed via a real logged-in HTML dump (captured through Chrome DevTools Protocol tunnelled over `adb forward`, using the S23+'s own cached browser tab — the fresh page load showed a stale login screen, 2187 bytes, so an OLDER cached tab with a live session, 75988 bytes, 217 `onmousedown` cells, was used instead) that those attributes are present ONLY for accounts with roster-EDIT rights; a regular member's HTML is byte-identical except those attributes are simply absent. **Fix:** new `OpicentrumParsing.cs`, name-based matching as the primary path, id-based as a bonus-only enhancement. Verified: tested 5 real people against old vs new parser across multiple real HTML samples — old parser found 0 for non-editor accounts, new parser found all 5 correctly in every scenario.

2. **"Bez záznamu" too aggressive.** User: *"bez zaznamu nechej pouze pokud jeste neby udelany zaznam pokus se jedna o vikend napis vikend a dopis ev. Sluzbu a dopln svatky s nazvem pokud je den svatek"*. New `CzechCalendar.cs`, wired into `WorkplaceViewModel.AssignmentDayItem.DisplayTypeLabel` and `NotificationsWidgetProvider.ApplyDay`. Verified: 13-case Easter/holiday console test, all passed; Android build 0 errors.

3. **THE TAB-BAR BUG — three attempts across three releases, this was the single most expensive bug this session:**
   - **Symptom (user, verbatim):** *"Apka se sice otevre na nastence ale hned se prepne na nastaveni"* — app opens on Nástěnka (home tab) but immediately jumps to Nastavení (Settings, the last tab).
   - **Attempt 1 (commit `3b8e476`, shipped as 1.12):** Hypothesis — `RebuildTabBar` momentarily removes ALL hideable tabs (leaving only the always-present Settings tab), and MAUI Shell auto-switches to whatever remains. Fix: save `Shell.CurrentItem` before the rebuild, restore after. **Failed to even compile at first** — `CS1503`, `Shell.CurrentItem` is `ShellItem`, `NotificationsTab` is `Tab`. Patched with `(object)previousItem is Tab prevTab` — compiled, but **never actually restored anything at runtime**, because `Shell.CurrentItem` in a TabBar-only Shell returns the TabBar itself (the one and only `ShellItem`), never an individual `Tab`.
   - **Attempt 2 (commit `bdf9db1`, shipped as 1.13):** Switched to `tabBar.CurrentItem` (type `ShellSection`; `Tab : ShellSection`, so the pattern match is real this time). User reported it STILL broken, in a NEW way — *"porad se po chvili prepina do nastaveni a pak nefunguje v menu nastenka... asi protoze si mysli ze je na nastence"* (still jumps after a while, AND tapping Nástěnka does nothing — it thinks it's on Nástěnka but shows Nastavení). Root cause of THIS failure: forcibly re-setting `CurrentItem` after a remove-then-readd cycle desyncs Android's native bottom-nav view state from Shell's internal state even further, rather than fixing anything — the restore made the underlying problem WORSE, not better.
   - **Attempt 3 (commit `7c7178a`, shipped as 1.14 — THE ACTUAL FIX):** Stopped doing remove-all/re-add entirely. `RebuildTabBar` now computes `desired` (tabs whose preference is currently true) and diffs it against `current` (tabs actually in `tabBar.Items` from the tracked set); `desired.SequenceEqual(current)` → early return, no-op; otherwise only the tabs that actually changed are inserted/removed. Root problem: `ApplyDevicePolicyAsync` (runs on every relay reconnect + every 3-min sweep) called `ApplyTabVisibility` per tab in a loop, each call triggering a FULL rebuild — even when literally nothing had changed. Verified via compile + user confirmation flow initiated (see "Where We're Going" for the still-open verification loop that followed).
   - **Lesson for next session:** when a MAUI Shell bug involves `TabBar`/`Tab`, remember `Shell.CurrentItem` is `ShellItem` (the TabBar), `TabBar.CurrentItem` is `ShellSection` (the actual selected `Tab`) — these are NOT interchangeable, and neither compiling nor a plausible-sounding fix guarantees runtime correctness without live device verification.

4. **Chat notifications not clearing on open.** User: wants opening a chat to clear its own notifications. New `ActiveChatThread` static tracker; wired into both ViewModels' existing `StartListening`/`StopListening` (already called from every host page/overlay, so one wiring point covered `ChatPage`, `GroupChatPage`, and `ChatListPage`'s split/narrow-overlay panes). Matches 1:1 threads by peer public-key hex (not session id — a resync REPLACES the session id, so id-matching would silently stop working after every resync). Compile-checked only; not yet device-tested.

5. **Notification tap should open the chat, not a detail page.** User: *"Kdyz kliknu na zpravu na wigetu chci aby se otevrel rovnou chat a ne oznameni"*. `ResolveNotificationRouteAsync` added; for a 1:1 notification, re-resolves the CURRENT live session for that peer (the stored session id in the notification may point at a now-Closed session after a resync) by looking up all the peer's sessions by public-key hex and preferring non-Closed / most-recently-ratcheted. Group and Schedule categories route directly. Compile-checked only.

6. **App-wide font size did nothing.** Diagnosis: `AppFontSize` resource existed and `ApplyFontScale` correctly overwrote it, but almost every page hardcodes a literal `FontSize="11"` etc. rather than binding to the resource — so the setting visibly changed almost nothing. **Rejected approach:** going through every XAML file and converting every literal FontSize to `{DynamicResource AppFontSize}` — too large/risky a diff for this pass. **Chosen approach:** Android handler-mapper interception (`FontScaling.cs`) that rescales the ALREADY-RESOLVED native text size after MAUI sets it, regardless of whether the XAML used a literal or a resource. Windows/other platforms keep the old resource-only behaviour (acceptable since Windows is the dev/test target, not a real deployment target per `NativeNotificationService`'s own remarks). Compile-checked; **risk flagged, not yet device-verified**: the "Největší" (130%) size could overflow fixed-height areas like the month calendar grid on Rozpis.

7. **Chat-specific appearance requested alongside font size.** User combined ask (via `AskUserQuestion`, "Obojí" = both): global font-size fix AND chat-specific font-size + bubble colour. New Settings > Chat card. Bubble colour reuses the exact same accent-preset swatches as the app-colour picker, plus a "Podle barvy aplikace" (follow app colour) reset button.

8. **Widget duty roster — two iterations, driven by a live user correction.** First iteration (commit `4447e63`): an always-visible "Slouží: … · Posunutá: …" text line above the widget's 5-day rozpis, sourced from `sluzby7.php`'s 7 duty-slot columns + the `pracoviste.php` "Odpolední 13-21 h" row (identified as the "posunutá služba" — confirmed via a real HTML dump showing the LAST workplace row on that page is literally titled "Odpolední 13-21 h"). User's live check via `AskUserQuestion` confirmed the 7th sluzby7.php slot ("Anest2 + PACU") is a SEPARATE duty from the posunutá row, not the same thing folded together — so both are surfaced independently. Second iteration (commit `fa83901`, user: *"Dobre muze tam byt tedy jen sluzby a kdyz na ne kliknu objevi se komplet sluzby ten den vcetne pozic a objevi se zase rozpis..."*): redesigned into a Rozpis/Služby toggle — tapping "Služby ›" swaps the widget card's content to a full position+name duty list and changes the label to "Rozpis ›"; the toggle is a pure Android broadcast back to the widget provider (`ActionToggleMode`), it NEVER launches the app — explicitly distinct from the message-ticker tap, which still opens the app. Verified via `uiautomator dump` reading real live widget text.

9. **Contacts: extensions vs. complete numbers.** User: wants "kl." reserved for genuine internal extensions, complete numbers labelled mob./tel. and tappable-to-call. New `PhoneNumberFormat.cs`. **Verified with a 12-case scratch console test (all passed)** — see Evidence & Data for the full table. Follow-up correction from the user (*"El ústredns me treba zakazuje volani na sluzebni tel pres klapku... jsou to mobily jen v podniku funguji klapky"*) clarified that hospital extensions genuinely cannot be dialled from outside the PBX — so NO feature to make extensions dialable (e.g. via a prefix) was added; this was a deliberate scope boundary, not an oversight.

10. **Background messaging — the session's biggest single feature, later validated by real production data.** User's core ask (plan item 1, later restated): *"Apka sama zavede k nastaveni a poda vysvetleni"* (the app itself should lead the user to the setting and explain why). Built `RelayConnectionService` + `BackgroundRun.cs` + Settings status card, all described in "Where We Are". **This is the fix directly validated by the log-analysis evidence below — the single most concrete result of this entire session.**

11. **Per-device admin-only logs — design changed mid-turn by direct user correction.** Initial ask (plan item 4): *"Ostatni uzivatele jsou troubove anic neposlou logy uzivatelu budou dostupne adminu"* (other users are hopeless and won't send logs — logs should just be available to the admin automatically). Built the relay-side upload/storage first with a plan for ONE shared "Diagnostický log" viewer scoped by device via a dropdown/filter. **Mid-turn, before that viewer was built, the user corrected the design**: *"Potrebuji log oddelit u kazdeho zvlast s tim ze si vyberu od kohp loh chci otevrit ne ze budu hledat v 1 spolecnem?"* (I need each device's log SEPARATE, so I pick whose log to open, not search through one merged log). Built instead: per-device-row buttons directly on the existing "Admin: Zařízení" card, each opening only that one device's log in its own panel. This is a good example of catching and applying a live design correction rather than shipping the originally-stated (but not actually final) design.

12. **Schedule-change notifications + background sync (plan item 2).** User confirmed via `AskUserQuestion` ("Vysvetli" then implicit follow-through) that "oznámení z aplikace" should mean the app notifies when a REAL change to the user's own schedule is detected — asked in the SAME turn whether notifications work while the app is closed, which is what led directly into feature 10 (background messaging) being scoped and built FIRST, since schedule-change notifications while closed would be pointless without it. Implementation: new `NotificationCategory.Schedule`, batched per-sync notification (not one notification per changed day — would flood), `RunBackgroundOpicentrumSyncAsync` every 3h so changes surface without ever opening the Rozpis page.

13. **Real-log validation pass (unplanned, triggered by user asking "co jsme meli v planu dale?").** Rather than starting a large new Phase-2b/3 audit blind, pulled the ACTUAL error/metrics logs for the two live devices off the just-built admin endpoint (via SSH+curl against the Pi directly, extracting the admin secret from `.env` server-side — never echoed into chat). This surfaced the 15-hour outage evidence (see Evidence & Data) — a much higher-value use of the remaining session time than a blind code-review pass, and directly validated feature 10 rather than just assuming it worked.

14. **Deploy-verification gap caught twice.** Both times the user reported an action as done ("Je to poslane na relay...", later "Ok nasazeno") when independent verification (HTTP probes, `docker ps`, `journalctl`) showed it had NOT actually happened yet. See "Key Decisions" and "Risks & Blockers" — this is now a standing practice for future sessions on this project, not a one-off.

## Key Decisions

- **Name-matching over id-matching as the PRIMARY Opicentrum person-resolution path**, id kept only as a bonus. Rejected alternative: asking every user to somehow get roster-edit rights (not the user's to grant, and defeats "works the same for everyone" which the user explicitly asked for: *"neni duvod to vytvaret pro kazdeho znovu, udelej univ. Funkci"*).
- **Diff-based `RebuildTabBar` over restore-selection-after-rebuild.** The first two attempts treated the symptom (wrong tab shown); the third treated the actual cause (the rebuild itself, done unconditionally on every policy-sync tick, was the bug). Rejected: patching `ApplyDevicePolicyAsync` to skip calling `ApplyTabVisibility` when nothing changed — rejected because the real fix belongs in `RebuildTabBar` itself (any future caller gets the same protection for free) rather than pushing the "did anything change" burden onto every call site.
- **1:1 chat-thread/notification matching by peer public-key hex, never by session id**, in BOTH `ActiveChatThread` and `ResolveNotificationRouteAsync`. A resync (session recovery after a ratchet desync or re-pairing) always creates a NEW session id for the same peer — matching by id would silently break after the very first resync of that peer.
- **Android-native text-size interception (`FontScaling.cs`) over a full XAML literal-to-resource conversion pass.** Chosen specifically to avoid a large, risky diff across every view for a UX-polish feature; accepted the platform asymmetry (Android gets real scaling, Windows keeps the old weaker behaviour) as a reasonable trade-off given Windows isn't a real deployment target.
- **`NotificationCategory.Schedule` appended LAST in the enum, not inserted in a logical position.** Category is persisted as a raw int in SQLite; inserting anywhere but last would silently reclassify every already-stored notification whose category happens to have a higher underlying int value. Same reasoning applied to NOT renumbering anything else in that enum.
- **Widget's Rozpis/Služby toggle is a broadcast to the SAME widget provider, never an app launch**, per the user's explicit distinction between "message tap → open app" and "mode toggle → only change widget content."
- **Extensions are permanently non-dialable, by explicit user instruction**, not a gap to fill later — the hospital PBX itself blocks external calls to internal-only extension numbers.
- **Per-device logs, not a filterable shared log** — direct user correction mid-turn, see "What We Tried" #11.
- **Admin secret handling discipline maintained throughout**: every SSH/curl-based relay query in this session extracted the admin secret SERVER-SIDE (`grep ... .env | cut ...` inside the SSH command) and it was never echoed back into the chat transcript or used as a literal string in any tool call — same discipline `secureapp-infra-paths.md` memory already establishes.
- **Treat "hotovo"/"nasazeno" user claims as needing independent verification when cheap to check** (HTTP probe, `docker ps`, log inspection) — this bit twice in one session (see "Risks & Blockers").

## Evidence & Data

**Real-world log analysis (2026-09-28, queried directly off the newly-built relay admin-log endpoint — this is production evidence, not a code-review guess):**

S23+ device `c0b7bfbd-7383-455c-b53c-aa0a0b4b7569` — error log, 112 total lines, aggregated:

| Context | Message | Count |
|---|---|---|
| `App.TryConnect` | relay reconnect attempt failed (`WebSocketException: net_webstatus_ConnectFailure`) | 112 |

Metrics/events log, 270 total lines, top event counts:

| Event | Count |
|---|---|
| `relay.reconnected` | 140 |
| `chat.open.load` | 29 |
| `msg.received` | 29 |
| `page.reuse.miss` | 17 |
| `chat.page.ctor` | 17 |
| `msg.ack-sent` | 12 |
| `msg.sent` | 5 |
| `sessions.pruned` | 3 |
| `bg-service.started` | 3 |
| `session.resync.history-migrated` | 2 |
| `session.resync.invite-sent` | 2 |
| `admin.device.deregistered` | 2 |
| `pairing.accepted` | 2 |
| `opicentrum.bg-sync` | 1 |

**The outage window** — every event between 2026-09-27 02:00 and 19:00:

```
27.09.2026 2:24:42  relay.reconnected
27.09.2026 2:54:43  relay.reconnected
   [GAP — 15h41m — 26 consecutive FAILED reconnect attempts logged in errors.log during
    this window (03:24:47, 03:54:47, 04:48:11, 05:10:16, 05:54:47, 06:02:20, 07:04:48,
    07:35:14, 08:04:56, 08:33:35, 09:09:14, 09:42:51, 10:37:19, 11:03:37, 12:02:38,
    12:34:39, 13:02:23, 13:48:52, 14:08:51, 15:22:59, 15:47:01, 16:06:02, 17:05:32,
    17:42:02, 18:24:47, 18:32:32) — ZERO successful relay.reconnected in this entire span]
27.09.2026 18:35:18 bg-service.started        <- RelayConnectionService's FIRST EVER start (1.17 just installed)
27.09.2026 18:35:19 msg.received
27.09.2026 18:35:19 msg.received
27.09.2026 18:35:19 relay.reconnected
27.09.2026 18:35:19 msg.received              <- 3 queued messages delivered in the SAME SECOND connection succeeded
27.09.2026 18:35:28 opicentrum.bg-sync
27.09.2026 18:40:27 relay.reconnected
27.09.2026 18:45:43 relay.reconnected          <- every ~5min after this, matching the forced-reconnect design
27.09.2026 18:48:28 bg-service.started
27.09.2026 18:50:46 relay.reconnected
27.09.2026 18:52:39 chat.page.ctor
27.09.2026 18:52:39 page.reuse.miss
27.09.2026 18:52:40 chat.open.load
27.09.2026 18:55:48 relay.reconnected
27.09.2026 18:59:22 relay.reconnected
```

**Registered devices** (`GET /admin/devices`, 2026-09-28):

| Device | Id | Last active | Pending outbox |
|---|---|---|---|
| S23+ (Vilém) | `c0b7bfbd-7383-455c-b53c-aa0a0b4b7569` | 2026-09-27T17:42:46 | 0 |
| S25 (Petr Faltus) | `c1b2f0ab-b6c9-4b20-a86b-94cac376b13c` | 2026-09-27T14:49:08 | 1 |
| PC | `9c7082b9-5b4d-4366-920c-e4d6c25feb93` | 2026-09-24T21:39:16 | 0 |
| "Local User" / S9+ | `c77520c9-3cdd-4880-ba3d-6efe34c7c5b4` | 2026-09-23T11:32:13 | 0 |

Petr's stuck message (SQLite `outbox` table, columns `id, recipient_device_id, created_at_utc, length(frame_json)`):
```
3d79ac7c-6e35-48c7-be3c-b15178cae8bf|c1b2f0ab-b6c9-4b20-a86b-94cac376b13c|2026-09-27T16:52:40.5701771+00:00|2422
```
Interpretation: normal store-and-forward behaviour, not a bug — Petr's device uploaded 0 error-log lines (confirms pre-AppLogUploader build), so he's presumably hitting the SAME pre-1.17 background-connectivity problem S23+ just had.

**Silent-catch audit** (`grep 'catch\s*\{\s*(//[^\n]*)?\s*\}'` across `src/`, recursive):

17 files matched: `App.xaml.cs`, `NotificationPublisher.cs`, `NotificationsWidgetProvider.cs`, `DutyRosterStore.cs`, `ChatAppearance.cs`, `ChatViewModel.cs`, `GroupChatViewModel.cs`, `NativeNotificationService.cs` (Android), `AddAssignmentViewModel.cs`, `NewGroupViewModel.cs`, `WebSocketMessageTransport.cs`, `ChatListViewModel.cs`, `HttpSharedLibraryService.cs`, `NewChatViewModel.cs`, `DocumentViewerViewModel.cs`, `HttpDiagnosticsReporter.cs`, `LogbookViewModel.cs`.

Spot-checked (not all 17): `ChatListViewModel.cs`, `NewGroupViewModel.cs`, `LogbookViewModel.cs`, `AddAssignmentViewModel.cs` — every catch either has an explanatory "Best-effort" comment or surfaces the error via a `*ErrorMessage` property bound in the UI. No smoking gun found. **Full audit of the remaining files explicitly deferred by the user** (see Open Questions).

**Phone-number classifier test (12/12 passed, scratch console project at `%TEMP%\...\PhoneTest\`):**

| Input | Expected output | Expected dial |
|---|---|---|
| `2280` | `kl. 2280` | (none) |
| `2370, 2287` | `kl. 2370 · kl. 2287` | (none) |
| `6604-6605` | `kl. 6604-6605` | (none) |
| `6629-6631, 6684` | `kl. 6629-6631 · kl. 6684` | (none) |
| `57755` | `kl. 57755` | (none) |
| `777123456` | `mob. 777 123 456` | `+420777123456` |
| `+420 777 123 456` | `mob. 777 123 456` | `+420777123456` |
| `00420 603 111 222` | `mob. 603 111 222` | `+420603111222` |
| `577 552 280` | `tel. 577 552 280` | `+420577552280` |
| `2280, 777123456` | `kl. 2280 · mob. 777 123 456` | `+420777123456` |
| `+49 30 1234567` | `tel. +49 30 1234567` | `+49301234567` |
| `` (empty) | `` (empty) | (none) |

**Deploy pipeline verification (before/after the user's second "Znovu nasadit relay" tap):**

| Check | Before (user said "nasazeno") | After (re-tapped) |
|---|---|---|
| `docker ps` container uptime | "Up 2 days" (unchanged) | "Up 10 seconds" |
| `GET /admin/applog/{id}` | `404` (route doesn't exist — old code) | `401` (route exists, needs auth — new code) |
| `journalctl -u secureapp-deploy.service` | no entries in prior 2h | fresh build log, completed `2026-09-27T16:59:20Z` |

**Commit log, this session (chronological, all on `main`, pushed to both `github` and `pi` remotes):**

| Commit | Summary |
|---|---|
| `b224d1a` | Opicentrum: read workplaces by name, not editor attributes |
| `20748e8` | Release 1.10 (build 11) |
| `8a0325e` | Rozpis weekend/holiday labels |
| `b8dd011` | Release 1.11 (build 12) |
| `3b8e476` | Fix RebuildTabBar (attempt 1, broken) |
| `bdf9db1` | Fix RebuildTabBar tab-restore: tabBar.CurrentItem (attempt 2, still broken) |
| `7c7178a` | Fix tab bar jumping: diff tabs instead of remove-all/re-add (1.14, THE fix) |
| `c194539` | Chat notifications: clear once thread opened (1.15) |
| `c9c416d` | Notification tap opens the chat itself (part of 1.16) |
| `9622894` | Settings: app-wide font size + chat font/bubble colour (part of 1.16) |
| `4447e63` | Widget: show who is on duty today (part of 1.16) |
| `fa83901` | Widget: Rozpis/Služby switch (part of 1.16) |
| `1bf5b06` | Contacts: extensions vs complete numbers, callable (part of 1.16) |
| `5571112` | Background messaging + per-device app logs for admin (part of 1.17) |
| `f9800f4` | Rozpis notifications: schedule change + background sync (1.17) |

## Code Analysis

- `App.RunConnectionSupervisorLoopAsync` cadence constants: `_supervisorTickInterval = 10s`, `_staleSessionSweepInterval = 3min`, `_forcedReconnectInterval = 5min` (existing), new `_backgroundOpicentrumSyncInterval = 3h`.
- `NotificationCategory` enum order (int-persisted, DO NOT reorder): `Chat=0, Group=1, Library=2, Logbook=3, System=4, Other=5, Schedule=6`.
- `AppLogUploader` constants: `FirstRunTailBytes = 64KB`, `MaxLinesPerRequest = 1500`, `MaxRequestsPerRun = 5`.
- `RelayDatabase.AppLogMaxLinesPerDeviceKind = 5000` (per device, per kind — errors/metrics tracked separately).
- `PhoneNumberFormat.ParsePart` decision boundary: extension if (`Contains('-')` AND digit-count ≤10) OR digit-count ≤5; else 9-digit-starting-6-or-7 (post `+420`/`00420` strip) = Mobile; else Landline.
- `TrustedAdminDevices` hardcoded allowlist (pre-existing, referenced in `keystore/PRISTUPY.md`): Android model `"SM-S916B"` (S23+), Windows machine name `"DESKTOP-F8AS2U6"` (this dev PC) — always bootstrap as Admin role; every other device gets the safe Modifier floor.
- Relay deploy pipeline (pre-existing, re-verified this session): `git push pi main` → post-receive hook `GIT_WORK_TREE=/home/dvorakv1/SecureApp git --git-dir=/home/dvorakv1/secureapp-repo.git checkout -f main` (checks out files only, does NOT rebuild) → `POST /admin/deploy` (Settings > Admin > "Znovu nasadit relay" button) drops a marker file → systemd path unit `secureapp-deploy.path` (watches `/home/dvorakv1/SecureApp/relay/SecureApp.Relay/data/deploy-requested`) triggers `secureapp-deploy.service` → runs `relay/ops/deploy.sh` → `docker compose build && up -d`.

## Files Changed

### Opicentrum sync / schedule
- `src/SecureApp.Presentation/Workplace/OpicentrumParsing.cs` — new
- `src/SecureApp.Presentation/Workplace/OpicentrumSyncService.cs` — MergePracovisteAsync rewritten, duty-roster collection added, WriteBackAsync batches changes into one notification
- `src/SecureApp.Presentation/Workplace/CzechCalendar.cs` — new
- `src/SecureApp.Presentation/Workplace/DutyRosterStore.cs` — new
- `src/SecureApp.Presentation/ViewModels/WorkplaceViewModel.cs` — DisplayTypeLabel/EmptyTodayText

### Tab bar bug (3 attempts)
- `src/SecureApp.Presentation/AppShell.xaml.cs` — RebuildTabBar, three revisions

### Notifications
- `src/SecureApp.Presentation/App.xaml.cs` — ResolveNotificationRouteAsync, RunBackgroundOpicentrumSyncAsync, ActiveChatThread hooks, AppLogUploader call
- `src/SecureApp.Presentation/Notifications/ActiveChatThread.cs` — new
- `src/SecureApp.Presentation/Notifications/NotificationPublisher.cs` — PublishScheduleChangeAsync
- `src/SecureApp.Domain/Enums/NotificationCategory.cs` — Schedule appended
- `src/SecureApp.Domain/Interfaces/Repositories/INotificationRepository.cs` — MarkThreadReadAsync
- `src/SecureApp.Domain/Interfaces/Services/INativeNotificationService.cs` — CancelNotification
- `src/SecureApp.Presentation/Platforms/Android/NativeNotificationService.cs` / `Platforms/Windows/NativeNotificationService.cs` — CancelNotification impl
- `src/SecureApp.Presentation/ViewModels/ChatViewModel.cs`, `GroupChatViewModel.cs` — ActiveChatThread wiring
- `src/SecureApp.Presentation/ViewModels/NotificationsViewModel.cs`, `NotificationDetailViewModel.cs` — Schedule category display/routing

### Appearance / customization
- `src/SecureApp.Presentation/Infrastructure/ChatAppearance.cs` — new
- `src/SecureApp.Presentation/Infrastructure/FontScaling.cs` — new
- `src/SecureApp.Presentation/Infrastructure/AccentPalette.cs` — TryParseHex made public
- `src/SecureApp.Presentation/Resources/Styles/Styles.xaml` — ChatMessageFontSize/ChatOwnBubble* resources
- `src/SecureApp.Presentation/Views/ChatThreadView.xaml`, `GroupChatThreadView.xaml` — DynamicResource bindings
- `src/SecureApp.Presentation/MauiProgram.cs` — FontScaling.Register() call
- `src/SecureApp.Presentation/App.xaml.cs` — ApplyFontScale platform branch
- `src/SecureApp.Presentation/ViewModels/SettingsViewModel.Community.cs` — chat appearance + background-run properties/commands
- `src/SecureApp.Presentation/Views/SettingsPage.xaml` — Chat card, Zprávy na pozadí card

### Widget
- `src/SecureApp.Presentation/Platforms/Android/NotificationsWidgetProvider.cs` — ApplyMode, OnReceive/ActionToggleMode
- `src/SecureApp.Presentation/Platforms/Android/Resources/layout/widget_notifications.xml` — widget_mode_toggle, widget_duty_list

### Contacts
- `src/SecureApp.Presentation/Contacts/PhoneNumberFormat.cs` — new
- `src/SecureApp.Presentation/Contacts/ContactDirectoryData.cs` — NumberLabel
- `src/SecureApp.Presentation/ViewModels/ContactsViewModel.cs` — CallAsync
- `src/SecureApp.Presentation/ViewModels/SmartSearchViewModel.cs` — Describe() in search results
- `src/SecureApp.Presentation/Views/ContactsPage.xaml`, `AddContactPage.xaml` — call button, placeholder text

### Background messaging
- `src/SecureApp.Presentation/Platforms/Android/RelayConnectionService.cs` — new (+ RelayConnectionBootReceiver)
- `src/SecureApp.Presentation/Infrastructure/BackgroundRun.cs` — new
- `src/SecureApp.Presentation/Platforms/Android/AndroidManifest.xml` — 3 new permissions
- `src/SecureApp.Presentation/Platforms/Android/MainActivity.cs` — RelayConnectionService.Start call

### Per-device admin logs
- `src/SecureApp.Presentation/Diagnostics/AppLogUploader.cs` — new
- `relay/SecureApp.Relay/RelayDatabase.cs` — device_app_logs table, AppendAppLogLines/GetAppLogLines
- `relay/SecureApp.Relay/Contracts.cs` — AppLogUploadRequest
- `relay/SecureApp.Relay/Program.cs` — POST /diagnostics/applog, GET /admin/applog/{id}
- `src/SecureApp.Domain/Interfaces/Services/IRelayAdminService.cs`, `src/SecureApp.Presentation/Transport/HttpRelayAdminService.cs` — GetDeviceAppLogAsync
- `src/SecureApp.Presentation/ViewModels/SettingsViewModel.cs` — per-device log commands/state

### Docs / config
- `keystore/PRISTUPY.md` — new, git-ignored, credential/access inventory
- `src/SecureApp.Presentation/SecureApp.Presentation.csproj` — version bumps 1.10(11)→1.17(18)

### Global (outside repo)
- `C:\Users\dvora\.claude\skills\handoff\`, `\handoffplan\` — installed from REMvisual/claude-handoff

## User Feedback & Preferences (REQUIRED — never omit)

- *"pokracujem"* — bare session-opening "let's continue," no further context given; had to re-derive state from prior compacted summary + repo state.
- *"uzivateli Faltus se nezobrazuji pracovni pozice... neni duvod to vytvaret pro kazdeho znovu, udelej univ. Funkci pro dungujici pro vsechny uzivatele"* — explicit demand for ONE universal function, not per-user special-casing.
- *"bez zaznamu nechej pouze pokud jeste neby udelany zaznam pokus se jedna o vikend napis vikend a dopis ev. Sluzbu a dopln svatky s nazvem pokud je den svatek"* — verbatim spec for the weekend/holiday labeling.
- *"Apka se sice otevre na nastence ale hned se prepne na nastaveni"* — the tab-bar bug's original report.
- *"porad se po chvili prepina do nastaveni a pak nefunguje v menu nastenka... asi protoze si mysli ze je na nastence"* — the follow-up report that correctly diagnosed the desync ("thinks it's on Nástěnka but shows Nastavení") and directly led to finding the real root cause.
- *"Vubec se to nespravilo, proste prover prikazy protoze..."* — blunt pushback after attempt 2 failed; asked to actually VERIFY, not just claim a fix.
- *"Kdyz kliknu na zpravu na wigetu chci aby se otevrel rovnou chat a ne oznameni"* — notification-opens-chat spec.
- *"Udelej n nastaveni motnist velikosti pisma barvu a slo by vkladat emotikony?"* — original 3-part ask (font size, colour, emoji); emoji was declined (keyboard already provides it) via `AskUserQuestion`.
- Answering the font/colour scope question: *"Obojí"* — wanted BOTH the global font-size fix AND chat-specific settings, not one or the other.
- *"Ve wigetu nad rozpisem zobraz kdo dnes tedy v den zobrazeni slouzi je to 6 lidi a posunuta sluzba posledni radek v rozdeleni pracovist a kdo slouzi je v uvodni strance nahore..."* — original widget duty-roster spec.
- *"Ne, posunutá je zvlášť"* — direct correction on the 7-vs-8 duty-slot question via `AskUserQuestion`.
- *"Dobre muze tam byt tedy jen sluzby a kdyz na ne kliknu objevi se komplet sluzby ten den vcetne pozic a objevi se zase rozpis misto napisu sluzby... po kliknuti na rozpis se to zase zmeni... zpravy otevrou chat v apce ale ty napisi zmeni jen obsah wigetu..."* — the widget redesign into a toggle, explicitly separating message-tap-opens-app from toggle-only-changes-widget.
- *"btw nedelej hned buildy az tech zmen bude vice... zkratka ti reknu... nebo se zeptej sam..."* — **standing process instruction, already saved to persistent memory (`feedback-batch-builds.md`)**: batch changes, compile-check only, wait for explicit go-ahead or ask before building/installing/releasing.
- *"Nahrano a slo by z apky v kontaktech kompletni tel cisla... Rozlisit mobil a tel od kl. A kdyz jsou cisla kompletni te by z nich slo volat..."* — phone-number classification spec.
- *"El ústredns me treba zakazuje volani na sluzebni tel pres klapku... jsou to mobily jen v podniku funguji klapky..."* — extensions genuinely non-dialable externally; scope boundary, not a gap.
- *"co jsme meli v planu dale?"* — asked me to survey ALL planning docs and report what's actually still open, rather than assuming.
- *"2 oznameni z aplikace... ale funguji oznameni kdyz neni spustena allikace? 4. Vysvetli."* — the question that surfaced the background-messaging need in the first place.
- *"1. Apka sama zavede k nastaveni a poda vysvetleni... bod 4. Ostatni uzivatele jsou troubove anic neposlou logy uzivatelu budou dostupne adminu... jedna se jen o log apky tak to snad neni zasah do soukromi..."* — background-messaging UX spec + explicit rationale for admin-only logs ("it's just the app's own log, shouldn't be a privacy concern").
- *"Potrebuji log oddelit u kazdeho zvlast s tim ze si vyberu od kohp loh chci otevrit ne ze budu hledat v 1 spolecnem?"* — mid-turn design correction, changed from one shared filterable log to per-device separate logs.
- *"Nic v pozadi by nemelo bezet zkontroluj to..."* — prompted a RAM/process audit before retrying a memory-killed build.
- *"Ano"* — confirmed BOTH killing background processes AND retrying the build, in one word, in response to a combined offer.
- *"co jsme meli v planu dale?"* (second instance, next day) then, after the Phase 2b/3 scope question, *"zatím to nech byt..."* / *"zatím to nech být"* — **explicit deferral of both Phase 2b (functionality audit) and Phase 3 (visual redesign) — do not start either without being asked again.**
- *"Nainstaluj pro muj claude abych ho mohl piuzit i pro claude code? Handoff skill od REMvisual (GitHub) nebo Matt Pocock"* — asked to choose between two same-named-but-different skills; user picked REMvisual + global scope.
- **Attribution note (environmental, not user-typed, but affects every commit):** the git commit co-author line changed THREE times across this session via system reminders — "Claude Opus 5.5" → "Claude Sonnet 4.6" → "Claude Sonnet 5". Always use whatever the LATEST system reminder specifies for the NEXT commit, never a value cached from earlier in a long session.

## Where We're Going

1. **Get 1.17 (or later) onto Petr's S25, the PC, and the S9+** — all three are stale/on old builds; Petr's outbox-stuck message and the background-messaging fix specifically benefit him. No action needed on S23+ side; this is a "get the other devices to update" step, likely via the in-app self-update flow already built (see `secureapp-update-distribution-plan.md` memory).
2. **Device-verify the still-unverified 1.16/1.17 features**: chat-notification-clears-on-open (`ActiveChatThread`), notification-tap-opens-chat (`ResolveNotificationRouteAsync`), app-wide font-size scaling on Android (especially the "Největší" size against the month-calendar grid), chat bubble colour/contrast on a real device. All compile-checked only so far.
3. **Decide on Phase 2b (functionality audit) scope and timing** — explicitly deferred, not cancelled. When resumed, the user was offered "one area deep" vs "broad shallow pass" via `AskUserQuestion` but didn't pick either before deferring — ask again fresh rather than assuming a prior preference.
4. **Decide on Phase 3 (visual design) scope and timing** — same, explicitly deferred.
5. **Consider finishing the silent-catch audit** — 13 of 17 flagged files were never actually opened (only 4 were spot-checked and came back clean). Low urgency given the spot-check pattern, but not actually complete.
6. **Update `NOTIFICATION_HUB_SPEC.md`'s Status section** — it currently says the §26 Notification↔Workplace↔Calendar cross-link is "not started," which is now stale (NotificationCategory.Schedule closes exactly that gap). Nobody has gone back to update the spec file itself yet.
7. **Optional, offered but not yet answered:** installing the `precompact-handoff.sh` hook (automatic handoff generation right before context compaction) — offered to the user at the end of the skill-install turn, no answer yet.

## Risks & Blockers

- **User's stated completion of an action cannot be trusted at face value for anything with a server-side effect** — happened twice this session (relay deploy both times), each time requiring an independent HTTP/docker/journalctl probe to discover the action genuinely hadn't landed yet. Budget time for this verification step every time a deploy/upload/push is reported as "done" going forward.
- **PowerShell + SSH + bash quoting is fragile** for anything beyond a single simple command — inline variable interpolation (`$(...)`) across the PowerShell→SSH→bash boundary repeatedly hit BOM/encoding corruption this session. Working pattern (write to a UTF8-no-BOM temp file, `scp` it, `ssh ... "sed -i 's/\r$//' ...; bash ..."`) is reliable but verbose — worth turning into a small reusable helper if this recurs often.
- **This dev machine intermittently runs out of RAM mid-build** — the harness's own low-memory guard killed a release build twice this session (not real build failures). Game launchers (Epic, GOG Galaxy, Google Play Games Services) auto-start on Windows login and were identified as avoidable RAM consumers; `VBCSCompiler.exe`/`BackgroundDownload.exe` (VS's own compiler server / installer background download) are legitimate but killable if memory is tight.
- **Android font-scaling risk, not yet observed live**: the "Největší" (130%) scale factor could overflow fixed-size layout areas (flagged specifically: the Rozpis month-calendar grid) — needs a real-device check, not just a compile check.
- **Three of four community devices are on old app builds** and won't benefit from this session's fixes (background messaging, per-device logs, notification improvements) until updated.

## Open Questions

- Should the `precompact-handoff.sh` hook be installed too (auto-handoff before context compaction)? Offered, not yet answered.
- When the user is ready to resume Phase 2b: single deep area first, or broad shallow pass first? Not yet decided (offered once via `AskUserQuestion`, deferred before answering).
- Is there a plan to get Petr (and whoever uses the PC/S9+) onto 1.17+ proactively, or does the app's own self-update prompt (already built per `secureapp-update-distribution-plan.md`) suffice without further action?

## Code Snippets (primary evidence — captured verbatim, too expensive to re-derive from a diff)

**`AppShell.xaml.cs` `RebuildTabBar` — the final, correct version (commit `7c7178a`):**
```csharp
private void RebuildTabBar()
{
    if (Items.Count == 0 || Items[0] is not TabBar tabBar) return;

    var desired = _hideableTabs
        .Where(t => Preferences.Default.Get(t.PreferenceKey, t.DefaultVisible))
        .Select(t => (ShellSection)t.Tab)
        .ToList();
    var current = tabBar.Items.Where(i => _hideableTabs.Any(h => h.Tab == i)).ToList();
    if (desired.SequenceEqual(current)) return; // <-- the fix: no-op when nothing changed

    foreach (var (tab, _, _) in _hideableTabs)
    {
        if (!desired.Contains(tab))
            tabBar.Items.Remove(tab);
    }
    for (var i = 0; i < desired.Count; i++)
    {
        if (!tabBar.Items.Contains(desired[i]))
            tabBar.Items.Insert(i, desired[i]);
    }
}
```
Compare against attempt 1 (`3b8e476`) and attempt 2 (`bdf9db1`), both of which kept the original unconditional `foreach (var (tab,_,_) in _hideableTabs) tabBar.Items.Remove(tab);` followed by re-adding everything, and only tried to patch the SYMPTOM by saving/restoring a selection reference around that same broken remove-all/re-add cycle. The type confusion in those attempts: `Shell.CurrentItem` is `ShellItem` (the `TabBar` itself, since it's the only top-level item); `TabBar.CurrentItem` is `ShellSection` (the actually-selected `Tab`, since `Tab : ShellSection`). Attempt 1 used the former (never matched anything at runtime); attempt 2 used the latter (matched, but restoring selection around a still-broken remove/re-add cycle made the Android-native-bottom-nav-vs-Shell-internal-state desync WORSE, not better).

**`App.xaml.cs` `ResolveNotificationRouteAsync` — 1:1 peer re-resolution logic:**
```csharp
private async Task<string> ResolveNotificationRouteAsync(Notification notification, IServiceProvider services)
{
    if (notification.Category == NotificationCategory.Schedule)
        return nameof(WorkplacePage);

    if (notification.RelatedGroupChatId is { } groupChatId)
    {
        AppShell.GroupChatPageFactory.PendingKey = groupChatId.ToString();
        return $"{nameof(GroupChatPage)}?groupChatId={groupChatId}";
    }

    if (notification.RelatedChatSessionId is { } sessionId)
    {
        using var scope = services.CreateScope();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<IChatSessionRepository>();
        var original = await sessionRepo.GetByIdAsync(sessionId);
        var targetSessionId = sessionId;
        if (original is { State: ChatSessionState.Closed })
        {
            // The stored session may have been superseded by a resync — find the CURRENT
            // live session for the same peer (matched by public-key hex, never by id).
            var allForPeer = await sessionRepo.GetAllByPeerPublicKeyAsync(original.PeerIdentityPublicKey);
            var current = allForPeer
                .Where(s => s.State != ChatSessionState.Closed)
                .OrderByDescending(s => s.LastRatchetedAtUtc ?? s.CreatedAtUtc)
                .FirstOrDefault();
            if (current is not null) targetSessionId = current.Id;
        }
        AppShell.ChatPageFactory.PendingKey = targetSessionId.ToString();
        return $"{nameof(ChatPage)}?chatSessionId={targetSessionId}";
    }

    return $"{nameof(NotificationDetailPage)}?notificationId={notification.Id}";
}
```
(Reconstructed from the session's own implementation work — exact variable names may differ slightly from the committed version; the LOGIC — peer-public-key-hex re-resolution, never session-id matching — is what matters and is verified against the actual commit intent.)

**`FontScaling.cs` — the Android handler-mapper interception that makes literal `FontSize` values actually scale:**
```csharp
public static void Register()
{
    Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping("FontScaling", (handler, view) =>
    {
        if (view is not ITextStyle style || handler.PlatformView is not TextView textView) return;
        ApplyScale(textView, style);
    });
    // ...repeated for ButtonHandler, EntryHandler, EditorHandler, PickerHandler, DatePickerHandler, TimePickerHandler
}

private static void ApplyScale(TextView textView, ITextStyle style)
{
    var baseSizeSp = style.Font.Size > 0 ? style.Font.Size : textView.TextSize / textView.Resources.DisplayMetrics.ScaledDensity;
    textView.SetTextSize(ComplexUnitType.Sp, (float)(baseSizeSp * Factor));
}
```
Called ONCE from `MauiProgram.CreateMauiApp()`, BEFORE `MauiApp.CreateBuilder()` — ordering matters because handler mappers must be registered before any handler instance is constructed.

**Relay: `device_app_logs` schema + trim query (`RelayDatabase.cs`):**
```sql
CREATE TABLE IF NOT EXISTS device_app_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id TEXT NOT NULL,
    kind TEXT NOT NULL,       -- 'errors' or 'metrics'
    line TEXT NOT NULL,
    received_at_utc TEXT NOT NULL
);

-- On every AppendAppLogLines call, after inserting:
DELETE FROM device_app_logs
WHERE device_id = @deviceId AND kind = @kind
  AND id NOT IN (
    SELECT id FROM device_app_logs
    WHERE device_id = @deviceId AND kind = @kind
    ORDER BY id DESC LIMIT @maxLines   -- 5000
  );
```

**`AppLogUploader.cs` — offset-tracking logic (the part that avoids re-uploading already-sent lines):**
```csharp
var offsetKey = $"applog_uploaded_bytes_{kind}"; // "errors" or "metrics"
var lastOffset = Preferences.Get(offsetKey, isFirstRun ? Math.Max(0, fileLength - FirstRunTailBytes) : 0L);
// Read from lastOffset to EOF, split on the LAST '\n' so a partial trailing line
// (still being written) is never uploaded prematurely — its bytes stay un-offset
// for the next sweep to pick up along with whatever gets appended after it.
// Rotation handling: if AppLog.cs has rotated errors.log into errors.log.1 since
// the last upload (detected by the current file being SHORTER than lastOffset),
// first drain whatever's new in the .1 backup, then reset the offset to 0 for
// the fresh main file.
```

## Evidence & Data (continued)

**AndroidManifest.xml — 3 new permissions this session:**

| Permission | Why |
|---|---|
| `android.permission.FOREGROUND_SERVICE_REMOTE_MESSAGING` | required (Android 14+/API 34+) to declare `RelayConnectionService`'s `ForegroundServiceType = TypeRemoteMessaging` |
| `android.permission.RECEIVE_BOOT_COMPLETED` | lets `RelayConnectionBootReceiver` restart the service after a phone reboot |
| `android.permission.WAKE_LOCK` | held briefly by the foreground service during reconnect attempts so Doze doesn't immediately re-suspend the process mid-handshake |

**New Preferences keys introduced this session** (all per-device, `Microsoft.Maui.Storage.Preferences`, none relay-synced):

| Key | Purpose | Default |
|---|---|---|
| `opicentrum_duty_roster` | JSON-serialized `DutyEntry` list, `DutyRosterStore` | (empty) |
| `widget_show_duty` | widget mode flag (rozpis vs. duty list) | `false` (rozpis) |
| `chat_font_size_index` | `ChatAppearance` font-size choice | 1 (Normal) |
| `chat_bubble_hex` | custom own-bubble colour, empty = follow app accent | (empty) |
| `bg_run_last_prompt_utc` | throttles the background-run explanation dialog to 1×/3days | 0 |
| `applog_uploaded_bytes_errors` / `applog_uploaded_bytes_metrics` | `AppLogUploader` per-kind byte offset | 0 |
| `opicentrum_last_bg_sync_utc` | throttles `RunBackgroundOpicentrumSyncAsync` to 1×/3h | 0 |

**New MAUI resource keys (`Styles.xaml`):** `ChatMessageFontSize` (double, points), `ChatOwnBubbleColor`, `ChatOwnBubbleTextColor`, `ChatOwnBubbleMetaColor` (all `Color`) — set once at startup from Preferences, re-set live by `ChatAppearance.ApplyFontSize`/`ApplyBubbleColor`/`Refresh`.

**Widget layout diff (`widget_notifications.xml`):** removed the always-on `widget_duty_today` TextView from the first (1.16) iteration entirely; added `widget_mode_toggle` (TextView, tappable, label toggles "Služby ›" / "Rozpis ›") and `widget_duty_list` (TextView, multi-line, `visibility="gone"` by default, shown/hidden opposite to the 5 existing day rows).

**Opicentrum HTML parsing specifics (`OpicentrumParsing.cs`)** — captured because re-deriving the real page structure required a live logged-in HTML dump, expensive to redo:
- `pracoviste.php` roster-editor-only cell attribute: `onmousedown="datumupravovany=YYYYMMDD;osoba=NNN"` — present ONLY when the viewing account has roster-edit rights.
- Real dump sizes that proved the point: a fresh (non-logged-in-session) load of the page was 2187 bytes (login redirect); the SAME page loaded from a cached logged-in Chrome tab (captured via CDP tunnelled over `adb forward`) was 75988 bytes with 217 `onmousedown`-bearing cells — the difference IS the roster-editor-only markup, confirming the hypothesis before writing a line of parser code.
- `sluzby7.php` duty-slot page: 7 named duty-slot columns per day (varies slightly by hospital department roster, not hardcoded in the parser — column headers are read live).
- The "posunutá služba" (shifted/late duty) is the LAST workplace row on `pracoviste.php`, literally titled "Odpolední 13-21 h" in the source HTML — confirmed via the same real dump, not assumed.

## Quick Start for Next Session

```powershell
# Reference docs
Get-Content "H:\Visual Studio\C#\Aplikace\DEVELOPMENT_PLAN.md"
Get-Content "H:\Visual Studio\C#\Aplikace\IMPROVEMENT_PLAN.md"
Get-Content "H:\Visual Studio\C#\Aplikace\NOTIFICATION_HUB_SPEC.md"
Get-Content "H:\Visual Studio\C#\Aplikace\keystore\PRISTUPY.md"

# Key files to read first
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\App.xaml.cs" | Select-Object -First 60
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\AppShell.xaml.cs"
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\Notifications\ActiveChatThread.cs"
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\Diagnostics\AppLogUploader.cs"

# Verify current relay state (device app logs, deploy status)
ssh secureapp-pi "docker ps --format 'table {{.Names}}\t{{.Status}}' | grep secureapp"

# Verify installed app version on S23+
$adb = "C:\Users\dvora\AppData\Local\Android\Sdk\platform-tools\adb.exe"
& $adb connect 192.168.50.28:5555
& $adb -s 192.168.50.28:5555 shell dumpsys package com.companyname.secureapp.presentation | Select-String versionName

# Next action
# Ask the user: (a) get Petr/PC/S9+ onto 1.17+, (b) device-verify the still-unverified 1.16/1.17
# features listed in "Where We're Going" #2, or (c) resume Phase 2b/3 with a fresh scope decision.
```
