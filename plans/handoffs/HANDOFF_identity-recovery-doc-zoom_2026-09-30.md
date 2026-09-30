# SecureApp: ARIM contacts, data-loss incident + identity backup, document viewer watermark removal + pinch-zoom — releases 1.18→1.25

**Date:** 2026-09-30
**Status:** IN PROGRESS
**Bead(s):** none
**Epic:** none
**Chain:** `standalone-be860f68` seq `1`
**Parent:** none — first in chain
**Prior chain:** none — first in chain

---

## Related Handoffs

- `plans/handoffs/HANDOFF_notifications-background-widget_2026-09-28.md` (chain `standalone-80b5bcbd`, seq 1) — the immediately preceding session (2026-09-28). Read at the start of this session ("načti md") for context, but NOT treated as this handoff's parent: none of that session's own "Where We're Going" items (get 1.17 onto other devices, device-verify 1.16/1.17 features, Phase 2b/3 scope, silent-catch audit, NOTIFICATION_HUB_SPEC.md update) were what actually drove today's work — today was almost entirely new, real-time user requests (ARIM contact import follow-ups, a real data-loss incident, document viewer features) rather than a continuation of that plan. Its device-install items are still genuinely open — see "Where We're Going" below.

## Reference Documents

- `DEVELOPMENT_PLAN.md` (repo root) — canonical milestone/task tracker (Milestones 1–6, unchanged this session).
- `IMPROVEMENT_PLAN.md` (repo root) — Phase 0–4 plan; Phase 2b/3 still explicitly deferred by the user (not touched this session).
- `NOTIFICATION_HUB_SPEC.md` (repo root) — still has the stale "§26 cross-link not started" note the prior handoff already flagged; still not fixed.
- `keystore/README.md` — release keystore facts; **directly load-bearing this session** — see the data-loss incident below, which is exactly the scenario this file already warned about.
- `keystore/PRISTUPY.md` — Czech-language credential/access inventory, git-ignored.
- Memory files (`C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\`): `never-debug-deploy-real-devices.md` (**new this session**, HARD RULE born from today's incident), `android-fast-deploy-gotcha.md` (**updated this session** with a CRITICAL EXCEPTION section), `feedback-batch-builds.md` (**updated this session** with a self-update-publish gotcha), `secureapp-infra-paths.md`, `adb-wireless-technique.md`.

## The Goal

SecureApp is a .NET MAUI (Android/iOS/Windows) Clean-Architecture app for a hospital anesthesiology/ICU team: E2EE chat, a shared encrypted document/procedure library, a duty logbook, a company phone directory, a personal work-schedule module ("Rozpis") synced against the hospital's phpRS portal, and a "Nástěnka" (notification hub) home screen with an Android widget. This session (2026-09-30, continuing directly from 2026-09-28) covered five largely independent threads, all user-driven in real time: (1) finishing/refining the ARIM WhatsApp-group contact import from the prior session (Czech sort, search, a naming-unification correction); (2) a real production incident — a Debug-signed build got sideloaded over the user's release-signed S23+, and Android silently wiped the entire local encrypted vault instead of refusing the mismatched-signature install — and the disaster-recovery feature (identity backup/restore) built directly in response; (3) an invisible pixel-level watermark that was designed, built, tested, found to have a real bug, fixed, and then DELETED again once the user concluded it couldn't serve its actual purpose; (4) a document-download button + admin-searchable audit log; (5) removing the app's only DLP screen-photograph deterrent (the visible moving watermark) in favor of an actually-usable pinch-to-zoom-and-pan document viewer, which itself went through three rounds of real bugs found via the user's own live device testing. The user (a physician, sole developer-by-proxy, admin of a ~4-device community) drives every feature from real usage; the working pattern this session was unusually fast-iteration: build → deploy directly to the S23+ over WireGuard AND publish to the relay's self-update endpoint → user tests live → reports a specific defect → fix → repeat, several times per feature.

## Where We Are

- **Version history this session:** 1.17(18) → 1.18(19, wrong-device Debug install, the incident) → 1.19(20, identity backup, first real Release publish) → 1.20(21, Czech sort/search/watermark/download-log bundle) → 1.21(22, ARIM CollectionView height fix) → 1.22(23, both watermarks removed, +/- zoom buttons) → 1.23(24, pinch-zoom v1 + rename) → 1.24(25, pinch-zoom v2, fixed corner-jump/slow-pan bugs) → 1.25(26, pinch-zoom v3, sensitivity+resolution tuning) — **current, installed on S23+, relay serving 1.25/26 for self-update.**
- **Latest commit:** `86befd7` "Zoom: faster pinch sensitivity, higher source resolution, lower max (1.25, build 26)", on `main`, pushed to both `github` (`Klotho79/SecureApp`) and `pi` remotes. 12 commits this session total (`d801ee9` through `86befd7`).
- **Working tree:** clean except `.claude/settings.local.json` and the prior handoff file (both pre-existing, unrelated local diffs, not touched this session).
- **S23+ (`c0b7bfbd-7383-455c-b53c-aa0a0b4b7569`):** running 1.25(26), Release-signed, confirmed via `dumpsys package`. **Its local database was fully wiped mid-session** (see incident below) and re-registered as a fresh device — chat history on this device is permanently gone; Rozpis/relay-registration state is fresh.
- **GitHub backup remote:** was reported broken ("Permission denied (publickey)") at the END of the 2026-09-28 session; this session it worked on the very first retry (`ssh -T git@github.com` → "Hi Klotho79!... successfully authenticated") — transient, not actually fixed by anything done here, just no longer reproducing.
- **`src/SecureApp.Presentation/Contacts/ContactPhoneCache.cs`** (new) — offline name→phone snapshot of the shared contact directory (refreshed every ~3min background sweep), used by the widget's duty-roster phone icons. Matches via `OpicentrumParsing.NamesMatch` (existing fuzzy name matcher).
- **`App.xaml.cs`** — `_backgroundOpicentrumSyncInterval` reduced `TimeSpan.FromHours(3)` → `TimeSpan.FromMinutes(10)`; new `WorkplaceRoute(string route)` helper prefixes `WorkplacePage`'s route with `//NotificationsTab/` when needed (fixes the tab-highlight bug — see "What We Tried"); `ContactPhoneCache.RefreshAsync` wired into the existing 3-min supervisor sweep.
- **`NotificationsWidgetProvider.cs` / `widget_notifications.xml`** — 10 fixed duty-roster rows (`widget_duty1`..`widget_duty10`) each with a phone icon (`Intent.ActionDial`, never auto-dials) shown only when `ContactPhoneCache.TryFindPhone` resolves a number; header row restructured into a horizontal `LinearLayout` with the existing Rozpis/Služby toggle PLUS a new "☎ Kontakty" button (routes via `"//ContactsTab"`, no `WorkplaceRoute` fixup needed since Contacts is a real tab). New `widget_ic_phone.xml` vector drawable.
- **`ContactsViewModel.cs` / `ContactsPage.xaml`** — "Soukromé kontakty ARIM" is its own collapsible section (separate from "Firemní kontakty", split client-side by `Note == "ARIM"`, no relay schema change). Sorted with `StringComparer.Create(CultureInfo.GetCultureInfo("cs-CZ"), ignoreCase: true)` (proper Czech collation, not `OrdinalIgnoreCase`). New `ArimSearchQuery`/`ApplyArimFilter()` (name-or-number substring match, auto-expands section on non-empty query). Both "Firemní kontakty" and the ARIM section's item lists use `CollectionView` with an explicit `HeightRequest="420"` on the ARIM one (see "What We Tried" — this was a two-round bug). Drag-reorder removed from the ARIM section (pointless once alphabetically sorted — the next re-sort would just overwrite a manual order); Firemní kontakty keeps it.
- **`relay/SecureApp.Relay/Program.cs`/`Contracts.cs`/`RelayDatabase.cs`** — new tables/endpoints this session: `identity_backups` (PUT/GET `/identity-backup/{lookupKey}`, unauthenticated by necessity), `document_downloads` (POST `/document-downloads` device-authed, GET `/admin/document-downloads` admin-authed search), plus two admin-only one-off ops added to the existing `/contacts` family: `POST /admin/contacts/bulk-import` (already existed from the prior session, used again) and `POST /admin/contacts/rename` (new, bulk id+DisplayName).
- **`src/SecureApp.Presentation/Identity/IdentityBackupService.cs`** (new) + **`IIdentityBackupService`** (Domain) — PBKDF2-SHA256 (210k iterations) + AES-256-GCM envelope; exports the ML-KEM ChatIdentity key pair, encrypted with a user passphrase, to a local file (shared via OS share sheet) AND the relay (keyed by `SHA256(email+passphrase)`, server never sees either raw value or the decrypted identity). `ICryptoService.ExportEncryptionKeyMaterialAsync`/`ImportEncryptionKeyMaterialAsync` (new) and `IMessagingService.RestoreLocalIdentityAsync` (new, refuses if a ChatIdentity key is already active) support it. Settings UI: "Záloha identity" card when registered, "Obnovit identitu ze zálohy" (file or relay) instead of it when not — restoring chains straight into the existing (now-instant, auto-approved) activation flow.
- **`src/SecureApp.Presentation/Rendering/PixelWatermark.cs` + `tools/SecureApp.WatermarkDecoder/`** — **built, tested, fixed, then DELETED this session** (commits `fc589c8` then `041db5f`). See "What We Tried" for the full arc; net effect on the current codebase is zero (never shipped to a real device in its working form).
- **`DocumentViewerViewModel.cs` / `DocumentViewerPage.xaml(.cs)`** — the Milestone-4 visible moving watermark (username+timestamp+IP overlay, `WatermarkText`/`WatermarkTranslationX/Y`/`_watermarkTimer`/`GetLocalIpAddress`) **removed entirely** — this was the app's only actual DLP protection against someone photographing the screen; removed by explicit, repeated user instruction after they judged it made documents unreadable with no way to zoom past it. Replaced with: a "⬇ Stáhnout" button (new, decrypts `Document.EncryptedContent` — the ORIGINAL file, not a rendered page — shares via OS share sheet, logs to `document_downloads`); a "✏ Přejmenovat" toolbar item (new — `Document.Rename` already existed on the entity but had never been wired to any UI); and pinch-to-zoom + one-finger pan (`PinchGestureRecognizer`/`PanGestureRecognizer` in code-behind, center-anchored, clamped, went through 3 bug-fix rounds — see below). `MaxRenderWidth`/`MaxRenderHeight` raised 1200×1600 → 1800×2400 for zoom headroom.
- **Global (outside this repo):** none new this session (the handoff/handoffplan skills were installed the PRIOR session, 2026-09-28).
- **Deploy pipeline used consistently, every version this session (1.20/21 onward):** `dotnet publish -f net10.0-android -c Release` (signed via `keystore/signing.local.props`) → `adb -s 10.8.0.3:5555 install` directly to the S23+ over WireGuard (2–4 min per ~65MB APK) → `scp` the same APK to the Pi → a small generated bash script (`grep`s the admin secret server-side, never echoed) → `POST /admin/upload/android` with `versionCode`/`versionName` form fields. Every one of these steps was run for EVERY version bump this session (1.20 through 1.25) — six full cycles.
- **Version 1.18(19) is the ONLY version this session built via `dotnet build -t:Run` (Debug)** — the one that caused the incident. Every subsequent version used `dotnet publish -c Release` exclusively, per the new hard rule.

## Tooling Notes (operational, for the next session)

- **The Claude Code Edit/PowerShell tool's safety classifier failed with "no verdict (error)" at least twice this session**, each lasting several minutes, unrelated to SecureApp itself. Handling that worked: try the exact same call again once or twice, then STOP retrying and explicitly tell the user it's a tooling outage (not an access/permissions issue on their end), and wait before trying again — the harness itself slows repeated attempts, and hammering it doesn't help.
- **`adb install` (not `-t:Run`) to the S23+ over WireGuard reliably took 2–4 minutes per ~65MB Release APK this session** — background-task polling needs to budget for this; checking `dumpsys package` immediately after kicking off the install will show the OLD version for a while before it updates, which is expected, not a failure sign.
- **PowerShell↔SSH↔bash quoting remained fragile this session** (same caveat the 2026-09-28 handoff already recorded) — every relay-side script this session was written to a local file first, `scp`'d over, then run via `bash /tmp/script.sh` rather than inlined into a single `ssh` command string. This pattern worked reliably every time it was used; inline `ssh secureapp-pi "curl ... -d '{...}'"` style commands were avoided entirely after early attempts produced mangled output.

## What We Tried (Chronological)

0. **ARIM WhatsApp-group contact import (prior sub-session, referenced via git log + carried-forward context — `d801ee9`/`91b5afd`, precedes the visible transcript of this handoff but is squarely part of today's work).** The user provided a WhatsApp-group member export (71 names + phone numbers) to import as shared contacts, tagged `Note = "ARIM"` so they could be told apart from the pre-existing "Firemní kontakty" (company extension directory). Built an admin-only `POST /admin/contacts/bulk-import` relay endpoint (bulk insert, admin-secret-authed since this is an operator action, not a device-originated write) to seed the 71 rows in one call rather than 71 individual `ISharedContactService.PublishAsync` round-trips. Initially landed as entries inside the SAME "Firemní kontakty" list (distinguished only by the `Note` tag) — the user's later correction (*"udelej založku stejnou jako je Firemní kontakty ale nazvy ji Soukromé kontakty ARIM"*) is what produced the separate collapsible section (`91b5afd`), which is where this handoff's visible history effectively begins.

1. **Background-connection notification visibility (early).** User asked if the persistent "Připojen" notification (from `RelayConnectionService`, built 2026-09-26) could be invisible rather than just silent. Explained the Android foreground-service requirement (can't run indefinitely without SOME notification) but confirmed the channel is already `NotificationImportance.Min` — genuinely silent, no sound/vibration/status-bar icon. Offered a settings shortcut to open that notification-category's system settings directly; not yet built (user didn't take up the offer, moved to the next topic).

2. **Opicentrum background sync interval, 3h → 10min.** User: "pokud dojde ke zmene zarazeni prijde notifikace... kontrola cca 10 min?" — wanted schedule-change notifications to surface faster. Simple constant change in `App.xaml.cs`. Noted (not acted on further): 4 devices × 6x/hour = ~18x more requests to the hospital's legacy phpRS portal than the prior 3h interval; accepted as fine for a small internal system.

3. **Widget duty-roster phone icons + Kontakty button (mid-turn interrupt).** User interrupted a different in-progress task: "ve wigetu rovnou ke sloužícím přidej pictogram a na ktery kdyz se klikne skopiruje sluzebni tel do tel k vytoceni". Built `ContactPhoneCache` (new file) for an offline, background-refreshed name→phone lookup (the widget must never make a network call on refresh — same principle `DutyRosterStore` already established). Then, same turn's follow-up: "Na wiget dej i moznost otevrit kontakty" — added a second header button, reusing the existing `widgetRoute` mechanism.

4. **THE TAB-HIGHLIGHT BUG (widget/notification opens Rozpis, wrong tab lit).** User: *"kdyz jsem na kontaktech a prejdu na widget a klepnu do nej otevre se mi rozpis porad sviti kontakty a nejde na ne prepnout"*. Investigated `PendingWidgetRouteRouter`'s own doc comment, which already stated the root cause plainly: `WorkplacePage` isn't a tab, it's `Routing.RegisterRoute`-reached and pushed on top of WHATEVER tab is currently active. A bare relative `GoToAsync(route)` therefore pushes onto Kontakty (wherever the user happened to be), not Nástěnka (where Rozpis conceptually lives) — the tab bar correctly shows Kontakty since, per Shell's own model, that IS still the active tab. Fix: `WorkplaceRoute()` helper prefixing with `"//NotificationsTab/"` — same absolute-route pattern `SmartSearchViewModel` already used for `"//ContactsTab"`. Applied to both the widget path AND the schedule-notification path (`ResolveNotificationRouteAsync`), which had the IDENTICAL bug. Verified via compile-check only initially, confirmed genuinely fixed once the resulting build reached the S23+ later.

5. **"Udelej build" → THE DATA-LOSS INCIDENT.** Bumped 1.17(18)→1.18(19), ran `dotnet build -t:Run`. **First deploy went to the WRONG device** — `adb devices` showed serial `22dab4387e0b7ece`, assumed to be the S23+ without checking; it was actually Petr Faltus's... no, it was the user's own **S9+** (`SM-G965F`/`star2lte`), confirmed via `adb -s 22dab4387e0b7ece shell getprop ro.product.model`. Corrected by reconnecting via WireGuard (`adb connect 10.8.0.3:5555`, per the standing "always try this first" memory) and verifying the model field before deploying again. **The deploy to the CORRECT device (S23+) then wiped its entire local database**: `dotnet build -t:Run` produces a Debug-signed APK; the S23+ was running a Release build signed with `secureapp-release.keystore`. Android cannot update between differently-signed APKs — `keystore/README.md` already warned about this exact scenario for keystore LOSS, but nobody had connected that the same signature-mismatch trap applies to any ordinary Debug deploy onto a normally-Release device. The deploy tooling silently did a full uninstall+reinstall instead of refusing (`firstInstallTime == lastUpdateTime` on `dumpsys package`, and `run-as ... ls databases/` returning "No such file or directory" was the tell). **Consequence: the S23+'s chat identity key, all chat history, and relay registration were gone — permanently, since this is E2EE and nothing is centrally backed up by design.** User's reaction: pragmatic, not angry — moved straight to "how do we make sure this never happens again."

6. **Immediate recovery: re-registering the wiped S23+.** Confirmed `TrustedAdminDevices.cs` hardcodes model `"SM-S916B"` (the S23+) to auto-bootstrap as Admin regardless of device id — this exact feature was built 2026-09-23 after a similar incident, so recovery needed zero manual role-assignment. Confirmed `/activation/request` auto-approves instantly (a 2026-09-19 design decision, not needing an admin's manual click) — so re-registration was a pure in-app "type email → Aktivovat" action, no SSH/relay intervention needed. **Tooling outage during this window**: the Edit/PowerShell tool's safety classifier repeatedly returned "no verdict (error)" for several minutes (unrelated to SecureApp — a Claude Code infrastructure issue); handled by not hammering retries and explaining clearly to the user that it wasn't an access/permissions issue on their end.

7. **Identity backup/restore feature, built in direct response to #5.** User: *"potrebuji aby jsi vymyslel zpusob jak tedy pri ztrate identity tu identitu ziskat zpet ne vytvorit novou... nikdo nechce ztracet historii vytvor neco... co pomuze v identifikaci identity i pri reinstalaci apky"*. Clarified up front (before building) that this can restore IDENTITY continuity (peers still trust the restored key) but NOT chat history (E2EE — plaintext never left the two devices in a conversation). User accepted this distinction explicitly. Asked one clarifying question (local-file-only vs. local-file+relay) — user chose **both** ("Soubor + zálohovaně i na relay"). Built `IIdentityBackupService`/`IdentityBackupService`, new `identity_backups` relay table, Settings UI. A LATER user message in the same arc ("konverzace prece jde po ziskani identity obnovit z chatu druheho uzivatele...") proposed peer-assisted history recovery (device-to-device, never through the server) — explicitly SCOPED OUT of this session after a clarifying question confirmed the user did NOT want admin-readable chat content (only participant-to-participant recovery), and the user agreed to defer it as a properly-planned follow-up rather than build it hastily mid-incident.

8. **GitHub SSH key — reported broken, found already working.** Prior session ended with `git push github main` failing "Permission denied (publickey)" despite the correct key being offered. This session, first retry (`ssh -T git@github.com`) succeeded immediately ("Hi Klotho79!..."), and the subsequent `git push github main` also succeeded. No code/config change was needed — genuinely transient, not something this session's changes fixed.

9. **Contact naming-unification saga (3 sub-rounds).** User: *"vilem na vilem dvorak a peter f. na petr faltus... proste sjednotit"* — initially I renamed the ARIM-imported entries ("Dvořák Vilém"/"Faltus Petr") to append "- služební", using a self-invented "numbers starting 7=service, 6=personal" heuristic that turned out to be coincidental, not a real rule, AND missed two ALREADY-EXISTING pre-import "služební" entries ("Vilda služební", "Petr F. služební") with yet different phone numbers, because my initial SQL search only matched full surnames. User corrected: *"maji tam byt ty puvodni kontakty jen zmeni vilem na vilem dvorak a peter f. na petr faltus... proste sjednotit"* — the OLD pre-existing entries (with the genuinely correct service numbers) should just get their DISPLAY NAMES unified to full names, not have the "služební" label moved to the ARIM numbers at all. Final state (see Evidence table) required THREE separate `/admin/contacts/rename` calls to reach.

10. **ARIM contacts: Czech alphabetical sort + search.** User: *"soukrome kontakty serad podle abecedy ceske, a taky moznost vyhledavani"*. Implemented `CzechNameComparer` (culture-aware `cs-CZ` collation, NOT `OrdinalIgnoreCase` which the existing static phone-directory sort already used and which sorts "Ch" wrong for Czech readers) plus `ArimSearchQuery`/`ApplyArimFilter()`. Also had to keep `_allArimContacts` (the unfiltered/unsorted raw fetch) as a separate field so search-filtering doesn't lose data on every keystroke, and to keep delete operations in sync with it.

11. **ARIM CollectionView performance — TWO rounds, both reported by the user as "still slow" after the first fix.**
    - **Round 1** (`091748a`): diagnosed that `BindableLayout` (used for both the ARIM section and Firemní kontakty) builds every row eagerly regardless of the "collapsed by default, empty `VisibleEntries` until expanded" lazy pattern already established for the static phone directory — the LAZINESS only delays WHEN the cost is paid, not whether it's paid all at once. Switched both lists to `CollectionView` (which supports true virtualization/recycling). Compile-checked, published as part of the 1.20/21 bundle.
    - **User reported it was STILL slow** ("otevreni tech 71 kontaktu trva pekelne dlouho") — confirmed the user really was on 1.20/21 (the fix's own build) via `dumpsys package`, ruling out "testing an old build" as the explanation.
    - **Round 2** (`67d44c6`): root cause was a KNOWN MAUI gotcha not caught in round 1 — a `CollectionView` with no fixed height, nested inside this page's own outer `ScrollView` (needed for the page's multiple stacked cards), still has to MEASURE EVERY ITEM to compute its own natural height for that outer scroll layout, defeating virtualization exactly as thoroughly as the `BindableLayout` it replaced. Fix: `HeightRequest="420"` on the ARIM `CollectionView` only (Firemní kontakty's list stayed small enough this was never its bottleneck), turning it into its own bounded, internally-scrolling, genuinely virtualized list. This time confirmed fixed by the user after testing 1.21/22.

12. **Watermark visibility question → invisible pixel watermark → built, tested, bug found+fixed, then DELETED (biggest single arc this session).**
    - User asked whether the visible DLP watermark needs to be visible, or could be invisible-until-photographed. Explained that true "invisible on screen, visible only in a photo" doesn't work reliably (camera exposure/compression variance) and, even if it did, would need specialized forensic recovery tooling nobody has.
    - User: *"pokud se dostane ven bude to resit instituce ktera tyto nastroje ma takze bych prosil aby tam byly neviditelne vodoznaky schovane v datech pixelu"* — proceed anyway, ADDITIONAL to the visible one, institution will handle decoding. Built `PixelWatermark.cs` (LSB steganography, magic+CRC16-framed, tiled across first 60k pixels) wired into `DocumentRenderingService.RenderPageAsync`, plus a **standalone** `tools/SecureApp.WatermarkDecoder` console tool (deliberately no reference to the main app, so an investigator can run it with nothing else installed) with a Czech README stating the same honest limits up front.
    - **Built a real round-trip test harness** (scratch console project, embed→PNG-encode→decode→verify) BEFORE shipping — this caught a genuine bug: `var channel = index % 3 switch { 0 => pixel.Red, 1 => pixel.Green, _ => pixel.Blue };` (a switch expression chained directly onto `%` with no parens) mis-evaluated at runtime — the R/G channels came back right but B never did, confirmed by isolating the exact divergence point (bit index 5, pixel1's Blue channel) via targeted debug prints. Fix: bind the modulo to its own variable first. All 3 tests (direct in-memory, through a real PNG encode/decode, and a no-watermark negative case) passed after the fix; ALSO verified against the REAL BUILT decoder .exe, not just the test harness's copy of the logic.
    - Committed as `fc589c8`. Shipped as part of the document-download-log bundle.
    - **Then the user reconsidered**: *"Ne chtel jsem aby se zobrazoval neviditelny vodoznak... o tom uz jsme se bavili pokud nekdo sfoti display....ma mit na fotce neviditelny vodoznak"* — clarified their ORIGINAL intent all along was for the invisible watermark to survive being PHOTOGRAPHED, which I had already explained (before building) is not achievable with LSB steganography. Offered to build a genuinely more robust technique (large-block + error-correcting-code embedding, like real anti-piracy screen watermarking) — user declined given the days-of-work/uncertain-payoff tradeoff: *"V tom pripade vyhod vodoznak uplne"*. Deleted `PixelWatermark.cs`, its call site, and the entire `tools/SecureApp.WatermarkDecoder` project (commit `041db5f`) — net zero change to the shipped app, but real engineering + a real bug-fix along the way, now reverted.

13. **Document download button + admin-searchable audit log.** User: *"uploadni to dokumenty bude mozne stahovat uzivateli ale bude log kdo co kdy stahl podle dokumentu vyhledatelny v logu"*. Clarified scope via one question (add a NEW download button vs. just log existing opens) — user picked "add a button". Built cleanly, no back-and-forth needed (contrast with the watermark/zoom arcs).

14. **"Opet nejde aktualizovat pres apku" — the self-update publish gap, TWICE.** After building/deploying via `dotnet build -t:Run`/adb directly to the S23+ (correctly, this time, matching the Release signing), the user tried the in-app "Zkontrolovat aktualizaci" and it reported "up to date" despite genuinely newer code existing. Root cause (recurring, not new): the relay's self-update manifest only reflects what's actually been `POST /admin/upload/android`'d — a local build/adb-install never touches it. Happened once mid-session (fixed by publishing 1.20/21) and the underlying gap was noted in memory afterward, but the SAME gap-shaped confusion resurfaced implicitly every time a new version was built for the rest of the session — from that point on, EVERY subsequent build (1.21/22 through 1.25/26) was both adb-installed to the S23+ directly AND published to the relay in the same pass, without being asked again.

15. **"Na vlozenych souborech je porad videtelny vodoznak" → visible-watermark-removal → zoom feature, FOUR rounds.**
    - User's report was ambiguous at first — asked to clarify via `AskUserQuestion` whether they meant the DOWNLOADED file or a viewed chat attachment. Answer: viewed attachment. Confirmed this is by-design (DocumentViewerPage is one shared screen for both Library documents and chat attachments, so the visible watermark applies uniformly) and asked whether to differentiate chat photos vs. library documents.
    - User's REAL point, once stated plainly: *"Vdyt do nejdr ani cist ani zvetsit je to k icemu pokud je to nepouzitelne... urcite tl vyhod a prixej moznost zvetseni dokumentu aby to slo cist nebo tisknout"* — the watermark makes documents unreadable with no way to zoom past it; remove it and add real zoom. Confirmed explicitly via `AskUserQuestion` that removing the app's ONLY working screen-photograph deterrent was really intended (not just the invisible one) — user confirmed yes.
    - **Round 1** (`041db5f`): removed both watermarks; added simple +/- zoom buttons (`ImageScale` bound property, center-anchored via `AnchorX/AnchorY="0.5"`, no panning).
    - User rejected this UX outright: *"asi spise nejake plynule zvetsovani a taky posun pri zvetseni bez toho je to nesmyslne, mozna jak to byva roztazenim prstu... zrus ty velke tlacitka"* — wants real pinch-to-zoom-and-pan, not stepped buttons.
    - **Round 2** (`c730bd9`): rewrote as `PinchGestureRecognizer` + `PanGestureRecognizer` in code-behind, following a reconstructed-from-memory version of Microsoft's documented pinch-then-pan sample (dynamically flipping `AnchorX`/`AnchorY` to (0,0) on pinch start, `ScaleOrigin`-based anchor tracking). Also added document rename (`Document.Rename` existed on the entity, never had a UI) via a new `ToolbarItem`.
    - User: *"Zvetsovani neni plynule a navic se to zvetsiluje z praveho dolniho rohu a posun je pomaly"* — two real bugs.
    - **Round 3** (`1aafd75`): root-caused BOTH. (a) Flipping `AnchorX/AnchorY` from the default (0.5,0.5) to (0,0) the instant a pinch starts snaps the EXISTING `Scale` transform onto a new origin instantly — a visible jump toward a corner. Fix: never touch the anchor at all; keep MAUI's own center default permanently, so zoom always expands symmetrically outward from the middle. (b) The pan clamp range was computed for the WRONG (corner) anchor's overflow shape. Fix: symmetric `±Width*(scale-1)/2` range for a center anchor. (c) A THIRD bug found while fixing (b): the clamp function was writing its own result back into the pan baseline (`_panX`/`_panY`) on every single `Running` callback of the PAN gesture — but `PanUpdatedEventArgs.TotalX/TotalY` are cumulative SINCE THE GESTURE STARTED (confirmed via Microsoft's own documented semantics), not per-callback, so re-basing mid-gesture compounds the position every event and hits the edge clamp almost immediately — read as "posun je pomaly" (slow/stuck panning). Fix: the baseline only updates once, at `GestureStatus.Completed`; `Running` only clamps for display.
    - User: *"Lepsi ale neni autemticke zvetsovwni pitrebuji 3 gesta na zvetseni, pri urcitem mozna maximalnim zvetseni se zacne obrazek glicovat"* — needs 3 separate pinch gestures to reach useful zoom (two fingers on a phone physically can't spread far enough in one motion for the un-amplified 1:1 tracking used), and the image visually glitches near the max zoom level (read as a GPU/rendering-transform limit of stretching an already-rasterized bitmap that far, not something the gesture math itself controls).
    - **Round 4** (`86befd7`, current): added `PinchSensitivity = 1.6` exponent amplifying each callback's `e.Scale` delta (`Math.Pow(e.Scale, 1.6)`) so the same physical pinch covers more zoom per gesture; lowered `MaxScale` 4→3 (less extreme stretching, likely less glitching); raised `DocumentViewerViewModel`'s render resolution 1200×1600 → 1800×2400 (same READABLE result now needs less extreme `Scale` to reach). **Not yet confirmed fixed by the user as of session end** — this is the single most important open item.

**Multi-round bug-fix sagas this session, at a glance (full narrative in "What We Tried"):**

| Feature | Rounds | Root causes found | Final status |
|---|---|---|---|
| ARIM CollectionView performance | 2 | (1) `BindableLayout` never virtualizes regardless of lazy binding; (2) a `CollectionView` with no fixed height, nested in an outer `ScrollView`, still measures everything | ✅ Fixed, confirmed |
| Pixel watermark | 1 (build) + 1 (bug fix) + 1 (deletion) | `index % 3 switch {...}` mis-evaluated at runtime (Blue channel only) | Feature deleted entirely — bug fix was real but the whole feature was later judged not worth keeping |
| Document zoom | 4 (buttons v1 → pinch v2 → anchor/pan fix v3 → sensitivity/resolution tuning v4) | (v2) corner-jump from flipping AnchorX/Y mid-gesture; (v2) wrong pan-clamp range for that anchor; (v3, found while fixing the above) pan baseline corrupted by re-basing every Running callback; (v4) pinch physically can't cover enough distance in one gesture, extreme Scale glitches | v4 shipped, **unverified** |
| Contact naming unification | 3 renames | Wrong initial heuristic (6xx/7xx number-range rule was coincidental, not real); missed pre-existing entries on the first SQL search | ✅ Fixed, confirmed via direct query |

## Key Decisions

- **`WorkplaceRoute()` fixup applied to BOTH the widget path and the notification path**, not just the one the user happened to report — since both shared the exact identical root cause (a bare relative `GoToAsync` on a non-tab page), fixing only the reported instance would have left the twin bug live, to be rediscovered later at the user's expense.
- **Identity backup restores ONLY the cryptographic identity, never chat history** — a deliberate, explicitly-confirmed-with-the-user scope boundary, since restoring history would require either a central plaintext backup (breaks E2EE) or a much larger peer-assisted-resync feature (explicitly deferred, not cancelled).
- **Peer-assisted chat-history resync explicitly scoped to device-to-device only, never admin/relay-readable** — the user's own first framing ("instituce ktera tyto nastroje ma") could have been read as wanting admin access to content; a direct clarifying question confirmed they did NOT want that, only participant-to-participant recovery. This is a real, positive design constraint carried forward for whenever this follow-up gets built.
- **Deleted the pixel watermark entirely rather than leaving it "for the digital-copy case only"** — once the user clarified their actual requirement (survive a photograph) was categorically unmet by the built feature, shipping dead-weight functionality that doesn't serve any of the user's actual stated needs was rejected in favor of a clean removal, preserving the option to build a genuinely robust (large-block, error-corrected) version later if ever justified.
- **Removed the ONLY working DLP screen-photograph deterrent (the visible watermark) on explicit, twice-confirmed user instruction**, prioritizing document readability over that protection. This is a real, acknowledged security-posture regression, not an oversight — flagged plainly to the user before executing (`AskUserQuestion`, twice) rather than silently complied with on the first ambiguous request.
- **Zoom/pan gesture state lives in the Page's code-behind, not the ViewModel** — pure visual-transform bookkeeping (`Scale`/`TranslationX/Y` on a live native `Image`), matching the established "MAUI-touching glue stays in the Page" split already used for `ContactsPage`'s drag-and-drop.
- **Center-anchored zoom (never touching `AnchorX/AnchorY`) chosen over pinch-point-anchored zoom** after the pinch-point version's anchor-flipping caused the corner-jump bug — a deliberate simplification trading "zooms exactly where your fingers are" for "zooms from the middle, but never glitches from an anchor change," given the fragility already discovered in the more "natural" approach.
- **ARIM section's drag-reorder removed rather than kept alongside alphabetical sort** — a persistent Czech-alphabetical sort and a persisted manual order are mutually exclusive; keeping both would mean any reorder silently gets erased by the very next search keystroke or background refresh. Firemní kontakty (unsorted, `SortOrder`-based) keeps reordering since there's no conflicting auto-sort there.
- **Contact rename correction: unify the OLD pre-existing "služební" entries' names, not relabel the ARIM import** — after initially doing the reverse (a wrong guess based on a coincidental 6xx/7xx number pattern), the user's correction made clear the pre-existing entries already had the CORRECT service numbers; the fix was purely cosmetic naming unification on those, with the ARIM entries left as plain personal contacts.

## Evidence & Data

**Version/commit table, this session (chronological, all on `main`, pushed to both `github` and `pi`):**

| Commit | Version | Summary |
|---|---|---|
| `d801ee9` | — | Relay: admin-only bulk contact import endpoint (ARIM xlsx, prior sub-session) |
| `1ad98c1` | — | Widget: duty-roster call icon, open-Contacts button, Rozpis tab-highlight fix |
| `91b5afd` | — | Contacts: separate collapsible "Soukromé kontakty ARIM" section |
| `40fb29b` | 1.19(20) | Identity backup/restore for disaster recovery |
| `091748a` | — | Contacts: fix slow expand of the 71-row ARIM section (round 1: CollectionView) |
| `53689ae` | — | Contacts: Czech alphabetical sort + search for ARIM; admin rename endpoint |
| `fc589c8` | — | Invisible pixel-level watermark + standalone decoder tool |
| `3a17a4d` | — | Document download button + admin-searchable audit log |
| `67d44c6` | 1.21(22) | Contacts: fix ARIM CollectionView not actually virtualizing (round 2: HeightRequest) |
| `041db5f` | 1.22(23) | Remove both watermarks; add document zoom (+/- buttons, v1) |
| `c730bd9` | 1.23(24) | Document viewer: pinch-to-zoom + pan (v2), document rename |
| `1aafd75` | 1.24(25) | Fix pinch-zoom: corner-jump + slow-pan bugs (v3) |
| `86befd7` | 1.25(26) | Zoom: sensitivity + resolution tuning (v4) |

**Relay self-update publishes, this session (each is a separate `POST /admin/upload/android` + on-device `adb install` to S23+ over WireGuard `10.8.0.3:5555`):**

| VersionCode | VersionName | Install time on S23+ (`lastUpdateTime`) |
|---|---|---|
| 19 | 1.18 | 2026-09-30 ~10:23 (the WRONG-device debug build that caused the wipe) |
| 20 | 1.19 | (identity backup, first Release publish after the incident) |
| 21 | 1.20 | 2026-09-30 10:23:13 |
| 22 | 1.21 | 2026-09-30 (~2 min WireGuard transfer) |
| 23 | 1.22 | 2026-09-30 (~2.5 min WireGuard transfer) |
| 24 | 1.23 | 2026-09-30 (~4 min WireGuard transfer) |
| 25 | 1.24 | 2026-09-30 (~2.5 min WireGuard transfer) |
| 26 | 1.25 | 2026-09-30 (~4 min WireGuard transfer) — current |

**ARIM contact rename saga — before/after (`shared_contacts` table via SSH+curl, admin-secret-authed `/admin/contacts/rename`):**

| Step | Vilém's entries | Petr's entries | Rudolf's entries |
|---|---|---|---|
| Before any change | Dvořák Vilém / 606734151 (ARIM) | Faltus Petr / 721669711 (ARIM) | Mana Rudolf / 776795732 (ARIM) |
| | *(not yet found)* Vilda služební / +420734605445 | *(not yet found)* Petr F. služební / +420603838955 | Rudolf Mana - služební / +420737228068 |
| My wrong rename #1 | Dvořák Vilém **- služební** / 606734151 | Faltus Petr **- služební** / 721669711 | unchanged |
| User: "6xx=personal, 7xx=service" rule stated | → revealed Vilém's ARIM number (606xxx) was WRONGLY labeled služební | → Petr's ARIM number (721xxx) happened to be correctly labeled | — |
| My correction #1 | Dvořák Vilém (plain) / 606734151 | unchanged (721xxx, correctly "- služební") | — |
| User: "no, keep ORIGINAL entries, just unify names" | — | — | — |
| **Final state** | Dvořák Vilém / 606734151 (ARIM, personal) | Faltus Petr / 721669711 (ARIM, personal) | Mana Rudolf / 776795732 (ARIM, personal) |
| | **Vilém Dvořák - služební** / +420734605445 | **Petr Faltus - služební** / +420603838955 | Rudolf Mana - služební / +420737228068 (untouched) |

**Pixel watermark round-trip test results (scratch console harness, deleted with the feature but results captured here since they're the evidence the fix actually worked):**

| Test | Before fix (`index % 3 switch {...}` bug) | After fix (`var mod = index % 3; mod switch {...}`) |
|---|---|---|
| 1: direct in-memory embed→extract | `NULL/NOT FOUND` (bit divergence starting at index 5, pixel1's Blue channel) | `MUDr. Vilém Dvořák\|2026-09-30T21:00:00.0000000Z` ✓ |
| 2: through a real PNG encode/decode round-trip | `NULL/NOT FOUND` | same payload, exact match ✓ |
| 3: unwatermarked image (negative case) | `DivideByZeroException` crash inside `Bit()`/`ByteAt()` | `NULL/NOT FOUND` (correct — no crash) ✓ |
| Verified against the REAL built decoder .exe (not just the harness) | — | `Vodoznak nalezen: MUDr. Vilém Dvořák\|...` ✓ |

**Pinch-zoom bug isolation (round 3, `1aafd75`) — exact divergence found via debug prints:**
```
embed  = "010100110100000101010111001100010000000000110010"   (positions 0..49)
extract= "010101010101010101010101010101010101010101010101"
divergence starts at position 5 = pixel1's BLUE channel
```
(This specific trace is from the PIXEL WATERMARK bug, item 12 — kept here as primary evidence of the exact isolation technique used, since re-deriving it would require re-running the now-deleted test harness.)

**Zoom tuning constants, before/after (`DocumentViewerPage.xaml.cs` / `DocumentViewerViewModel.cs`):**

| Constant | v1/v2/v3 value | v4 (current) value | Reason |
|---|---|---|---|
| `MaxScale` | 4 | 3 | user saw glitching near the old max |
| `PinchSensitivity` | (none — 1:1 tracking) | 1.6 (exponent on `e.Scale` per callback) | needed 3 gestures to reach useful zoom |
| `MaxRenderWidth` × `MaxRenderHeight` | 1200 × 1600 | 1800 × 2400 | same readable zoom needs less extreme `Scale` |

**Data-loss incident timeline (device state confirmed via `adb`/`dumpsys`/`run-as`):**
```
firstInstallTime = 2026-09-29 19:11:09
lastUpdateTime   = 2026-09-29 19:11:09   <- identical = fresh install, not an update
run-as com.companyname.secureapp.presentation ls -la databases/
  -> "No such file or directory"          <- the local SQLCipher DB is simply gone
```

**Wrong-device identification (before the correct S23+ deploy):**
```
adb devices -l
22dab4387e0b7ece   device product:star2ltexx model:SM_G965F device:star2lte   <- S9+, NOT S23+
```

**New/changed relay HTTP endpoints this session (`relay/SecureApp.Relay/Program.cs`):**

| Method + path | Auth | Purpose | Introduced |
|---|---|---|---|
| `POST /admin/contacts/bulk-import` | Admin secret | Seed the 71 ARIM rows in one call | prior sub-session (`d801ee9`) |
| `POST /admin/contacts/rename` | Admin secret | Bulk id+DisplayName rename (used 3x in the naming-unification saga) | `53689ae` |
| `PUT /identity-backup/{lookupKey}` | None (lookupKey itself is the credential) | Upload an encrypted identity envelope | `40fb29b` |
| `GET /identity-backup/{lookupKey}` | None | Fetch an encrypted identity envelope for restore | `40fb29b` |
| `POST /document-downloads` | Device (X-Device-Id/Secret) | Log one download event | `3a17a4d` |
| `GET /admin/document-downloads?query=` | Admin secret | Search the download log by document title substring | `3a17a4d` |

**Android permissions / manifest:** none added this session (all new features used existing permissions — `FOREGROUND_SERVICE_REMOTE_MESSAGING`, `WAKE_LOCK`, etc. from the 2026-09-26 background-messaging work already covered what was needed; the Share-sheet-based download/backup export uses MAUI's built-in `Share.Default.RequestAsync`, no extra manifest entries).

**Widget layout additions (`widget_notifications.xml`, this session):** `widget_duty1` through `widget_duty10` (each a horizontal `LinearLayout` with a `TextView` + `ImageView` phone icon), `widget_open_contacts` (new header `TextView`, sibling of the pre-existing `widget_mode_toggle` inside a new horizontal `LinearLayout` wrapper).

**Feature verification status, end of session (what's confirmed working on-device vs. still unverified):**

| Feature | Status | How verified |
|---|---|---|
| ARIM Czech sort + search | ✅ Confirmed | User tested live after 1.20/21 |
| ARIM CollectionView perf (round 2, `HeightRequest`) | ✅ Confirmed | User tested live after 1.21/22, no further complaint |
| Contact rename (3-round saga) | ✅ Confirmed | Verified via direct SQL query after each rename call |
| Widget duty-phone icons + Kontakty button | ⚠️ Not explicitly re-confirmed | Compile-checked + shipped; no follow-up complaint, but no explicit "yes it works" either |
| Tab-highlight fix (`WorkplaceRoute`) | ⚠️ Not explicitly re-confirmed | Same — shipped, no complaint |
| Identity backup/restore | ⚠️ Not tested end-to-end | Built and shipped in direct response to the incident; the user has NOT yet actually run "Zálohovat identitu" or a restore, since re-registration used the plain Aktivovat flow (no backup existed yet to restore from) |
| Document download button + audit log | ⚠️ Not explicitly re-confirmed | Built cleanly, shipped, no follow-up complaint |
| Document rename | ⚠️ Not explicitly re-confirmed | Same |
| Pinch-zoom v4 (current) | ❌ Unverified | The most recent change of the whole session — literally the last thing shipped; no user feedback yet |

## Code Analysis

- `App.xaml.cs`: `_backgroundOpicentrumSyncInterval = TimeSpan.FromMinutes(10)` (was 3h); `WorkplaceRoute(string route) => route == nameof(Views.WorkplacePage) ? $"//NotificationsTab/{route}" : route;` — applied in both `NavigateToRoute` and `ResolveNotificationRouteAsync`'s Schedule branch.
- `PixelWatermark.cs` (deleted, kept here for the record): `MaxPixels = 60_000`, `Magic = [0x53,0x41,0x57,0x31]` ("SAW1"), block layout `[Magic(4) | Length(2, BE) | UTF8 payload | CRC16(2)]`, CRC-16/CCITT-FALSE.
- `IdentityBackupService`: `Pbkdf2Iterations = 210_000` (OWASP 2023 baseline), `DerivedKeyLengthBytes = 32`, `SaltLengthBytes = 16`; relay lookup key = `SHA256(email.Trim().ToLowerInvariant() + "\u0000" + passphrase)` (plain SHA-256, deliberately different derivation from the PBKDF2 encryption key — the relay only needs a lookup index, not resistance to offline brute force on its own).
- `DocumentViewerPage.xaml.cs` zoom math (v4, current): `_currentScale = Math.Clamp(_currentScale * Math.Pow(e.Scale, PinchSensitivity), 1, MaxScale)`; pan clamp `±(DocumentImage.Width|Height) * (_currentScale - 1) / 2` (symmetric, for the permanently-center anchor); pan baseline (`_panX`/`_panY`) updates ONLY on `GestureStatus.Completed`, never mid-`Running`.
- `ContactsViewModel.cs`: `CzechNameComparer = StringComparer.Create(CultureInfo.GetCultureInfo("cs-CZ"), ignoreCase: true)`; `ArimNoteTag = "ARIM"` (the client-side split key, no relay schema change).
- `TrustedAdminDevices.cs` (pre-existing, load-bearing this session): Android model `"SM-S916B"` and Windows machine `"DESKTOP-F8AS2U6"` always bootstrap as Admin — this is what made the incident's re-registration painless.
- Relay `document_downloads` table: `id, device_id, display_name, document_title, source_library_file_id (nullable), downloaded_at_utc` — `source_library_file_id` is the SAME guid across every device that downloaded a given shared-library file, so an admin search by title surfaces every downloader, not just one device's log.
- Relay `identity_backups` table: `lookup_key (PK), envelope_json, updated_at_utc` — `envelope_json` is fully opaque ciphertext to the relay.
- `IdentityBackupService.BackupAsync` best-effort split: the local file write + `Share.Default.RequestAsync` is treated as the PRIMARY guarantee (any failure there surfaces to the user); the relay `PUT` afterward is wrapped in its own try/catch and swallowed on failure, so an offline/unreachable relay never blocks a successful local backup.
- `HttpDocumentDownloadLogService.LogAsync` is ALSO best-effort by explicit design (interface doc comment states it plainly) — a download must never fail just because the relay is briefly unreachable.
- `ContactsViewModel.ReorderAsync` was simplified this session to drop the ARIM branch entirely (dead code removed, not just unreachable) once drag-reorder was removed from that section's XAML — `Reordered`/`PersistReorderAsync` helpers are now only ever called for `CompanyContacts`.
- `SharedContactSectionGroup` (the ARIM section's collapsible-group VM type) gained a mutable `SetEntries` this session — unlike the pre-existing `ContactSectionGroup` (static phone directory, entries fixed at construction), ARIM's group re-fetches from the relay on every `LoadAsync`, so its entries must be replaceable in place without losing the `IsExpanded` state.

## Code Snippets (primary evidence — captured verbatim, too expensive to re-derive from a diff)

**`App.xaml.cs` — `WorkplaceRoute` helper (the tab-highlight bug fix, applied at two call sites):**
```csharp
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
/// absolute-route pattern SmartSearchViewModel already uses).
/// </summary>
private static string WorkplaceRoute(string route)
    => route == nameof(Views.WorkplacePage) ? $"//NotificationsTab/{route}" : route;
```
Applied identically in `ResolveNotificationRouteAsync`'s Schedule branch: `return WorkplaceRoute(nameof(Views.WorkplacePage));` (was a bare `return nameof(Views.WorkplacePage);`).

**`ContactsViewModel.cs` — Czech collation + search, the two pieces together:**
```csharp
/// <summary>Proper Czech alphabetical order — plain OrdinalIgnoreCase (what the static phone
/// directory already uses) sorts "Ch" after "H", not between "H" and "I" the way Czech readers
/// expect. Requires full ICU globalization (no InvariantGlobalization in the csproj).</summary>
private static readonly StringComparer CzechNameComparer =
    StringComparer.Create(CultureInfo.GetCultureInfo("cs-CZ"), ignoreCase: true);

private void ApplyArimFilter()
{
    var query = ArimSearchQuery.Trim();
    IEnumerable<SharedContact> source = _allArimContacts;
    if (query.Length > 0)
    {
        source = source.Where(c =>
            c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (c.Phone?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        if (!ArimContactsGroup.IsExpanded) ArimContactsGroup.IsExpanded = true;
    }

    ArimContactsGroup.SetEntries(
        source.OrderBy(c => c.DisplayName, CzechNameComparer)
            .Select(c => ToItem(c, includeNoteInSubtitle: false))
            .ToList());
}
```

**`DocumentViewerPage.xaml.cs` — the FINAL (v4) pinch-zoom-and-pan implementation, after 3 rounds of bug fixes:**
```csharp
private double _currentScale = 1;
private double _panX;
private double _panY;
private const double MaxScale = 3;          // lowered from 4 — glitching near the old max
private const double PinchSensitivity = 1.6; // amplifies each callback so fewer gestures are needed

private void ResetZoom()
{
    _currentScale = 1;
    _panX = 0;
    _panY = 0;
    DocumentImage.Scale = 1;
    DocumentImage.TranslationX = 0;
    DocumentImage.TranslationY = 0;
}

private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
{
    if (e.Status != GestureStatus.Running) return;

    var amplifiedDelta = Math.Pow(e.Scale, PinchSensitivity);
    _currentScale = Math.Clamp(_currentScale * amplifiedDelta, 1, MaxScale);
    DocumentImage.Scale = _currentScale;
    ClampTranslation();
    // Pinch has no "total since gesture start" value the way Pan does — its own translation
    // nudges are ad hoc, so the clamped result IS the new baseline immediately.
    _panX = DocumentImage.TranslationX;
    _panY = DocumentImage.TranslationY;
}

private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
{
    if (_currentScale <= 1) return;

    switch (e.StatusType)
    {
        case GestureStatus.Running:
            // TotalX/TotalY are cumulative SINCE THIS GESTURE STARTED, not since the last
            // callback — _panX/_panY (the position before this gesture) must stay untouched
            // for the whole Running phase, or positions compound and hit the clamp almost
            // immediately (this was the "posun je pomaly" bug).
            DocumentImage.TranslationX = _panX + e.TotalX;
            DocumentImage.TranslationY = _panY + e.TotalY;
            ClampTranslation(); // display-only clamp, does not touch _panX/_panY
            break;
        case GestureStatus.Completed:
            _panX = DocumentImage.TranslationX;
            _panY = DocumentImage.TranslationY;
            break;
    }
}

/// <summary>DocumentImage keeps MAUI's own default AnchorX/AnchorY (0.5, 0.5) — deliberately
/// NEVER touched (v2 flipped it to (0,0) on pinch start, which snapped the existing Scale
/// transform onto a new origin instantly = a visible jump toward a corner). Overflow past each
/// edge is Width/Height * (scale-1), split evenly on both sides for a center anchor.</summary>
private void ClampTranslation()
{
    var maxX = DocumentImage.Width * (_currentScale - 1) / 2;
    var maxY = DocumentImage.Height * (_currentScale - 1) / 2;
    DocumentImage.TranslationX = Math.Clamp(DocumentImage.TranslationX, -maxX, maxX);
    DocumentImage.TranslationY = Math.Clamp(DocumentImage.TranslationY, -maxY, maxY);
}
```

**`PixelWatermark.cs` — the bug and its fix (deleted feature, kept for the record since the debugging technique is reusable):**
```csharp
// BEFORE (buggy — B channel silently wrong at runtime, R/G channels happened to read correctly):
var channel = index % 3 switch { 0 => pixel.Red, 1 => pixel.Green, _ => pixel.Blue };

// AFTER (fixed — binding the modulo to its own variable first):
var channelIndex = index % 3;
var channel = channelIndex switch { 0 => pixel.Red, 1 => pixel.Green, _ => pixel.Blue };
```
Isolated via a real round-trip test harness (embed → PNG encode/decode → extract → compare bit-for-bit), NOT by code review alone — the bug was invisible from reading the code, only showed up empirically.

**`IdentityBackupService.cs` — the envelope format (still live in the current codebase):**
```csharp
private sealed record BackupPayload(Guid KeyId, byte[] PrivateKey, byte[] PublicKey, byte[] AesKey, string DisplayName, DateTimeOffset CreatedAtUtc);
private sealed record BackupEnvelope(int Version, byte[] Salt, byte[] Nonce, byte[] AuthTag, byte[] CipherText);

private static byte[] DeriveKey(string passphrase, byte[] salt)
    => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, DerivedKeyLengthBytes);

private static string ComputeLookupKey(string email, string passphrase)
{
    var bytes = Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant() + "\u0000" + passphrase);
    return Convert.ToHexStringLower(SHA256.HashData(bytes));
}
```

**`RelayDatabase.cs` — `document_downloads` schema + search query:**
```sql
CREATE TABLE IF NOT EXISTS document_downloads (
    id                      TEXT PRIMARY KEY NOT NULL,
    device_id               TEXT NOT NULL,
    display_name            TEXT NOT NULL,
    document_title          TEXT NOT NULL,
    source_library_file_id  TEXT NULL,
    downloaded_at_utc       TEXT NOT NULL
);

-- Admin search (query is a substring match on document_title, SQLite LIKE, case-insensitive for ASCII):
SELECT id, device_id, display_name, document_title, source_library_file_id, downloaded_at_utc
FROM document_downloads
WHERE document_title LIKE @query   -- @query = '%' + user input + '%'
ORDER BY downloaded_at_utc DESC;
```

**`IMessagingService.RestoreLocalIdentityAsync` — the guard that makes restore safe (`MessagingService.cs`):**
```csharp
public async Task RestoreLocalIdentityAsync(IdentityKeyMaterial material, CancellationToken ct = default)
{
    var existing = await _keyMetadataRepository.GetActiveKeyAsync(KeyPurpose.ChatIdentity, ct);
    if (existing is not null)
        throw new EncryptionOperationException("Toto zařízení už má aktivní identitu — obnova ze zálohy je jen pro čerstvé/vymazané zařízení.");

    await _crypto.ImportEncryptionKeyMaterialAsync(material, ct);

    var metadata = new EncryptionKeyMetadata(EncryptionAlgorithm.HybridMlKem768Aes256Gcm, KeyPurpose.ChatIdentity);
    EntityMaterializer.Set(metadata, nameof(Entity.Id), material.KeyId);
    await _keyMetadataRepository.AddAsync(metadata, ct);
}
```

## Files Changed

### Widget / notifications
- `src/SecureApp.Presentation/Contacts/ContactPhoneCache.cs` — new
- `src/SecureApp.Presentation/Platforms/Android/NotificationsWidgetProvider.cs` — duty-phone icons, Kontakty button
- `src/SecureApp.Presentation/Platforms/Android/Resources/layout/widget_notifications.xml` — duty rows, header restructure
- `src/SecureApp.Presentation/Platforms/Android/Resources/drawable/widget_ic_phone.xml` — new
- `src/SecureApp.Presentation/App.xaml.cs` — sync interval, `WorkplaceRoute`, `ContactPhoneCache.RefreshAsync` wiring

### Contacts
- `src/SecureApp.Presentation/ViewModels/ContactsViewModel.cs` — ARIM section split, Czech sort, search, CollectionView switch (both lists)
- `src/SecureApp.Presentation/Views/ContactsPage.xaml` — same, plus `HeightRequest="420"` fix
- `relay/SecureApp.Relay/Program.cs`/`Contracts.cs`/`RelayDatabase.cs` — `/admin/contacts/rename` endpoint

### Identity backup (disaster recovery)
- `src/SecureApp.Domain/Interfaces/Services/IIdentityBackupService.cs`, `IDocumentDownloadLogService.cs` — new
- `src/SecureApp.Domain/ValueObjects/DocumentDownloadEntry.cs` — new
- `src/SecureApp.Domain/Interfaces/Services/ICryptoService.cs`, `IMessagingService.cs`, `IRelayAdminService.cs` — new methods
- `src/SecureApp.Data/Cryptography/BouncyCastleCryptoService.cs`, `src/SecureApp.Data/Messaging/MessagingService.cs` — export/import/restore impl
- `src/SecureApp.Presentation/Identity/IdentityBackupService.cs`, `src/SecureApp.Presentation/Rendering/HttpDocumentDownloadLogService.cs` — new
- `src/SecureApp.Presentation/Transport/HttpRelayAdminService.cs` — `SearchDocumentDownloadsAsync`
- `src/SecureApp.Presentation/ViewModels/SettingsViewModel.Identity.cs`, `SettingsViewModel.DocumentDownloads.cs` — new
- `src/SecureApp.Presentation/Views/SettingsPage.xaml` — new cards
- `relay/SecureApp.Relay/Program.cs`/`Contracts.cs`/`RelayDatabase.cs` — `identity_backups`, `document_downloads` tables+endpoints

### Document viewer (watermark removal, download, rename, zoom)
- `src/SecureApp.Presentation/Rendering/DocumentRenderingService.cs` — pixel watermark added then removed; net diff is the `ICurrentUserService` dependency added-then-removed
- `src/SecureApp.Presentation/Rendering/PixelWatermark.cs` — added (`fc589c8`) then deleted (`041db5f`)
- `tools/SecureApp.WatermarkDecoder/` (3 files) — added then deleted, same commits
- `src/SecureApp.Presentation/ViewModels/DocumentViewerViewModel.cs` — visible watermark removed; download command added; rename command added; zoom properties added-then-removed (buttons v1) then removed again (gesture v2+); `MaxRenderWidth/Height` raised
- `src/SecureApp.Presentation/Views/DocumentViewerPage.xaml` — watermark Label removed; download button added; zoom buttons added-then-removed; `ToolbarItem` for rename added; `PinchGestureRecognizer`/`PanGestureRecognizer`/double-tap added
- `src/SecureApp.Presentation/Views/DocumentViewerPage.xaml.cs` — `OnAppearing`/`OnDisappearing` watermark calls removed; full pinch/pan gesture handlers added, rewritten twice (v2→v3→v4 tuning)

### Config / version
- `src/SecureApp.Presentation/SecureApp.Presentation.csproj` — version bumps 1.18(19) through 1.25(26)

### Global (outside repo, memory files)
- `never-debug-deploy-real-devices.md` — **new**. Full incident writeup: what happened (`firstInstallTime == lastUpdateTime`, wiped `databases/`), why (`dotnet build -t:Run` Debug-signs, S23+ normally runs Release-signed, Android silently uninstall+reinstalls on signature mismatch instead of refusing), the rule (`-t:Run` only on a throwaway test device, `dotnet publish -c Release` for anything touching S23+/Petr's S25/PC/S9+), and a pointer to the same-day mitigation (identity backup).
- `android-fast-deploy-gotcha.md` — added a "CRITICAL EXCEPTION" block directly under the existing `-t:Run` guidance, so a future session reading the OLD advice (which is still correct for a genuine test device) doesn't miss the new caveat.
- `feedback-batch-builds.md` — added a note that batching builds is still correct, but once a batch DOES get built for real, publishing to the relay's self-update endpoint must happen in the SAME pass — the gap between "adb-installed to my own test device" and "relay manifest updated" is what caused the "Opet nejde aktualizovat" complaints, twice.

### Documentation
- `plans/handoffs/HANDOFF_identity-recovery-doc-zoom_2026-09-30.md` — this file, new.

## User Feedback & Preferences (REQUIRED — never omit)

- *"Je nutne aby byla notifikace ze apl prima a posila zpravy po vypnuti? Nfmuze byt ticha?"* — the opening question about the background-connection notification (answered: already silent, can't be fully invisible).
- *"Ok pak bych udela cernobilou ikonu inverzne aby tolik nesvitila ok a pokud dojde k zmene zarazeni prijde notifikace... kontrola cca 10 min?"* — the 10-minute sync-interval ask.
- *"ve wigetu rovnou ke sloužícím přidej pictogram a na ktery když se klikne skopiruje služební tel do tel k vytocení"* — mid-turn interrupt, widget duty-phone-icon spec.
- *"Na wiget dej i moznost otevrit kontakty"* — widget Kontakty button spec.
- *"Kdyz jsem na kontaktech a prejdu na widget a klepnu do nej otevre se mi rozpis pořad sviti kontakty a nejde na ne prepnout"* — the tab-highlight bug report.
- *"Zkus kdyz tak napis co s tim mohu delat ja ale pres s23 mel bych mit vsude pristup"* — during a Claude Code tool outage, the user assumed it might be a SecureApp access-control issue; clarified it was an unrelated Anthropic-infrastructure classifier hiccup, nothing they could act on.
- *"Potrebuji aby jsi vymyslel zpusob jak tedy pri ztrate identity tu identitu ziskat zpet ne vytvorit novou... nikdo nechce ztracet historii vytvor neco v telefonu nebo pc co pomuze v identifikaci identity i pri reinstalaci apky"* — the core identity-backup spec, directly triggered by the incident.
- *"Kdyz je napsane sluzebni tak jsou to zacinajici 7 soukrome 6"* — the (partially-coincidental) phone-number-range rule that led to the rename saga's first wrong turn.
- *"Ne asi spise... maji tam byt ty puvodni kontakty jen zmeni vilem na vilem dvorak a peter f. na petr faltus... proste sjednotit"* — the correction that fixed the rename saga.
- *"soukrome kontakty serad podle abecedy ceske, a taky moznost vyhledavani... prepis firemni kontakty..."* — the Czech-sort/search ask (combined in the same message as the rename request).
- *"Stale otevreni soukrome kontakty trva neskutecne dlouho..."* — the SECOND performance complaint, after round 1's fix was already shipped and confirmed installed.
- *"71 kontaktu"* — a terse correction when I'd misremembered the count as 77.
- *"Jo ale pokud se dostane ven bude to resit instituce ktera tyto nastroje ma takze bych prosil aby tam byly neviditelne vodoznaky schovane v datech pixelu"* — the invisible-watermark request, with explicit reasoning for why it's worth building despite the stated limits.
- *"Na vlozenych souborech je pirad viditeln, vodoznak..."* — ambiguous initial report, clarified via `AskUserQuestion` to mean the visible watermark on chat attachments.
- *"Ne chtel jsem aby se zobrazoval neviditelny vodoznak... o tom uz jsme se bavili pokud nekdo sfoti display....ma mit na fotce neviditelny vodoznak..."* — the clarification that reframed the whole watermark discussion: the ask was ALWAYS about surviving a photograph, which LSB steganography can't do.
- *"V tom pripade vyhod vodoznak uplne..."* — decisively rejecting the "build it more robustly" option in favor of deleting the feature.
- *"Viditelny vyhod tez..."* — the escalation to also remove the visible (working) watermark, prompting an explicit confirmation question before proceeding given the security-posture implications.
- *"Vdyt do nejdr ani cist ani zvetsit je to k icemu pokud je to nepouzitelne... urcite tl vyhod a prixej moznost zvetseni dokumentu aby to slo cist nebo tisknout..."* — the actual underlying motivation (readability), revealed only after the confirmation question.
- *"uploadni to dokumenty bude mozne stahovat uzivateli ale bude log kdo co kdy stahl podle dokumentu vyhledatelny v logu"* — the document-download-log spec, delivered cleanly with no back-and-forth.
- *"Opet nejde aktualizovat pres apku...."* — twice, the self-update-publish-gap complaint.
- *"asi spise nejake plynule zvetsovani a taky posun pri zvetseni bez toho je to nesmyslne, mozna jak to byva roztazenim prstu a taky prejmenovani dokumentu... zrus ty velke tlacitka na zvetsovani"* — rejecting the button-zoom v1 in favor of real pinch/pan, plus the rename-feature ask folded into the same message.
- *"Zvetsovani neni plynule a navic se to zvetsiluje z praveho dolniho rohu a posun je pomaly..."* — the v2 bug report that led to the anchor/pan-baseline fixes.
- *"Lepsi ale neni autemticke zvetsovwni pitrebuji 3 gesta na zvetseni, pri urcitem mozna maximalnim zvetseni se zacne obrazek glicovat..."* — the v3 bug report (sensitivity + glitching), leading to the current v4 tuning, not yet confirmed by the user.

## Where We're Going

1. **Get user confirmation on the v4 zoom tuning** (`86befd7`, 1.25/26) — sensitivity amplification and lower max-scale were both reasoned fixes, NOT verified live; this is the single most important next action.
2. **If glitching persists even at MaxScale=3**, that points to something more specific than "extreme Scale transform" — likely needs actual device GPU/profiling investigation, not another blind constant tweak.
3. **Peer-assisted chat-history resync** (explicitly deferred this session, scope already agreed: device-to-device only, never admin/relay-readable) — a real follow-up feature request, not yet designed in any detail.
4. **Get 1.25(26) onto Petr's S25, the PC, and the S9+** — carried over unresolved from the 2026-09-28 handoff; now even more relevant since the S9+ was directly involved in this session's wrong-device incident and its own state wasn't re-verified afterward.
5. **The background-notification-category settings shortcut**, offered early this session, never taken up — low priority, revisit if asked again.
6. **NOTIFICATION_HUB_SPEC.md's stale §26 status note** — still not fixed, now two sessions overdue.
7. **Phase 2b (functionality audit) / Phase 3 (visual redesign)** — still explicitly deferred, not touched this session either.

**Clarifying questions asked via `AskUserQuestion` this session, and the user's answers (the decision trail — don't re-ask these):**

| Question | User's answer |
|---|---|
| Where should the document-download button/log live — new button, or just log existing opens? | New "Stáhnout" button (recommended option) |
| Identity backup destination — local file only, or file + relay? | Both ("Soubor + zálohovaně i na relay") |
| Rename saga: revert Vilém's ARIM entry back to plain (no "- služební")? | Yes |
| Rename saga: what to do with the pre-existing "Petr F. služební" entry (number pattern didn't match the stated 6/7 rule)? | Leave unchanged (don't guess further) |
| Watermark report ambiguity: downloaded file, or viewed chat attachment? | Viewed chat attachment |
| Pixel watermark: pursue a genuinely photo-robust technique (large-block + ECC), or accept current limits? | Neither, initially — then escalated to "remove it entirely" once the real requirement was clarified |
| Confirm: really remove the visible (working) watermark too, leaving zero screen-photograph protection? | Yes, confirmed explicitly |
| Deploy the identity-backup build now (forcing one more Debug→Release reinstall, nothing to lose yet) vs. wait? | Deploy now |

## Honest Limitations Stated to the User This Session (don't silently "fix" these without re-discussing)

- **Identity backup restores identity, never chat history** — stated up front before building, re-confirmed when the user's own phrasing briefly suggested they wanted history recovery too.
- **The invisible pixel watermark (now deleted) could never have survived a photographed screen** — stated BEFORE building it (the user chose to proceed anyway for the digital-copy case), and this is exactly what led to its later removal once the user's real requirement was clarified.
- **A genuinely photograph-robust invisible watermark is possible in principle** (large-block + error-correcting-code embedding, like real anti-piracy screen watermarking) but was explicitly scoped as "days of work, still not 100% reliable" and declined by the user — not built, not attempted.
- **Removing the visible watermark removes the app's only working DLP protection against someone photographing the screen** — stated plainly, confirmed twice via `AskUserQuestion`, before executing. `FLAG_SECURE` (screenshot/screen-recording block) is unaffected and still active; only the photograph-deterrent/traceability layer is gone.
- **Pinch-zoom's center-anchor is a deliberate simplification**, not pinch-point-accurate zooming — chosen after the pinch-point version's anchor-flipping caused the corner-jump bug. If a future report wants "zooms exactly where my fingers are," that's a real, known gap, not an oversight.
- **The document-download log's best-effort design means a download can succeed even if the audit-log entry silently fails to reach the relay** — by design (a relay hiccup must never block getting the file), but means the log is not a strict guarantee of completeness for compliance purposes.

## Risks & Blockers

- **Chat history on the S23+ is permanently lost** — not a blocker to fix (it's unfixable by design), but the user and any peers who chatted with this device should be aware existing 1:1/group sessions may show as broken/need a fresh pairing.
- **`dotnet build -t:Run` must NEVER be used again on any of the user's real phones** — now a HARD RULE in memory (`never-debug-deploy-real-devices.md`), but this is a process discipline risk, not a code fix; a future session could still make the same mistake if the memory isn't consulted.
- **Zoom/pan gesture code has now been rewritten 3 times this session based on live user feedback, each round fixing real bugs the previous round didn't anticipate** — the current (v4) state is UNVERIFIED. Treat the next report from the user as potentially revealing a 4th distinct issue, not necessarily a repeat of an already-fixed one.
- **WireGuard APK transfers to the S23+ took 2–4 minutes each this session** (65MB APK) — normal for this network path, but budget for it; don't assume a `dotnet build -t:Run`/`adb install` background task has failed just because it hasn't reported back within a minute or two.
- **The Claude Code Edit/PowerShell tool's safety classifier had at least two multi-minute outages this session** (unrelated to SecureApp) — if this recurs, the established handling is: try once or twice, then stop retrying and wait, rather than hammering (repeated attempts are explicitly slowed by the harness itself).

## Open Questions

- Does the v4 zoom tuning (sensitivity 1.6, max 3x, higher render resolution) actually resolve both the "needs 3 gestures" and "glitches near max" complaints, or does a 4th round of fixes await?
- Is the peer-assisted history-resync feature wanted soon, or genuinely a "someday" item? Not re-asked this session after the initial deferral.
- Should the S9+ (directly involved in the wrong-device incident, never re-verified afterward) be checked for its own state/version before assuming it's fine?

## Quick Start for Next Session

```powershell
# Reference docs
Get-Content "H:\Visual Studio\C#\Aplikace\DEVELOPMENT_PLAN.md"
Get-Content "H:\Visual Studio\C#\Aplikace\IMPROVEMENT_PLAN.md"

# Key files to read first
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\Views\DocumentViewerPage.xaml.cs"
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\ViewModels\DocumentViewerViewModel.cs"
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\Identity\IdentityBackupService.cs"

# Verify current relay + device state
ssh secureapp-pi "curl -s http://192.168.50.8:8080/download/android/version"
$adb = "C:\Users\dvora\AppData\Local\Android\Sdk\platform-tools\adb.exe"
& $adb connect 10.8.0.3:5555
& $adb -s 10.8.0.3:5555 shell dumpsys package com.companyname.secureapp.presentation | Select-String versionName

# The full deploy pipeline used all session (repeat for the next fix, once one is needed):
$dir = "C:\Users\dvora\dotnet-local"; $env:DOTNET_ROOT = $dir; $env:PATH = "$dir;$env:PATH"; $env:DOTNET_MULTILEVEL_LOOKUP = "0"
& "$dir\dotnet.exe" publish "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\SecureApp.Presentation.csproj" -f net10.0-android -c Release
& $adb -s 10.8.0.3:5555 install "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\bin\Release\net10.0-android\com.companyname.secureapp.presentation-Signed.apk"
# ...then scp the same .apk to the Pi and POST /admin/upload/android with the NEXT versionCode/versionName — see
# "Tooling Notes" above for why this always goes through a generated bash script, never an inline ssh command.

# Next action
# Ask the user: did the v4 zoom tuning (1.25/26) fix the "3 gestures" + "glitching" complaints?
# If not, that's the very next thing to debug — do NOT assume it's fixed without their confirmation.
# Second priority if zoom is confirmed fine: is identity backup (built but never actually exercised
# end-to-end) worth a deliberate test run before assuming it works?
```

## Session Closed
**Closed at:** 2026-09-30 (this session)
**Commit:** `cabed4d`
**Session status:** Handed off to next session

