# SecureApp: finished the 10-08 WIP (press-and-hold real name in chat list, admin device list grouped by person), shipped release 1.56 (59) incl. relay deploy, renamed a shared contact

**Date:** 2026-10-09
**Status:** COMPLETED (shipped) — one live-verification item outstanding (Android press-and-hold gesture never seen on a device)
**Bead(s):** none (`bd` not installed on this machine)
**Epic:** none
**Chain:** `standalone-be860f68` seq `9`
**Parent:** `plans/handoffs/HANDOFF_standalone-be860f68_crash-fixes-wireguard-onboarding_2026-10-07.md` (seq 8)
**Prior chain:** `HANDOFF_identity-recovery-doc-zoom_2026-09-30.md` > `HANDOFF_zoom-fix-library-workflow_2026-10-01.md` > `HANDOFF_library-redesign-subcategories_2026-10-02.md` > `HANDOFF_standalone-be860f68_library-video-gcs-tools_2026-10-03.md` (seq 4) > `HANDOFF_standalone-be860f68_murray-das-chat-pairing_2026-10-04.md` (seq 5) > `HANDOFF_standalone-be860f68_widget-wireguard-multiprofile_2026-10-05.md` (seq 6) > `HANDOFF_standalone-be860f68_ai-translation-wireguard-fix_2026-10-06.md` (seq 7) > `HANDOFF_standalone-be860f68_crash-fixes-wireguard-onboarding_2026-10-07.md` (seq 8) > this (seq 9)

**Gap in the chain:** a whole session ran on **2026-10-08** (Claude Sonnet 5, session `015G2pcq4NXtVJUb4rntGBsw`) and wrote NO handoff. Its work is only recorded in 3 commit messages (`a4b4707`, `a26bd6c`, `7a5c2ef`) — summarized under "Since Last Handoff" below so it isn't lost.

---

## Since Last Handoff

Parent (seq 8, 10-07) "Where We're Going" vs what actually happened across 10-08 + 10-09:

1. **Live-verify onboarding with a real 2nd PC + non-admin person** — not done. BUT on 10-08 the user pushed back on the UX itself ("nobody will type a 24-char hex code into a terminal"), and the pickup code was replaced by a **single clickable link**: `GET /download/onboarding/{code}` peeks validity (`RelayDatabase.IsWireGuardPickupCodeValid`, read-only, never consumes) and serves `new-pc-onboarding.bat` with the code baked in. Settings shows/copies `WireGuardPickupLink` instead of the bare code (`a4b4707`, release 1.54/57). The `[RelayCommand]` kept its old name `CopyWireGuardPickupCodeCommand` (still bound at `SettingsPage.xaml:668`) and the property `WireGuardPickupCode` still exists alongside the link.
2. **Crash class came back on 10-08** — `ObjectDisposedException` on the Kontakty search `Entry`, user's S23+, app 1.54(57), with NO `GuardIsFocused` frame in the stack. Fix `a26bd6c` (release 1.55/58): `[MethodImpl(MethodImplOptions.NoInlining)]` on `InputFocusCrashGuard.GuardIsFocused` + catch widened from `ObjectDisposedException` to `Exception`. Theory (unproven): Android Release AOT inlined the generic method and lost its EH region for the `IEntry` instantiation. **Not re-reported since** — no evidence either way this session (no `device_app_logs` query run today).
3. **New feature thread on 10-08 (not in parent's plan at all), user-driven:** profile/ARIM reconciliation + contact RBAC + directory platform/person-link — WIP commit `7a5c2ef`, explicitly left with two open items "for tomorrow". **This session finished both and shipped it.**
4. **Translation `#if WINDOWS` gate live test, design K/L/M decision, 6-item carry-over backlog, documentation catch-up** — all still untouched (4th session running for the backlog/docs). Doc debt is now FIVE subsystems deep (adds: profile/ARIM reconciliation + RBAC EditContact + directory platform/person-link + press-and-hold reveal).
5. **Process fix proposed by parent ("bump build number on every distinct binary")** — honored this session: exactly one binary, one version (1.56/59).
6. **Stale `adb-wireless-technique` memory** — still not corrected (no adb use this session).

## Reference Documents

- No `*BIBLE*`, no `CLAUDE.md` (checked).
- `relay/ops/README.md` — de facto ops runbook (relay deploy pipeline, backups, onboarding).
- `KNOWN_ISSUES.md` — living crash tracker (not updated this session; the 10-08 regression fix `a26bd6c` may or may not be reflected there — check).
- Memory: `secureapp-infra-paths`, `dotnet-workloads-user-local`, `never-debug-deploy-real-devices`, `feedback-batch-builds`.

## The Goal

SecureApp is a .NET MAUI (Android/iOS/Windows) Clean-Architecture app for a hospital anesthesiology/ICU team (ARIM KNTB Zlín): E2EE chat, shared encrypted library, logbook, company phone directory ("Soukromé kontakty ARIM"), schedules, notification hub. The 10-08 thread's goal: tie every device to a real person — users now fill in Jméno/Příjmení/Telefon/E-mail (nick optional), the app reconciles that profile against the shared ARIM contact list, and the relay learns which device belongs to which person and on which platform. This session's goal: finish the two UI pieces left open (reveal real name behind a nick in the chat list; admin sees devices grouped per person with per-platform coverage), ship it as one release, and do a small data fix (rename a contact).

## Where We Are

- **Release 1.56 (versionCode 59) is live** on the relay: `GET /download/android/version` → `{"VersionCode":59,"VersionName":"1.56","ReleasedAtUtc":"2026-10-09T09:23:20.13Z"}`. Phones get it via the in-app self-update.
- **Relay redeployed** with the 10-08 relay changes (directory `formal_name`/`platform`/`arim_contact_id`, `shared_contacts.email`, `/directory/link-arim-contact`). Verified two ways: `deploy.log` tail `=== Deploy finished at 2026-10-09T09:20:05Z ===`, and `POST /directory/link-arim-contact` went **404 → 401**.
- **DB migration confirmed live**: `PRAGMA table_info(shared_contacts)` now has column 7 `email TEXT NULL`.
- **Shared contact renamed**: `f20fe73d-28de-4779-968b-06c317b1c724` "Batková Eliška" → **"Batková Elina"** (phone 776315969, note ARIM, sort_order 39 unchanged; `updated_at_utc` = `2026-10-09T09:21:40.031Z`).
- **Press-and-hold real-name reveal** (chat list, 1:1 rows) implemented: `DirectoryNameResolver.LastKnownFormalNames` + `ResolveFormalName(byte[])`; `ChatSessionItem` is now an `ObservableObject` class with `ShownName`/`FormalName`/`RevealFormalName(bool)`; `ChatListPage` has `OnSessionPointerPressed`/`OnSessionPointerReleased` (450 ms hold, 400 ms post-reveal tap suppression).
- **Admin "Zařízení" card grouped by person**: `SettingsViewModel.GroupDevicesByPerson` + `DescribePlatform` + `_coveredPlatforms`; `RegisteredDeviceItem` got init-only `GroupHeader`/`GroupCoverageText` + computed `HasGroupHeader`; XAML shows a header above the first device of each person.
- **Builds**: Android Release 0 errors / 118 warnings; Windows Debug 0 errors / 16 warnings — all warnings in pre-existing classes (`CS0618 DisplayAlert`, `CS0169 _systemTabPrewarmed`, `CA1416` etc.), none in this session's code (verified via `--no-incremental` warning dump on Windows).
- **APK verified before upload**: signer `CN=SecureApp, OU=ARIM KNTB Zlin, O=SecureApp Community, L=Zlin, C=CZ`, SHA256 `6E:75:39:28:...:B2:7C` (same release key as every release); `aapt dump badging` → `versionCode='59' versionName='1.56'`; size 70,376,087 bytes.
- **Git**: `1010b36` (feature) + `90acac8` (version bump) pushed to both `pi` and `github`.
- **NOT verified**: the pointer gesture on Android (does it break tap-to-open or scrolling? does hold actually reveal?), the grouped device list rendering, the 10-08 reconciliation flow on real devices. Nothing was run on any device this session.
- **Windows portable zip NOT rebuilt** — still the 2026-10-02 build (45,719,959 bytes) on the relay. Offered to the user, no answer yet.
- **Uncommitted working-tree leftovers from an earlier session** (deliberately NOT committed by me): `.claude/settings.local.json` (5 new permission allow-rules: `ssh secureapp-pi *`, `scp *`, `$git = ...`, `$dir = ...`, `$adb = ...` prefixes) and `plans/handoffs/HANDOFF_notifications-background-widget_2026-09-28.md` (+5 lines). Ownership unknown — ask before committing.

## What We Tried (Chronological)

1. **Orientation.** User said only "pokracujem". No paste prompt. Found latest handoff (seq 8, 10-07), then `git log` showed 3 newer commits from 10-08 with no handoff — the WIP commit `7a5c2ef` message listed exactly two open items. Treated that as the thing to continue (correct — user didn't object).
2. **Data plumbing check.** `DirectoryMember.FormalName` already existed and `HttpContactDirectoryService` already published/parsed it (10-08 work), but `DirectoryNameResolver.BuildAsync` dropped it (dictionary of display names only). → Added a second static dictionary rather than changing the existing `IReadOnlyDictionary<string,string>` contract used by ~8 call sites.
3. **Gesture choice.** Checked csproj: no `CommunityToolkit.Maui` (only `.MediaElement`), no existing `LongPress`/`TouchBehavior`/`PointerGestureRecognizer` anywhere. Used MAUI's built-in `PointerGestureRecognizer` (`PointerPressed`/`PointerReleased`/`PointerExited`) + `Dispatcher.DispatchDelayed(450ms)` as a hand-rolled long-press.
4. **Tap-after-hold problem (anticipated, not observed).** On Android the CollectionView item click fires on ACTION_UP, so releasing after a reveal would also open the chat. Added `_lastRevealEndedUtc` + 400 ms suppression in `OnSessionSelected`; then hardened it to also suppress while `_revealedItem is not null`, because the order of PointerReleased vs SelectionChanged on ACTION_UP isn't guaranteed.
5. **Device grouping.** Chose a flat `CollectionView` with per-row optional header rather than `IsGrouped="True"`.
6. **First build attempt failed**: machine SDK 10.0.401 workloads wiped again → `NETSDK1147 ... android, wasm-tools`. Used the user-local SDK per memory `dotnet-workloads-user-local` (`DOTNET_ROOT`/`PATH`/`DOTNET_MULTILEVEL_LOOKUP=0`, `C:\Users\dvora\dotnet-local`). Note: it now also demands `wasm-tools` for the machine SDK — the local SDK built fine anyway.
7. **Commit hygiene slip.** `git add -A` swept in the two unrelated leftover files. Caught by reading the git warnings; `git reset --soft HEAD~1`, unstaged the two, recommitted with `-C c612593` → clean `1010b36`. Never pushed the bad commit.
8. **Release.** User: "ok nasad". Bumped 1.55(58)→1.56(59) (byte-level UTF-8-no-BOM rewrite of csproj), started `dotnet publish` in background, committed+pushed (`90acac8`), triggered `POST /admin/deploy`, waited 90 s, verified deploy (log + 404→401).
9. **Waiting on the background publish — two broken polling loops.** (a) `Get-Content -Raw` on an empty file returns `$null`, and the `-notmatch` loop exited immediately; APK on disk was still 10-08's (70,306,455 bytes, 17:55). (b) `[IO.File]::ReadAllText` threw "file in use by another process" on the task output file while the task was writing. The publish itself finished fine (exit 0, APK 11:22:55). Lesson: just wait for the task-completion notification; don't poll the output file.
10. **Contact rename.** Found the row via read-only `sqlite3 -readonly` on the host (db is root-owned, host writes fail). Waited until after the relay deploy (migration adds `email` to the same table). Wrote via a throwaway container: `docker run --rm -v <data>:/data -v /tmp/rename.sql:/rename.sql:ro --entrypoint sh nginx:alpine -c 'apk add -q --no-cache sqlite && sqlite3 /data/relay.db3 < /rename.sql'` — `dvorakv1` is in the `docker` group (gid 984). Guarded UPDATE (`WHERE id=... AND display_name='Batková Eliška'`), `changes()` = 1.

## Key Decisions

- **Second dictionary `LastKnownFormalNames`, not a richer value type in `LastKnown`.** Why: `LastKnown`/`Resolve`/`AreEquivalent`/`IsActive` are used by ChatList, ChatViewModel, GroupChatViewModel, App sweeps — changing the value type ripples everywhere for one reader. Rejected: a `record DirectoryEntry(Name, FormalName)` dictionary.
- **`ChatSessionItem` record → `ObservableObject` class.** Why: flipping one row's text in place needs change notification; rebuilding the whole `Sessions` collection on every press would flicker and lose scroll. Kept the same property names (`Id`, `PeerDisplayName`, `State`, ...) so every existing consumer (`OnSessionSelected`, button handlers, `ResetSessionAsync`, `DeleteSessionAsync`) compiles unchanged. Lost record value-equality — nothing relied on it (grep checked).
- **`FormalName` is nulled when equal to the nick** — so hold is a no-op for users without a nick (nothing new to reveal).
- **Pointer recognizer on the whole row Border**, not just the name label. Why: a label-only target is too small to hold reliably. Accepted risk: on Android a gesture recognizer inside a CollectionView item might interfere with selection/scrolling — flagged to user for testing. Rejected: adding CommunityToolkit.Maui just for `TouchBehavior.LongPressCommand` (new dependency, new init in MauiProgram, for one gesture).
- **Flat list + per-row header for device grouping, not `IsGrouped` CollectionView.** Why: grouped CollectionView has historically been flaky on Android, and the card needs no group-level features. Header text precomputed in VM (project's "no converters" convention).
- **Platform coverage shows 4 fixed slots** `Android / PC (WinUI) / iPhone (iOS) / Mac (MacCatalyst)` with ✓/✗. Unreconciled devices (`ArimContactId == null`) go last under "❔ Nepřiřazená zařízení". Within a person: ordered by Platform, then name. Device row name now gets a `(Android)`/`(PC)`/`neznámá platforma` suffix — also flows into the Deregister confirm dialog text (harmless).
- **Rename via direct DB write, not via the app.** Why: the `/contacts` endpoints need device auth (not admin secret); clients only `PublishAsync` on explicit edit (AddContact, Contacts reorder at `ContactsViewModel.cs:306`, Settings reconciliation `SettingsViewModel.cs:804/828`), so the relay is the source of truth and the rename sticks. Rejected: asking the user to edit it on the phone.
- **Did not rebuild Windows portable** — not requested; the PC build is from 10-02 and relay changes are additive. Offered instead.

## Evidence & Data

**Commits this session (pushed to `pi` + `github`):**

| Commit | What |
|---|---|
| `1010b36` | Chat list press-and-hold reveals real name; admin device list grouped by person (6 files, +175/−11) |
| `90acac8` | Release 1.56 (59) — csproj version bump only |

**Commits from the undocumented 10-08 session (context):**

| Commit | Version | What |
|---|---|---|
| `a4b4707` | 1.54 (57) | WireGuard onboarding: single clickable link `/download/onboarding/{code}` instead of typed pickup code |
| `a26bd6c` | 1.55 (58) | InputFocusCrashGuard: `NoInlining` + catch `Exception` (Contacts-search crash regression) |
| `7a5c2ef` | — (WIP) | User FirstName/LastName/Phone/Email (schema v16→v17), ARIM reconciliation on Save, `RbacAction.EditContact`, `SharedContact.Email`, directory formal_name/platform/arim_contact_id, `/directory/link-arim-contact` |

**Build results:**

| Target | Config | Errors | Warnings | Time |
|---|---|---|---|---|
| Android | Release (build) | 0 | 118 | 3:32 |
| Windows | Debug | 0 | 16 | 0:28 |
| Android | Release (publish) | 0 | — | ~4 min (11:18→11:22) |

Parent baseline was Android 113 / Windows 12; the delta came with the 10-08 commits (not this session's files — Windows list contains only `CS0618` ×7 in GroupChatViewModel/ChatViewModel/SettingsViewModel and `CS0169` in SettingsPage.xaml.cs).

**APK:**

| Field | Value |
|---|---|
| Path | `src/SecureApp.Presentation/bin/Release/net10.0-android/publish/com.companyname.secureapp.presentation-Signed.apk` |
| Size | 70,376,087 bytes (prev 1.55: 70,306,455) |
| versionCode / versionName | 59 / 1.56 |
| Signer SHA256 | `6E:75:39:28:45:B5:A2:91:6B:90:6C:AE:64:7B:5E:05:38:67:A4:2F:B6:FF:4F:49:43:29:EB:F2:03:41:B2:7C` |
| Upload response | `{"uploaded":true,"sizeBytes":70376087,"versionCode":59,"versionName":"1.56"}` |

**Relay deploy verification:**

| Signal | Before | After |
|---|---|---|
| `POST /directory/link-arim-contact` (no auth) | 404 | 401 |
| `deploy.log` tail | — | `=== Deploy finished at 2026-10-09T09:20:05Z ===` |
| `/admin/deploy` response | `202 {"queued":true,...}` | |

**Relay downloads dir (`~/SecureApp/relay/SecureApp.Relay/data/downloads/`) before upload:**

```
new-pc-onboarding.bat            2430  2026-10-07 08:59  (root)
new-pc-onboarding.ps1            8855  2026-10-07 08:59  (root)
secureapp-android.apk        70306455  2026-10-08 17:56  (dvorakv1)
secureapp-android.version.json     91  2026-10-08 17:56
secureapp-windows-portable.zip 45719959 2026-10-02 13:02
```

**Contact row before/after:**

```
before: f20fe73d-28de-4779-968b-06c317b1c724|Batková Eliška|776315969|ARIM|39|2026-09-29T14:59:41.72Z|2026-09-29T14:59:42.27Z
after:  f20fe73d-28de-4779-968b-06c317b1c724|Batková Elina|776315969|ARIM|2026-10-09T09:21:40.031Z (updated_at)
```

Search was `LIKE '%atkov%' OR '%ATKOV%' OR '%Elin%'` → exactly 1 row, so no ambiguity about which contact.

**`shared_contacts` schema after migration:** `id TEXT PK, display_name TEXT NOT NULL, phone, note, sort_order INTEGER NOT NULL, created_at_utc, updated_at_utc, email TEXT NULL`. `PRAGMA journal_mode` = `delete` (rollback journal, not WAL — safe to write from a second container on the same host bind mount).

**Relay container mount:** `/home/dvorakv1/SecureApp/relay/SecureApp.Relay/data -> /data`. Container has no `sqlite3` binary. Local images available for throwaway use: `nginx:alpine`, `wg-easy`, `jellyfin`.

**Pi user groups:** `dvorakv1` ∈ `sudo(27)`, `docker(984)` among others — docker access works without a password.

## Code Analysis

- `DirectoryNameResolver` (`src/SecureApp.Presentation/Chat/DirectoryNameResolver.cs`): `BuildAsync` now groups members by base64 public key once, builds both `result` (DisplayName) and `LastKnownFormalNames` (only non-blank FormalName). `ResolveFormalName(byte[] publicKey) => string?`. On fetch failure, `LastKnownFormalNames` keeps its previous value (only `LastKnown` semantics existed before).
- `ChatListViewModel.BuildListsAsync` snapshots `_displayedFormalNames = DirectoryNameResolver.LastKnownFormalNames`; `RefreshNamesInBackgroundAsync` skips the rebuild only if BOTH display names and formal names are equivalent (`AreEquivalent` reused for both).
- `ChatSessionItem` ctor: `(Guid id, string peerDisplayName, ChatSessionState state, string lastActivityText, string initials, bool isUnavailable = false, string? formalName = null)`; `ShownName` is `[ObservableProperty] public partial string`.
- `ChatListPage.xaml.cs` constants: `_revealHoldDelay = 450 ms`, `_revealTapSuppression = 400 ms`; state `_pressedItem`, `_revealedItem`, `_lastRevealEndedUtc`. Pressed handler resolves the row via `sender is BindableObject { BindingContext: ChatSessionItem item }` (gesture recognizers inherit BindingContext). The timer callback checks `ReferenceEquals(_pressedItem, item)` so a quick tap or a release before 450 ms never reveals.
- Only the main `Sessions` template got the gesture; the **Archive** `ArchivedSessions` template and group rows did not (still bind `PeerDisplayName`).
- `SettingsViewModel._coveredPlatforms`: `[("Android","Android"),("WinUI","PC"),("iOS","iPhone"),("MacCatalyst","Mac")]` — keys must match `DeviceInfo.Current.Platform.ToString()` as published by `HttpContactDirectoryService` (line ~74).
- `GroupDevicesByPerson`: people ordered by first non-blank `ArimContactName` (CurrentCultureIgnoreCase); header `"👤 {name}"`, coverage joined with `"  ·  "`; local function `AddGroup` sets header only on the first row via `item with { GroupHeader=..., GroupCoverageText=... }`.
- `RegisteredDeviceItem` remains a positional record; added members are init-only properties so `with` works and existing positional construction is unchanged.

## Files Changed

### Source code (commit `1010b36`)
- `src/SecureApp.Presentation/Chat/DirectoryNameResolver.cs` — `LastKnownFormalNames`, `ResolveFormalName`, BuildAsync fills both.
- `src/SecureApp.Presentation/ViewModels/ChatListViewModel.cs` — `ChatSessionItem` record→class; formal name passed in `ToSessionItem`; `_displayedFormalNames` + refresh comparison.
- `src/SecureApp.Presentation/Views/ChatListPage.xaml` — `PointerGestureRecognizer` on 1:1 row Border; name label binds `ShownName`.
- `src/SecureApp.Presentation/Views/ChatListPage.xaml.cs` — pointer handlers + tap suppression in `OnSessionSelected`.
- `src/SecureApp.Presentation/ViewModels/SettingsViewModel.cs` — `GroupDevicesByPerson`, `DescribePlatform`, `_coveredPlatforms`; `RefreshRegisteredDevicesAsync` uses grouping; device name gets platform suffix; `RegisteredDeviceItem` gets `GroupHeader`/`GroupCoverageText`/`HasGroupHeader`.
- `src/SecureApp.Presentation/Views/SettingsPage.xaml` — device DataTemplate wrapped in `VerticalStackLayout` with conditional person header.

### Config (commit `90acac8`)
- `src/SecureApp.Presentation/SecureApp.Presentation.csproj` — `ApplicationDisplayVersion` 1.55→1.56, `ApplicationVersion` 58→59.

### Data (production relay, not in git)
- `relay.db3` `shared_contacts` row `f20fe73d-...` display_name renamed.

### Scratch (session-local, disposable)
- `%TEMP%\claude\...\scratchpad\q.sql`, `rename.sql` — the lookup/rename SQL used above.

## User Feedback & Preferences (REQUIRED — never omit)

- *"pokracujem"* — whole opening message; expected me to find where we left off myself (git log + handoffs) rather than ask. Worked.
- *"ok nasad a v kontaktech přejmenuj batkovou na Batková Elina"* — approval to deploy came only after I explicitly asked; consistent with the `feedback-batch-builds` memory (build/release only when the user says). Also shows the user hands over small production data fixes casually, with partial names ("batkovou") — resolve by searching, confirm uniqueness (1 match), do it.
- The user's earlier standing rules still apply and were honored: release builds only, signed with the release key, never debug-deploy real devices; never uninstall+install.
- Communicate in Czech with the user (all user messages are Czech; replies in Czech).

## Where We're Going

1. **Live-check the press-and-hold on Android (1.56)** — ask the user: does a normal tap still open a chat, does scrolling the list still work, does a ~0.5 s hold show Jméno Příjmení and a release NOT open the chat. If tap/scroll is broken → quickest fallback: move the `PointerGestureRecognizer` to the avatar `Border` only, or revert the gesture block (keep the data plumbing), release 1.57.
2. **Check `device_app_logs` for crashes on 1.56(59)** (pattern: write .sql → scp → `ssh secureapp-pi "sqlite3 -readonly <db> < /tmp/x.sql"`), especially `ObjectDisposedException`/`GuardIsFocused` to confirm `a26bd6c` held, and anything new from the schema v17 migration on real devices.
3. **Verify the 10-08 reconciliation flow on a real device** — Settings → fill Jméno/Příjmení → Save → ARIM match/mismatch dialog → `/directory/link-arim-contact` called → admin "Zařízení" card shows that person grouped with ✓ platform. Note Elina: if her profile says "Eliška", Save will raise the mismatch prompt (intended).
4. **Windows portable rebuild + `/admin/upload/windows`** if the user wants PC on 1.56 (offered, unanswered).
5. **Decide on the two uncommitted leftover files** (`.claude/settings.local.json`, the 09-28 handoff edit).
6. Long-standing: documentation catch-up (`DEVELOPMENT_PLAN.md`/`NOTIFICATION_HUB_SPEC.md`, now 5 subsystems behind), carry-over backlog, design K/L/M — raise directly with the user rather than silently carrying a 5th time.

## Risks & Blockers

- **`PointerGestureRecognizer` inside a CollectionView item on Android is untested** — could swallow taps or interfere with scroll on the main chat list (the app's most-used screen). Release is already out to everyone via self-update.
- **Hold never released** (e.g. finger slides off and Android sends neither Released nor Exited) → the row keeps showing the formal name until the next list rebuild; `_revealedItem` non-null would also suppress the next chat tap. Low impact, but a possible "chat won't open" report would point here.
- **Machine-wide .NET SDK workloads are gone again** (now also asks for `wasm-tools`); every build needs the user-local SDK env vars. Permanent fix needs an elevated `dotnet workload restore`.
- **Windows PC clients on the 10-02 build** talk to a relay with new directory fields — believed additive/backward compatible (all new DTO fields nullable/defaulted), not tested.
- **Root-owned relay data dir**: host-side writes need the docker-throwaway-container trick (or `/admin/upload/ops` for files). The two inert test rows from 10-07 (`pending_wireguard_peers`, `wireguard_pickup_codes` "ZZZ Pickup Test") could now be cleaned the same way.

## Open Questions

- Does the gesture behave on Android/S23+? (see Risks)
- Should the Archive section and group-chat member names get the same reveal?
- Does the user want the Windows portable updated to 1.56?
- Is the 10-08 `NoInlining` theory for the crash regression right — any recurrences on 1.55/1.56?
- Who owns the uncommitted `.claude/settings.local.json` / 09-28 handoff edits — commit or discard?

## Quick Start for Next Session

```powershell
# Read this handoff, then the parent for the long carry-over list
Get-Content "H:\Visual Studio\C#\Aplikace\plans\handoffs\HANDOFF_standalone-be860f68_crash-fixes-wireguard-onboarding_2026-10-07.md"

# Git state (git.exe is not on PATH)
$git = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
& $git log --oneline -6      # expect 90acac8 Release 1.56 (59) at/near top
& $git status -s             # expect the 2 leftover files unless resolved

# Relay state
curl.exe -sS http://192.168.50.8:8080/download/android/version   # expect 59 / 1.56

# Builds MUST use the user-local SDK (machine workloads wiped)
$dir="C:\Users\dvora\dotnet-local"; $env:DOTNET_ROOT=$dir; $env:PATH="$dir;$env:PATH"; $env:DOTNET_MULTILEVEL_LOOKUP="0"

# Admin secret (never print it): latest backup .env
#   C:\Users\dvora\SecureApp-Backups\<newest>\relay.env  -> SECUREAPP_RELAY_ADMIN_SECRET

# Key files
#   src/SecureApp.Presentation/Views/ChatListPage.xaml(.cs)      — press-and-hold
#   src/SecureApp.Presentation/Chat/DirectoryNameResolver.cs
#   src/SecureApp.Presentation/ViewModels/SettingsViewModel.cs   — GroupDevicesByPerson, ReconcileArimContactAsync

# Next action: ask the user how 1.56 behaves on the phone (tap opens chat? hold shows real name?)
# and query device_app_logs for errors with app_version '1.56 (59)'.
```

## Session Closed
**Closed at:** 2026-10-09
**Commit:** `bf1b849` (pushed to `pi` and `github`)
**Session status:** Handed off to next session
