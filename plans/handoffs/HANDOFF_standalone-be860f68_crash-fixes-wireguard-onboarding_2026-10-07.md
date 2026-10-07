# SecureApp: chased and fixed a 3-bug crash cascade on a real user's phone live, then built a full one-time-pickup-code self-service WireGuard+SecureApp onboarding system

**Date:** 2026-10-07
**Session span:** roughly the full working day — onboarding, a 3-bug crash-fix saga with 6 distinct Android rebuilds/reinstalls, then a 3-round feature build culminating in a brand-new relay-side subsystem.
**Status:** IN PROGRESS
**Bead(s):** none
**Epic:** none
**Scope this handoff covers:** Android client crash fixes (×3), one Windows-client WireGuard-sanitizer fix, and a full relay+client self-service onboarding feature — NOT the carried-over design-mockup or documentation-debt threads, which remain exactly as the parent left them.
**Chain:** `standalone-be860f68` seq `8`
**Parent:** `plans/handoffs/HANDOFF_standalone-be860f68_ai-translation-wireguard-fix_2026-10-06.md` (seq 7)
**Prior chain:** `HANDOFF_identity-recovery-doc-zoom_2026-09-30.md` > `HANDOFF_zoom-fix-library-workflow_2026-10-01.md` > `HANDOFF_library-redesign-subcategories_2026-10-02.md` > `HANDOFF_standalone-be860f68_library-video-gcs-tools_2026-10-03.md` (seq 4) > `HANDOFF_standalone-be860f68_murray-das-chat-pairing_2026-10-04.md` (seq 5) > `HANDOFF_standalone-be860f68_widget-wireguard-multiprofile_2026-10-05.md` (seq 6) > `HANDOFF_standalone-be860f68_ai-translation-wireguard-fix_2026-10-06.md` (seq 7) > this (seq 8)

---

## Since Last Handoff

Parent's (seq 7) "Where We're Going" had 5 items. Mixed outcome this time — not a clean miss like seq 5→6→7, but not a clean hit either.

1. **Design direction decision (K/L/M mockups)**
   - Still **not touched**, 2nd session running untouched.
   - Legitimately crowded out this time by a real production emergency, unlike the discretionary feature-request drift of prior sessions.
2. **Confirm the WireGuard `wireguard[1]` fix actually resolves the live issue**
   - Tangled, never cleanly isolated.
   - The user's phone was crash-looping on EVERY launch (a completely separate, far more severe bug) before they could even reach Settings to retest the original fix.
   - Once the app could launch again, a SECOND, different WireGuard bug surfaced (tunnel name rejecting spaces) and was also fixed.
   - The original bracket-suffix bug is presumed fixed (code-correct, never reverted) but was never independently re-confirmed in isolation — it got folded into a much bigger retest cycle.
3. **Live-verify the local-AI translation feature end-to-end**
   - Still **not done**.
   - Compounded: the feature's native QuestPDF dependency turned out to be the ROOT CAUSE of this session's worst crash (see Crash #2 below) and is now Windows-gated behind `#if WINDOWS`.
   - That gate itself has also never been exercised live on a real Windows machine.
4. **The carried-over 7-item backlog** (pairing-loop, Android UI fix verification, Petr Faltus's phone, `WebSocketException`, `SearchBar` crash, doc debt, Windows multi-profile distribution)
   - **One item genuinely resolved this session**: the `SearchBar`/`Entry` `ObjectDisposedException` crash (`KNOWN_ISSUES.md` #4, open since 2026-09-29) is fixed and field-verified via live `adb`/`uiautomator` testing.
   - The other 6 remain fully untouched, now 3 sessions deep.
5. **Documentation debt**
   - Got **worse again**.
   - `DEVELOPMENT_PLAN.md`/`NOTIFICATION_HUB_SPEC.md` were already missing the 2026-10-05 multi-profile login and the 2026-10-05/06 translation feature.
   - This session added an entire 3-bug crash-fix saga AND a brand-new WireGuard self-service onboarding system (new DB table, 2 new relay endpoints, new client-side UI, 2 new ops scripts) with zero entries in either file.

**Net trajectory**: unlike seq 5→6→7 (discretionary feature requests derailing planned carry-over work), this session's derailment was first a genuine production emergency — the user's own phone was fully unusable — followed by a legitimate, directly user-requested feature build that grew organically out of fixing a real friction point found live. The 6-item carry-over backlog is now unambiguously at the point where a direct conversation is warranted: dedicate a session to it, or explicitly accept it as permanently low-priority background debt.

## Reference Documents

None found this session — no `*BIBLE*` file, no `CLAUDE.md` at the repo root or in `.claude/`.
`relay/ops/README.md` continues to serve as the de facto ops runbook and was extended again this session (see Files Changed).
`KNOWN_ISSUES.md` remains the project's own living crash-tracking document, generated 2026-10-04 from a direct `device_app_logs` query rather than guesswork, and extended twice more this session for the same reason.

## The Goal

SecureApp is a .NET MAUI (Android/iOS/Windows) Clean-Architecture app for a hospital anesthesiology/ICU team: E2EE chat, a shared encrypted document/procedure library, a duty logbook, a company phone directory, a personal work-schedule module, and a notification hub with an Android widget.

This session had two distinct, sequential phases, both driven entirely by the user's own live reports, not by planned work:

1. The user's own phone was crashing in production immediately after the prior session's release. Diagnosed and fixed THREE separate, unrelated bugs in sequence — each one found only after the previous fix had already shipped and a brand-new crash surfaced on the very next retest.
2. Once the app was genuinely stable again, the user asked for a WireGuard config file to stop failing to import. That led, through two explicit scope-clarifying rounds, to building a complete self-service onboarding system: a non-admin colleague can now set up a brand-new PC (WireGuard client + SecureApp + VPN tunnel) with nothing but one `.bat` file and a short one-time code — never an admin password, never a `.conf` file handed over by hand.

## Where We Are

- **Crash #1 — pre-existing bug from the parent session, open since 2026-09-29: `ObjectDisposedException` on `Entry`/`SearchBar`/`Editor` focus-change.**
  - Root-caused via a direct `device_app_logs` SQL query, not the user's vague report alone ("Apka pada sprav to prosim..." named no screen or action).
  - Mechanism: a native Android `View.OnFocusChange` callback lands on the UI thread AFTER Shell has already navigated away from a page and torn down that page's handler's own DI scope.
  - `InputView.MapIsFocused` then calls `handler.GetService<HideSoftInputOnTappedChangedManager>()` against the now-disposed `IServiceProvider`, which terminates the whole process — not a catchable .NET exception at the call site, since it arrives through a JNI callback.
  - Fix: new file `Infrastructure/InputFocusCrashGuard.cs`, `#if ANDROID`-guarded, calls `ModifyMapping("IsFocused", ...)` on `EntryHandler`/`EditorHandler`/`SearchBarHandler.Mapper`, wrapping the base mapping in a try/catch for `ObjectDisposedException`.
  - This exact fix shipped with a real same-day regression (see Crash #3) that wasn't caught until the very next release cycle, minutes later.
- **Crash #2 — new, far more severe, introduced by the PARENT session's own work: `TypeInitializationException: QuestPDF.Settings`.**
  - Crashed on EVERY single launch, every screen, no exceptions, on Android.
  - Root cause: the parent session's local-AI-translation feature added `PdfPig`/`QuestPDF` `PackageReference`s to `SecureApp.Presentation.csproj` with NO platform `Condition`.
  - `MauiProgram.cs`'s `QuestPDF.Settings.License = ...` line ran unconditionally on every platform including Android, despite the FEATURE itself already being gated Windows-only everywhere else in the UI (`DeviceInfo.Current.Platform == WinUI`).
  - Touching `QuestPDF.Settings` at all forced the CLR to load the QuestPDF assembly, which forced a `dlopen` of its native `libQuestPdfSkia.so`, which needs `libstdc++.so.6` — a library modern Android (the user's S23+ specifically) no longer ships.
  - Found via live `adb logcat` while the user reproduced it in real time — this crash NEVER reached the relay's `device_app_logs` table at all, since the app died before its own diagnostics/`AppLogUploader` infrastructure could initialize.
  - Fix: `PdfPig`/`QuestPDF` `PackageReference`s in the `.csproj` gained `Condition="'$(TargetFramework)'=='net10.0-windows10.0.19041.0'"`.
  - The `QuestPDF.Settings.License` call in `MauiProgram.cs` wrapped in `#if WINDOWS`.
  - The ENTIRE `Translation/LocalAiLibraryTranslationService.cs` file body wrapped in `#if WINDOWS` (it references QuestPDF/PdfPig types throughout).
  - New file `Translation/UnsupportedLibraryTranslationService.cs` (throws `PlatformNotSupportedException` on both interface methods) registered via `#else` in `MauiProgram.cs`'s DI setup, since `ILibraryTranslationService` is a REQUIRED constructor parameter in `LibraryViewModel`/`LibrarySubcategoryDetailViewModel`/`SettingsViewModel` on every platform, not just Windows.
  - APK size dropped from 82,711,812 bytes to 70,294,167 bytes once the native library was actually excluded — confirmed by opening the `.apk` as a zip and grepping for `libQuestPdfSkia.so`/`libstdc`, finding zero matches after the fix (both present before).
- **Crash #3 — self-inflicted regression, introduced by THIS session's own Crash #1 fix, same day: `NullReferenceException` inside `InputFocusCrashGuard.GuardIsFocused` itself.**
  - Crashed on EVERY SINGLE focus change — meaning Settings, Contacts, Logbook, and Files (Library) all failed to open at all.
  - Root cause: `ModifyMapping`'s "capture the existing/base mapping action for this key" mechanism returned `null` for `Entry`/`Editor`/`SearchBar`'s `"IsFocused"` key specifically.
  - That key is defined on `InputView`'s BASE `PropertyMapper` and resolved via the mapper's own inheritance/fallback chain at property-update time, but is NOT present directly in each handler's own local dictionary — so `ModifyMapping`'s lookup (which only checks the local dictionary) found nothing to wrap.
  - Calling that `null` delegate (`baseAction(handler, view)`, no null-check) threw `NullReferenceException`, which the existing `catch (ObjectDisposedException)` block does not catch, so it propagated up and killed the process — on literally every focus change, i.e. far MORE often than the rare navigation-away race the original fix targeted.
  - Fix: changed `baseAction(handler, view)` to `baseAction?.Invoke(handler, view)`.
  - Verified live via `adb`+`uiautomator`, not by asking the user to retest a 3rd time: launched the app fresh, dumped the UI tree to find the bottom tab bar's real on-screen coordinates, tapped "Kontakty" (756,2140) and "Soubory" (540,2140), confirmed via `adb shell pidof` that the SAME process PID (27874) survived both taps, confirmed zero `FATAL`/`AndroidRuntime` lines in a freshly-cleared `logcat` capture.
  - One stray tap (972,2140 — the expected "More" tab position) accidentally landed on the Claude Code companion app itself running on the same physical S23+ (the user's own remote-control phone), not on SecureApp. Caught immediately via a UI dump showing Claude Code's own "Cogitating... / 2 running tasks" text; re-foregrounded SecureApp via `adb shell am start` (which correctly reported "Activity not started, its current task has been brought to the front" — proof it never crashed from the stray tap either); confirmed the SAME PID was still alive throughout.
- **WireGuard bug #2 — new, genuinely distinct from the parent session's `wireguard[1]` bracket-dedup bug, same user-visible error text, different root cause entirely.**
  - User attached `e2622d18-PC_v_praci.conf` (the hash prefix is Claude's own upload-sanitization, not the real device filename) and reported the SAME "Název tunelu je neplatný" error persisting.
  - Opened the file: `[Interface] Address = 10.8.0.7/24` — matches wg-easy client #8, "PC v praci", from the PARENT session's own evidence table exactly.
  - The member name "PC v praci" contains a SPACE. `Path.GetInvalidFileNameChars()` (the OLD sanitizer, fixed in the parent session for the bracket bug) does NOT consider a space invalid for a FILENAME — so the file downloaded fine as "PC v praci.conf".
  - Windows' WireGuard client derives its internal TUNNEL NAME from that filename and validates it against its OWN, much stricter character set (`^[a-zA-Z0-9_=+.-]{1,32}$` — confirmed via `WebFetch` against WireGuard's own documentation, not guessed), which rejects spaces outright.
  - Fix: `SettingsViewModel.Updates.cs`'s `DownloadWireGuardConfigAsync` sanitizer switched from `Path.GetInvalidFileNameChars()` to a direct regex against WireGuard's own allowed charset: `Regex.Replace(_lastWireGuardMemberName, "[^a-zA-Z0-9_=+.-]", "_")`, truncated to 32 chars, falling back to the literal "wireguard" if empty — same fallback behavior as before, just a stricter/more correct character filter.
- **New feature, built across 3 escalating rounds this session: self-service WireGuard + SecureApp onboarding via a one-time pickup code, requiring zero admin credentials in a non-admin colleague's hands.**
  - Round 1 (`relay/ops/new-pc-onboarding.ps1`): checks for/installs the WireGuard client and SecureApp portable build if either is missing; installs a `.conf` as a Windows tunnel service directly via `wireguard /installtunnelservice <path>` (a real, documented WireGuard CLI command, verified via `WebFetch` against `git.zx2c4.com/wireguard-windows/tree/docs/enterprise.md` — bypasses the GUI "Import tunnel" dialog and its interactive name-validation entirely).
  - Round 2 (`new-pc-onboarding.bat`): a double-click wrapper, since a `.ps1` opens in a text editor on double-click rather than running. Auto-detects a `.conf` file sitting next to it.
  - Round 3 (final, this session's real point): the admin now creates a WireGuard client exactly as before (Settings → WireGuard card), and the relay ALSO mints a 24-hour, single-use pickup code, shown in a brand-new UI block with a one-tap copy button.
  - A non-admin colleague downloads ONE file — `new-pc-onboarding.bat`, now hosted directly on the relay at `/download/onboarding` — runs it, and types the code when prompted. No `.conf` file ever changes hands, no admin secret is ever exposed to them.
  - The `.bat` self-fetches its own logic (`new-pc-onboarding.ps1`) fresh from the relay EVERY run, so any future fix to the real logic only needs re-uploading to the relay — the already-distributed `.bat` copy never needs to be resent.
- **New relay DB table `wireguard_pickup_codes`** (`code` TEXT PK, `config_text` TEXT, `member_name` TEXT, `created_at_utc` TEXT, `expires_at_utc` TEXT, `consumed_at_utc` TEXT NULL).
  - Same one-time-use shape (code/expires/consumed) as the pre-existing `invites` table, deliberately kept as a SEPARATE table since `invites` is specifically for SecureApp's own device-activation flow (`/register` reads it) and conflating two different "what does this code unlock" semantics risks a future bug.
- **New relay endpoints**:
  - `GET /download/onboarding` (serves the `.bat`)
  - `GET /download/onboarding.ps1` (serves the `.ps1`, self-fetched by the `.bat` on every run)
  - `GET /onboarding/pickup/{code}` (PUBLIC, no admin auth at all — the entire point of the feature)
  - `POST /admin/upload/ops?name=` (generic admin-secret-gated file upload to `downloadsDir`, added mid-session because a plain `scp` as `dvorakv1` failed with "Permission denied" on that root-owned directory)
- **Version progression**: 1.51(54) at session start → 1.52(55) → 1.53(56).
  - 1.52(55) alone covered FOUR separate, binary-distinct rebuilds under the exact same version number, each one fixing a newly-discovered crash — see the Evidence table for all 4 sizes.
  - 1.53(56) is the pickup-code feature, a genuine version bump since it's real new client capability, not just a bug fix.
- **Relay redeployed 3 times this session.**
  - The crash fixes themselves (QuestPDF guard, null-check, regex sanitizer) never touched relay code, only client code.
  - The onboarding-hosting change, the `/admin/upload/ops` addition, and the pickup-code feature each needed a real relay code redeploy.
  - All 3 independently hit the SAME deploy-verification gotcha already documented in `relay/ops/README.md` from 2026-09-24 (see Risks & Blockers and the Evidence table).
- **Full end-to-end live test of the pickup-code flow, against the REAL production relay (not a mock).**
  - Created a genuine test WireGuard client ("ZZZ Pickup Test") via `/admin/wireguard/clients`.
  - Extracted its returned pickup code (`5b68af404e4d2e30a4452804`).
  - Redeemed it successfully at `/onboarding/pickup/{code}` (got back the correct `configText`+`memberName`).
  - Redeemed the SAME code again and confirmed it now correctly returns `404` (one-time-use enforced, race-safe via a single atomic `UPDATE`).
  - Deleted the real test wg-easy peer (`da9e47bd-447f-4e3c-a6fb-13276bfa45fa`) via `DELETE /api/wireguard/client/{id}`.
  - Two now-harmless, already-consumed test rows (`pending_wireguard_peers`, `wireguard_pickup_codes`) were left in the DB — the cleanup `DELETE` via `sqlite3` as `dvorakv1` hit the exact same root-owned-file permission wall that `/admin/upload/ops` exists to work around; not worth a 3rd relay interaction for 2 inert test rows.

## What We Tried (Chronological)

1. **Onboarding (parent seq 7 → this session).**
   - Read the FULL parent handoff per its own Quick Start instructions.
   - Verified git HEAD (`555ef4a`) and the live relay version (1.51/54) matched the parent's claims exactly.
   - Confirmed zero comments existed on the design-exploration Artifact canvas (checked via `ArtifactComments`, not assumed).
   - Read the 3 key files the parent explicitly named (`SettingsViewModel.Updates.cs`, `LocalAiLibraryTranslationService.cs`, `LibraryViewModel.Actions.cs`) plus 3 adjacent files not explicitly listed (`SettingsViewModel.LocalAiTranslation.cs`, `LibrarySubcategoryDetailViewModel.cs`, `KNOWN_ISSUES.md`).
   - Confirmed every single code-level claim in the parent handoff matched the current codebase with zero drift.
   - Reported this summary back to the user and waited for direction rather than guessing which of the 4 open threads to pick up first.
2. **User: "Apka pada sprav to prosim..."** (no screen, no action, no device named).
   - Rather than ask for repro steps immediately, queried the relay's own `device_app_logs` table directly.
   - PowerShell cannot pass nested double-quotes through to `ssh`/`sqlite3` reliably (confirmed twice — the quotes get silently stripped or mis-parsed). Working pattern: write a `.sql` file locally with `Write`, `scp` it to the Pi, run `ssh secureapp-pi "sqlite3 <db> < /tmp/file.sql"`, clean up the remote temp file.
   - Query: `SELECT device_id, received_at_utc, app_version, SUBSTR(line,1,300) FROM device_app_logs WHERE kind='errors' ORDER BY received_at_utc DESC LIMIT 15;`
   - Found the most recent crash (`ObjectDisposedException`, 2026-10-06T14:25:36Z / 16:25:29 local) on device `6b4bf8e9-131b-4304-8368-c831a32c5a2b`, confirmed via a `directory_entries` lookup to be "Zařízení S23+ uživatele Vilém" — the user's own phone.
   - Pulled the FULL stack trace (not just the first line) for 2 occurrences that week, confirmed it matched `KNOWN_ISSUES.md` item #4 exactly, open since 2026-09-29.
   - Confirmed, via a full-codebase grep for `<SearchBar` vs `<Entry`, that this app has NO real `SearchBar` control anywhere — every "search bar" screen uses a plain `<Entry>` — so the item's original "SearchBar" label was always a guess, now corrected.
   - Built `InputFocusCrashGuard.cs`, built clean on both Android (`dotnet build -f net10.0-android -c Release`, 0 errors, same 113 pre-existing warning classes) and Windows (0 errors, same 12 pre-existing warnings).
   - Committed (`ef880fb`), pushed to `pi`+`github`.
   - User then asked me to push the fix to their phone directly, since the in-app self-update mechanism needs the app running — which it currently isn't.
3. **Direct `adb` deployment saga.**
   - The first device connected via USB (`22dab4387e0b7ece`) turned out, on checking `adb shell getprop ro.product.model`, to be the **Samsung Galaxy S9+** (`SM-G965F`), NOT the S23+ the user meant — caught before any install action was taken, a real near-miss given this project's own hard rule about never installing the wrong build on the wrong real device.
   - Wireless `adb connect 10.8.0.3:5555` (the address the existing `adb-wireless-technique` memory says to "ALWAYS try first, fixed port") was actively refused — confirmed the port is NOT actually persistently fixed on this phone.
   - Required asking the user to check Settings → Developer options → Wireless debugging and read out the CURRENT port FOUR separate times this session (`46113`, then `38575`, then `45971`, then `34829`), each one after the previous wireless session had silently dropped.
   - Once connected (confirmed via `getprop ro.product.model` → `SM-S916B`, the real S23+), bumped the version 1.51(54)→1.52(55) in the `.csproj`, ran `dotnet publish -f net10.0-android -c Release`.
   - Verified the signing certificate via `keytool -printcert -jarfile` (`SHA256 6E:75:39:28:...:B2:7C` — same as every prior release).
   - `adb install -r` (an in-place UPGRADE install, deliberately never `adb uninstall` + `adb install`, per this project's own hard rule born from a real past data-loss incident).
   - The user asked directly, mid-install, "Doufam ze nebyl uninstall a install? Ztratim vstchny data...." — answered by showing `dumpsys package`'s `firstInstallTime` (unchanged, 2026-09-29) vs `lastUpdateTime` (just-now) as concrete proof, not just a verbal reassurance.
   - Uploaded the APK to the relay via `POST /admin/upload/android` — had to re-discover the exact required multipart field names (`apk`, `versionCode`, `versionName` — NOT `file`, which the relay rejected with "Missing 'apk' file." on the first attempt).
   - Committed the version bump (`ff9d967`).
4. **User: "Porad pada..."**
   - Re-queried `device_app_logs`: zero new rows since before the update.
   - Had the user force-close the app and retest ("Ikdyz apku vymazu z pameti pada") — still crashing immediately.
   - Reconnected wireless `adb` (port had already changed once more), cleared the device's live `logcat` buffer, had the user reproduce it in real time while watching the log directly — this bypassed the relay's log pipeline entirely, the only way to see this one at all, since the app was dying before its own `AppLogUploader` diagnostics could even initialize.
   - Found `android.runtime.JavaProxyThrowable: [System.TypeInitializationException]: TypeInitialization_Type, QuestPDF.Settings`, with a `monodroid-assembly: Could not load library '.../libQuestPdfSkia.so'. dlopen failed: library "libstdc++.so.6" not found` warning logged immediately above it, repeated identically across 3 separate launch attempts in the capture.
   - Diagnosed, fixed (see Crash #2). First rebuild attempt used `dotnet build` instead of `dotnet publish`, so the signed APK sitting in `/bin/Release/.../publish/` was stale (identical file timestamp to before the fix was even written) — caught only by re-opening the `.apk` as a zip archive and finding `libQuestPdfSkia.so` STILL present.
   - Republished correctly with `dotnet publish`, re-checked the zip, confirmed the native library was now genuinely absent, confirmed the file size had dropped (82.7MB → 70.3MB) as independent corroborating evidence.
   - Verified the signing certificate matched again, reinstalled via `adb install -r`, launched via `adb shell am start`, confirmed `topResumedActivity`/`ResumedActivity` via `dumpsys activity activities` (genuinely in the foreground, not silently dead), confirmed zero `FATAL`/`AndroidRuntime` lines in a fresh `logcat` capture across the whole launch.
   - Committed the fix (`c2e93dd`), documented it in `KNOWN_ISSUES.md` as a brand-new item #0 (`6a319e2`, also updating item #4's own status to note its verification was blocked by this unrelated bug), re-uploaded the corrected APK to the relay under the SAME version number 1.52(55) — a real binary-vs-version-number mismatch worth flagging (see Evidence table).
5. **User: "Jede ale od kdyz chci otrvrit soubory kontakty nebo logbook nebo nastaveni spadne...."** then, after a clarifying question, **"Ne proste po tapnuti na nastaveni nebo kontakty ci logbook, nebo dokumenty apka spadne..."**
   - Cleared the device's `logcat` buffer, had the user reproduce; the wireless connection had dropped yet again mid-investigation (new port `45971` needed from the user).
   - Fresh dump revealed `NullReferenceException` inside `SecureApp.Presentation.Infrastructure.InputFocusCrashGuard.GuardIsFocused` itself — a same-day regression in THIS session's own Crash #1 fix.
   - Fixed with a null-conditional invoke, rebuilt, verified the signature again, reinstalled.
   - Rather than asking the user to retest a 3rd time in a row, drove the verification personally via `adb`+`uiautomator`: dumped the UI tree to find the bottom tab bar's real coordinates, tapped through Kontakty and Soubory, confirmed the SAME process PID (27874) survived both without any `FATAL` lines.
   - One stray tap (at the "More" tab's expected coordinates) accidentally landed on the Claude Code companion app running on the SAME physical device instead of SecureApp — caught immediately via the UI dump, corrected by re-foregrounding SecureApp.
   - Committed (`4f6a088`), re-uploaded a 4th distinct binary to the relay still under version 1.52(55), pushed.
6. **User attached `e2622d18-PC_v_praci.conf`, reporting the exact same "invalid name" error persisting** despite the already-shipped bracket-dedup fix from the parent session.
   - Read the file directly (`[Interface] Address = 10.8.0.7/24` matches wg-easy client #8, "PC v praci", from the parent session's own evidence table).
   - Recognized the member name itself contains a space — a genuinely DIFFERENT bug than the one already fixed.
   - Confirmed this precisely via `WebFetch` against WireGuard's own documentation (`www.wireguard.com/install/`, then `git.zx2c4.com/wireguard-windows/tree/docs/enterprise.md`, then `.../docs/adminregistry.md`) rather than guessing — found the real installer download URL, confirmed `wireguard /installtunnelservice <path>` is a real documented command, confirmed the installer `.exe` itself has NO documented silent-install flag (only the raw MSI via `msiexec` does).
   - Fixed the sanitizer in `DownloadWireGuardConfigAsync` (regex against the WireGuard-safe charset directly), rebuilt Android (a 5th distinct binary, still version 1.52/55), verified signature + data-safety again, reinstalled (port had changed again, to `34829`), uploaded to relay.
   - Committed (`d6afc69` — this commit also includes Round 1 of the onboarding script), pushed.
7. **User: "Musis mi updatenout v s23 sam ja to pres apku nedam kdyz pada..."** (established the whole direct-adb-deployment pattern used throughout steps 3-6) **→ later: "Spis udelej jednoduchy instalator jednoduche kopirovani rozbaleni, kontrola jestli je nainstalovane co ma byt a kdyz tak stahnout a nainstalovat."**
   - Built `relay/ops/new-pc-onboarding.ps1` (Round 1): self-elevates, checks for/installs the WireGuard client (downloads `https://download.wireguard.com/windows-client/wireguard-installer.exe`, verified as the real stable URL via `WebFetch`), checks for/installs SecureApp's portable build (downloads the relay's existing `/download/windows` zip, extracts to `C:\SecureApp` by default).
   - If given a `.conf` path, installs it as a Windows tunnel service via `wireguard /installtunnelservice <path>` — bypassing the GUI import dialog (and its name validation) entirely, with the WireGuard-safe-charset re-sanitization applied as defense-in-depth.
   - Sent via `SendUserFile`.
8. **User: "Vytvor neco co zvladne i lajk nejaky exe soubor nebo bat..."**
   - Built `relay/ops/new-pc-onboarding.bat` (Round 2), a double-click wrapper — a `.ps1` opens in a text editor rather than running when double-clicked, real friction for anyone without a PowerShell habit.
   - Designed to auto-detect a `.conf` file sitting in the SAME folder as the `.bat` (via `dir "%~dp0*.conf" /b /o-d`, newest-by-modified-time first) or accept one dragged onto its icon.
   - **Hit a real, classic batch-scripting bug**: inside a `for /f "delims=" %%f in (...) do ( if "%CONF%"=="" set "CONF=..." )` block, `%CONF%` is expanded ONCE at PARSE time, not freshly on each loop iteration — every file in the loop saw the SAME original (empty) value and unconditionally overwrote it, so the LAST file in the sorted list won instead of the intended FIRST/newest one.
   - Confirmed empirically with an isolated test (two dummy `.conf` files, one newer by 1 second via `Start-Sleep`) before and after the fix.
   - Fixed with `setlocal enabledelayedexpansion` + reading `!CONF!` instead of `%CONF%` inside that one block.
   - Verified ALL THREE paths this fix needed — explicit drag-and-drop argument, auto-detect-newest-of-several, no-`.conf`-present-at-all — against a dummy stand-in `.ps1` (never the real one, to avoid triggering real installs during testing) before trusting any of it.
   - Committed (`4f23a06`), sent both files via `SendUserFile`.
9. **User: "Ok a je to k dispozici v relay? A nemuze se stahnout pouze bat nebo exe ktery vse zaridi..."**
   - Hosted both files directly on the relay: new `GET /download/onboarding` (serves the `.bat`) and `GET /download/onboarding.ps1` (serves the `.ps1`), plus a new section on the existing `/download` landing page HTML, matching the exact pattern already used for the Android/Windows download sections.
   - Rewrote the `.bat` to self-fetch its companion `.ps1` fresh from the relay EVERY run (via a short embedded PowerShell `Invoke-WebRequest` one-liner) instead of requiring the two files to always travel together.
   - Rebuilt the relay project to confirm the new routes compiled (0 errors).
   - Hit a real deployment snag: a plain `scp` of both files to `~/SecureApp/relay/SecureApp.Relay/data/downloads/` as `dvorakv1` failed outright with "Permission denied" — that directory turned out to be root-owned (the relay CONTAINER, running as root, is what normally writes there).
   - Rather than ask the user for the Pi's own sudo password, added a NEW relay endpoint `POST /admin/upload/ops?name=<filename>` — same `X-Admin-Secret` gate as every existing `/admin/*` route, writes directly into `downloadsDir` from INSIDE the container, with basic path-traversal/invalid-filename rejection on the `name` parameter.
   - Rebuilt+redeployed the relay (triggered `POST /admin/deploy`, then verified it had ACTUALLY taken effect — `docker inspect StartedAt` alone was once again unreliable, matching the already-documented 2026-09-24 gotcha).
   - Uploaded both onboarding files through the new endpoint, verified via direct `curl` that both routes now return `200`, verified the hosted `.ps1`'s SHA-256 hash matched the local source file exactly.
   - Sent the user the single, now self-sufficient `.bat` file, with both the direct file and the new relay URL.
10. **User: "Si mi nerozumel chci jednoduchy bat ktery mi sam stahne a nainstaluje apku a stahne a nainstaluje chybejici komponenty abych nemusel uzivatele nutit stahovat vice souboru manualne a navic vysvetlovat co s tim..."**
    - Recognized this was a real "you misunderstood me" correction, not just a request for more polish: the prior single-self-fetching-`.bat` answer genuinely solved "one file, not two," but didn't address the DEEPER ask — zero files AND zero explanation needed for a THIRD PARTY (not the admin) to set up a brand-new PC, which the `.conf`-file dependency still implicitly required.
    - Rather than guess the architecture, used `AskUserQuestion` to resolve a real security-relevant fork: would the SAME `.bat` always be run by the admin themselves, or does it need to work for a non-admin colleague with zero admin access at all?
    - User picked the non-admin-colleague option explicitly (labeled Recommended).
    - Built the full pickup-code system from scratch this round: new `wireguard_pickup_codes` DB table (mirroring the existing `invites` table's exact shape and code-generation pattern, `Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))`).
    - Extended `/admin/wireguard/clients` to also mint a 24-hour single-use code via the new `CreateWireGuardPickupCode` method and return it in `WireGuardClientResponse` (a new `PickupCode` field, default-valued).
    - New PUBLIC `GET /onboarding/pickup/{code}` endpoint that atomically consumes the code via a single `UPDATE ... WHERE consumed_at_utc IS NULL` statement (race-safe) and returns both the stored `.conf` content AND the member name as JSON.
    - Updated `IRelayAdminService.CreateWireGuardClientAsync`'s return type from `Task<string>` to `Task<(string ConfigurationText, string? PickupCode)>` (matching this codebase's own existing value-tuple convention, used identically by `CreateInviteAsync`).
    - Updated `HttpRelayAdminService` and its private `WireGuardClientResult` record to match, and the one call site in `SettingsViewModel.Updates.cs`'s `CreateWireGuardAccessAsync`.
    - Added `WireGuardPickupCode`/`HasWireGuardPickupCode` plus `CopyWireGuardPickupCodeCommand` (using `Clipboard.SetTextAsync` — this app's first-ever clipboard use), and a new bordered XAML block in `SettingsPage.xaml`.
    - Rewrote `new-pc-onboarding.ps1` to add a `-Code` parameter that redeems the pickup endpoint FIRST (via `Invoke-RestMethod`) before doing anything else, naming the resulting temp `.conf` file after the server-returned member name.
    - Rewrote `new-pc-onboarding.bat` so the interactive code prompt (`set /p "CODE=..."`) is now the PRIMARY flow, falling back to the old local-`.conf` auto-detection only if the prompt is left blank.
    - Verified every branch of the rewritten `.bat` (code as argument, code typed at prompt, blank/skip, `.conf`-fallback) against a dummy `.ps1` via piped `echo` input before trusting any of it.
    - Rebuilt the relay (0 errors), rebuilt+republished the Android client (0 errors on both Android and a Windows sanity build), bumped the version 1.52(55)→1.53(56).
    - Verified the signing certificate matched again, redeployed the relay (same deploy-verification technique, this time the build log visibly ran mid-check, so the check had to be retried after an explicit wait).
    - Ran a full LIVE end-to-end test against the real production relay (create a test client → extract pickup code → redeem → confirm a second redemption attempt correctly fails with `404` → delete the real test wg-easy peer afterward).
    - Uploaded the new APK and both updated ops scripts to the relay, committed everything (`11b8fb2`), pushed, sent the user the final updated `.bat`.

## Key Decisions

- **Diagnosed every single crash from live evidence (a direct SQL query, or a live `logcat` capture) and never from the user's own description alone.**
  - Why: the user's own reports were consistently terse and vague ("Apka pada", "Porad pada", "Jede ale... spadne"), and this project has an established pattern of the user's own hypothesis about a bug's cause turning out to be wrong on direct testing.
  - This discipline paid off concretely THREE times in one session — without it, Crash #2 and Crash #3 would almost certainly have been misdiagnosed as "the same Crash #1 bug just not fully fixed," when each was a genuinely different root cause.
- **Fixed `InputFocusCrashGuard`'s regression with a plain null-conditional invoke rather than reconstructing MAUI's "real" keyboard-hide behavior for this code path.**
  - Why: `ModifyMapping`'s capture mechanism has no reliable way to retrieve the base action for a key resolved only via the mapper's inherited base-class chain — any correct fix for this specific key has to tolerate a null base action.
  - Rejected alternative: digging into why MAUI's inheritance-chain resolution doesn't surface through `ModifyMapping`'s capture step, and building a "complete" fix that still calls the real base behavior — judged not worth the extra time, since Android's native `EditText` focus/IME behavior is OS-level and independent of this particular MAUI-added manager.
- **Conditioned `PdfPig`/`QuestPDF` to Windows-only at the `PackageReference` level, not just at the call-site inside `MauiProgram.cs`.**
  - Why: the feature's own UI-level gate already existed everywhere it mattered, but did nothing to stop the native library from being BUNDLED into the Android APK in the first place.
  - Rejected alternative: guard only the `MauiProgram.cs` line and leave the `PackageReference`s unconditional — would have stopped THIS crash but left ~12MB of dead native code baked into every Android build indefinitely, and the ALREADY-LOGGED `XA0141` 16KB-page-size warning means a future Android OS bump would eventually hard-fail on a library like this anyway.
- **New `UnsupportedLibraryTranslationService` throwing stub, rather than making `ILibraryTranslationService` a nullable constructor dependency everywhere it's injected.**
  - Why: it's a REQUIRED constructor parameter in THREE ViewModels across every platform; converting all three to nullable-with-null-checks would touch meaningfully more call sites for no real benefit, since the UI already fully prevents any non-Windows platform from invoking a method on this service.
  - The throw is intentional and safe — reaching it would itself be proof of a bug in the Windows-only UI gate elsewhere, never a legitimate runtime path.
- **New dedicated `wireguard_pickup_codes` table, deliberately NOT a reuse of the pre-existing `invites` table**, despite both having the exact same one-time-use shape.
  - Why: `invites` is specifically wired into SecureApp's own device-activation flow (`/register` reads it directly) — conflating two conceptually different "what does possessing this code unlock" semantics risks a future maintenance bug far more than the cost of a few duplicated columns in a second small table.
- **`GET /onboarding/pickup/{code}` is public and unauthenticated, despite mutating real state on every successful call.**
  - Why: that IS the entire point of the feature — a non-admin colleague needs to redeem it with absolutely nothing else in hand. Same "anyone already on the network" exposure class this relay already accepts for every existing `/download/*` route.
  - Race-safety is enforced entirely by the single atomic `UPDATE ... WHERE consumed_at_utc IS NULL` statement's own row-count, not by any property of the HTTP verb chosen.
- **Added a new generic `/admin/upload/ops` endpoint rather than asking the user for the Raspberry Pi's own sudo password.**
  - Why: the relay's container already runs as root and can write to the root-owned `downloads/` directory without any host-level permission change; reusing that existing trust boundary (the same `X-Admin-Secret` already used for every other admin action) avoids exchanging a second, unrelated credential (the Pi's own login) just to solve a one-off file-placement problem.
  - Also now genuinely reusable for any future ops file, not a one-off hack specific to this feature.
- **`new-pc-onboarding.bat`'s interactive flow tries a pickup code FIRST, falls back to local-`.conf` auto-detection only if the code prompt is left blank.**
  - Why: the pickup-code path is now the real, intended, advertised route for a non-admin colleague — zero files besides the `.bat` itself.
  - The `.conf`-file fallback is kept working purely so the admin's own prior direct-file workflow doesn't silently break, but is deliberately NOT mentioned in the interactive prompt text, to avoid confusing a non-technical colleague with a second, unexplained option.

- **Version-bumped to 1.53(56) only for the pickup-code feature, not for any of the 4 preceding same-day crash fixes.**
  - Why: this was the first change this session that represented genuinely new client-facing capability (a UI the admin now sees and uses) rather than a pure bug fix to existing behavior.
  - Rejected alternative (implicit, by omission): bumping the build number for every crash fix too — would have been the more disciplined choice in hindsight (see the "same version, 4 binaries" problem in Evidence & Data), but wasn't done in the moment since each fix felt like "still fixing 1.52(55)," not "shipping something new."

## Evidence & Data

**Commit log, this session (chronological, `pi`+`github`), 9 commits total:**

| Commit | What |
|---|---|
| `ef880fb` | Fix app-crashing ObjectDisposedException on Entry focus-change after navigation (Crash #1 fix) |
| `ff9d967` | Release 1.52 (55) — first build under this version; unknowingly still contained Crash #2 |
| `c2e93dd` | Fix app-startup crash on Android: QuestPDF native lib never Windows-gated (Crash #2 fix) |
| `6a319e2` | KNOWN_ISSUES.md: document the QuestPDF startup crash and its fix |
| `4f6a088` | Fix regression: InputFocusCrashGuard crashed on every focus change (Crash #3 fix) |
| `d6afc69` | Fix WireGuard tunnel-name validation (2nd, distinct WG bug) + add new-pc-onboarding.ps1 (Round 1) |
| `4f23a06` | Add double-click .bat launcher for new-pc-onboarding.ps1 (Round 2) |
| `ae6cd36` | Relay: host the new-pc onboarding script on /download, add generic /admin/upload/ops |
| `11b8fb2` | Add self-service WireGuard onboarding: one-time pickup code, no admin secret needed (Round 3) |

**The "same version number, five different binaries" problem — tracked explicitly here since `device_app_logs.app_version` alone cannot disambiguate them:**

| # | Build reason | APK size (bytes) | Version string | SHA256 signing cert (constant throughout) |
|---|---|---|---|---|
| 1 | Crash #1 fix only (unknowingly still had Crash #2) | 82,711,812 | 1.52 (55) | `6E:75:39:28:45:B5:A2:91:6B:90:6C:AE:64:7B:5E:05:38:67:A4:2F:B6:FF:4F:49:43:29:EB:F2:03:41:B2:7C` |
| 2 | + Crash #2 fix (QuestPDF Windows-gated) | 70,294,167 | 1.52 (55) | same |
| 3 | + Crash #3 fix (null-check in InputFocusCrashGuard) | 69,937,329 | 1.52 (55) | same |
| 4 | + 2nd WireGuard-tunnel-name fix | 70,294,167 | 1.52 (55) | same |
| 5 | + full pickup-code feature | 70,302,359 | **1.53 (56)** | same |

Builds 1-4 never bumped the Android build number despite being 4 genuinely distinct binaries — a real process gap: if `device_app_logs.app_version` is ever trusted to mean "this exact code was running," the window covering builds 1-4 is ambiguous without cross-referencing against this handoff's own timestamps.

**The original Crash #1 full stack trace** (device `6b4bf8e9-131b-4304-8368-c831a32c5a2b`, `received_at_utc` 2026-10-06T14:25:36.71Z, `app_version` "1.50 (53)" — the version the user was actually running when they first reported it):

```
ERROR  OnUnhandledException  [Error] Neošetřená výjimka — aplikace se ukončuje.  ObjectDisposedException:
ObjectDisposed_Generic ObjectDisposed_ObjectName_Name, IServiceProvider |
    at Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceProviderEngineScope.GetService(...)
    at Microsoft.Maui.MauiContext.WrappedServiceProvider.GetService(Type serviceType)
    at Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService[HideSoftInputOnTappedChangedManager](...)
    at Microsoft.Maui.Controls.InputView.MapIsFocused(IViewHandler handler, IView view)
    ... (PropertyMapper/ModifyMapping/AppendToMapping generic framework frames) ...
    at Microsoft.Maui.Handlers.ViewHandler.OnPlatformViewFocusChange(Object sender, FocusChangeEventArgs e)
    at Android.Views.View.IOnFocusChangeListenerImplementor.OnFocusChange(View, Boolean)
```

**Crash #2's full stack trace, from a LIVE `logcat` capture** (never reached `device_app_logs` at all — the app died before diagnostics init):

```
E monodroid-assembly: Could not load library '/data/app/.../lib/arm64/libQuestPdfSkia.so'. dlopen failed:
    library "libstdc++.so.6" not found: needed by .../libQuestPdfSkia.so in namespace clns-9
  (repeated identically ~15 times - every static field touch inside the QuestPDF assembly re-attempts it)
E AndroidRuntime: FATAL EXCEPTION: main
E AndroidRuntime: android.runtime.JavaProxyThrowable: [System.TypeInitializationException]:
    TypeInitialization_Type, QuestPDF.Settings
E AndroidRuntime:   at SecureApp.Presentation.MauiProgram.CreateMauiApp + 0x16(Unknown Source)
E AndroidRuntime:   at SecureApp.Presentation.MainApplication.CreateMauiApp + 0x0(Unknown Source)
```

**Crash #3's full stack trace, from a LIVE `logcat` capture** (the same-day regression):

```
E AndroidRuntime: FATAL EXCEPTION: main
E AndroidRuntime: android.runtime.JavaProxyThrowable: [System.NullReferenceException]: Object reference not set to an instance of an object
E AndroidRuntime:   at SecureApp.Presentation.Infrastructure.InputFocusCrashGuard.GuardIsFocused + 0x0(Unknown Source)
E AndroidRuntime:   at Microsoft.Maui.PropertyMapperExtensions+<>c__DisplayClass0_0`2[...].<ModifyMapping>g__newMethod|0 + 0x0(Unknown Source)
```

The `+ 0x0` offset in `GuardIsFocused` — literally the method's first instruction — is exactly consistent with `baseAction(handler, view)` being the very first statement inside the `try` block, invoked on a `null` delegate.

**Wireless `adb` port instability on the S23+ this session** (directly contradicts the existing `adb-wireless-technique` memory's claim of a persistently fixed port 5555):

| Attempt | Port given/tried | Outcome |
|---|---|---|
| 1st | `5555` (from the existing memory, tried first per its own instruction) | Actively refused — host reachable, nothing listening |
| 2nd (after user checked Settings) | `46113` | Connected; used for the first 1.52(55) install |
| 3rd (dropped mid-session) | `38575` | Connected; used for the Crash #2 fix install |
| 4th (dropped again) | `45971` | Connected; used for the Crash #3 fix install + live `uiautomator` verification |
| 5th (dropped again) | `34829` | Connected; used for the 2nd WireGuard fix install |

Four separate times in ONE session, the user had to stop and manually read out Settings → Developer options → Wireless debugging's current IP:port. The existing memory's "always try 5555 first" guidance is now demonstrably stale.

**Pickup-code end-to-end test, run live against the real production Pi relay, 2026-10-07:**

```
$ curl -X POST http://192.168.50.8:8080/admin/wireguard/clients \
    -H "X-Admin-Secret: $SECRET" -d '{"name":"ZZZ Pickup Test"}'
{"configurationText":"\n[Interface]\nPrivateKey = YAPz4vDVZjkpCSvuxinIXty0AgQjZpOI/4X0P6sJlmk=\n
Address = 10.8.0.7/24\nDNS = 1.1.1.1\n\n[Peer]\nPublicKey = UfEzSiu/0r2jzhm0yTXTyw7tcoZCmLVwt3bGJ7LBclU=\n
PresharedKey = G7w5KBih4iey/5PeOIu7mJWnWaH+MERpFojDU4DAUv0=\nAllowedIPs = 0.0.0.0/0, ::/0\n
PersistentKeepalive = 0\nEndpoint = 212.111.15.246:51820",
 "pickupCode":"5b68af404e4d2e30a4452804"}

$ curl http://192.168.50.8:8080/onboarding/pickup/5b68af404e4d2e30a4452804   # 1st redemption
{"configText":"... same config ...","memberName":"ZZZ Pickup Test"}          # 200 OK

$ curl -w "\n%{http_code}\n" http://192.168.50.8:8080/onboarding/pickup/5b68af404e4d2e30a4452804   # 2nd, same code
"Kód je neplatný, již použitý nebo vypršel."
404
```

Test wg-easy peer `da9e47bd-447f-4e3c-a6fb-13276bfa45fa` ("ZZZ Pickup Test") deleted afterward via `DELETE /api/wireguard/client/{id}`, confirmed `{"success":true}`. Two now-inert DB rows (`pending_wireguard_peers`, `wireguard_pickup_codes`, both for "ZZZ Pickup Test") were left in place — `sqlite3` as `dvorakv1` hit the exact same root-owned-file permission wall `/admin/upload/ops` exists to route around (`Error: stepping, attempt to write a readonly database (8)`); not worth a 3rd relay interaction to clean up 2 already-inert test rows.

**Relay deploy verification, 3 separate times this session — `docker inspect StartedAt` alone was unreliable EVERY time, matching the already-documented 2026-09-24 gotcha in `relay/ops/README.md`:**

| Attempt | What `docker inspect -f '{{.State.StartedAt}}'` reported | What was actually true (determined how) |
|---|---|---|
| 1 (onboarding-hosting change) | Stale timestamp (2026-10-05, days old) | Deploy HAD actually happened — confirmed by `POST /admin/upload/ops` correctly returning `401` (proves the route exists) rather than `404` |
| 2 (pickup-code feature, 1st check) | Stale (06:31 UTC) while `deploy.log`'s tail was mid-`docker build` | Deploy was genuinely still IN PROGRESS — confirmed by re-reading `deploy.log`'s tail ~25s later, finding its own `=== Deploy finished at 2026-10-07T06:58:12Z ===` line |
| 2 (pickup-code feature, 2nd check, after waiting) | Now matched (06:58:12Z) | Confirmed independently via the live endpoint returning the NEW custom 404 message text instead of a generic framework 404 |

The one reliable signal, every time: test the REAL endpoint's actual behavior (a custom error message, or a `401`-vs-`404` distinction), cross-checked against `deploy.log`'s own self-reported completion timestamp — never `docker inspect` in isolation.

**`AskUserQuestion` rounds this session — both resolved with the Recommended option:**

| Question asked | Options offered | User picked |
|---|---|---|
| "Co přesně má ten jednoduchý instalátor řešit?" | (1) SecureApp only (2) WireGuard client only (3) Both in one (**Recommended**) | **(3)** |
| "Kdo bude ten .bat ve výsledku spouštět na novém PC?" | (1) Always the admin themselves (2) A non-admin colleague, no admin involvement at all (**Recommended**) | **(2)** |

## Code Analysis

- **`InputView.MapIsFocused`** (MAUI framework internal) is the exact call that invokes `handler.GetService<HideSoftInputOnTappedChangedManager>()`, which throws `ObjectDisposedException` once a page's own handler/DI scope has already been torn down.
  - `ModifyMapping` on `EntryHandler`/`EditorHandler`/`SearchBarHandler.Mapper` for the string key `"IsFocused"` is the ONLY viable interception point — an `AppendToMapping` runs strictly AFTER the base mapping executes, too late to ever catch the throw.
- **`ModifyMapping`'s "capture the existing action for this key" step only checks a handler's own LOCAL `PropertyMapper` dictionary, not its full inherited chain.**
  - For `"IsFocused"` on `Entry`/`Editor`/`SearchBar` specifically, the real mapping is defined on `InputView`'s BASE mapper and resolved via the mapper's own parent-chain mechanism at actual property-update time — never copied into each concrete handler's own dictionary.
  - Any future code using this pattern on a similarly-inherited (not locally-overridden) key should always treat the captured "base action" as potentially `null`.
- **`wireguard /installtunnelservice <path>`** (the real WireGuard-for-Windows CLI command, confirmed via `git.zx2c4.com/wireguard-windows/tree/docs/enterprise.md`, never guessed) creates a Windows service literally named `WireGuardTunnel$<filename-without-extension>`.
  - The tunnel name is DERIVED straight from the filename, meaning the WireGuard-safe sanitizer matters identically whether a `.conf` is imported via the GUI dialog OR via this CLI path — there is no way to bypass WireGuard's own naming constraint entirely.
- **`RelayDatabase.TryConsumeWireGuardPickupCode`** uses a SELECT-then-conditional-UPDATE pattern rather than a single `UPDATE ... RETURNING` statement, matching the exact style already used by the pre-existing `TryConsumeInvite`.
  - The SELECT is purely informational; the UPDATE's own `WHERE consumed_at_utc IS NULL` clause is what actually guarantees atomicity — the method returns `null` unless the UPDATE itself reports `> 0` rows affected, regardless of what the SELECT saw.
- **`Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))`** is this codebase's established one-time-code generation pattern, first used by `CreateInvite` and reused verbatim for `CreateWireGuardPickupCode` — 24 lowercase hex characters, cryptographically random, no custom alphabet or dictation-friendliness attempted, since this code is expected to be typed once from a chat message, not read aloud.
- **`CreateWireGuardClientAsync`'s return-type change** (`Task<string>` → `Task<(string ConfigurationText, string? PickupCode)>`) matches this codebase's own pre-existing convention for multi-value admin results — `CreateInviteAsync` already returns `Task<(string InviteCode, DateTimeOffset ExpiresAtUtc)>` in the exact same interface file.
- **Batch `%VAR%` vs `!VAR!` (delayed expansion)**: inside any `for /f ... do (...)` block, `%VAR%` is expanded exactly ONCE, at the moment the whole parenthesized block is PARSED — not freshly per loop iteration.
  - Reading a variable inside such a block that the SAME block also conditionally sets will always see the value from BEFORE the loop started, every iteration, silently letting the LAST matching iteration win instead of the intended first one.
  - `setlocal enabledelayedexpansion` plus `!VAR!` re-reads the live, current value on each iteration instead.
- **`SettingsPage.xaml`'s new pickup-code block uses the exact same `IsVisible="{Binding Has...}"` derived-boolean-property pattern already used throughout this ViewModel** (e.g. `HasWireGuardConfigText`, `HasWireGuardQrImage`) rather than a `BoolToVisibilityConverter` or similar — consistent with this app's own established "derived property via `OnXChanged` partial method, not an `IValueConverter`" convention, documented inline in `SettingsViewModel.Updates.cs`'s own existing remarks on `CanDownloadUpdate`.
- **`Clipboard.SetTextAsync` requires no platform-specific registration or permission on any of this app's 3 real targets (Android/iOS/Windows)** — unlike, say, `Share.Default.RequestAsync` (already used elsewhere in this app for the `.conf` download), which needed no new ceremony either; both are plain cross-platform MAUI `ApplicationModel`/`DataTransfer` APIs with zero manifest changes required.

## File:Line Quick-Reference for the New Pickup-Code Feature

- `relay/SecureApp.Relay/RelayDatabase.cs` — `wireguard_pickup_codes` table DDL inside `Initialize()`, immediately after `pending_wireguard_peers`'s own DDL block; `CreateWireGuardPickupCode`/`TryConsumeWireGuardPickupCode` methods placed immediately after the pre-existing `CreateInvite`/`TryConsumeInvite` pair, for easy side-by-side comparison.
- `relay/SecureApp.Relay/Program.cs` — the `/admin/wireguard/clients` handler's pickup-code-minting line sits directly above its `return Results.Ok(...)`; the new `/onboarding/pickup/{code}` route is declared immediately below that handler, in the same file region; `/admin/upload/ops` sits immediately after `/download/onboarding.ps1`.
- `src/SecureApp.Presentation/ViewModels/SettingsViewModel.Updates.cs` — the new pickup-code properties/command sit directly below the pre-existing `WireGuardConfigText`/`HasWireGuardConfigText` pair, before `CreateWireGuardAccessAsync`'s own method body.
- `src/SecureApp.Presentation/Views/SettingsPage.xaml` — the new `<Border>` block sits between the existing "⬇ Stáhnout .conf" `<Button>` and the `WireGuardStatusText` `<Label>`, inside the same WireGuard card `<VerticalStackLayout>`.

## Files Changed

### Relay (`SecureApp.Relay`)
- `RelayDatabase.cs` — new `wireguard_pickup_codes` table (schema: `code` PK, `config_text`, `member_name`, `created_at_utc`, `expires_at_utc`, `consumed_at_utc` nullable); new `CreateWireGuardPickupCode(configText, memberName, validFor)` and `TryConsumeWireGuardPickupCode(code)` methods, directly mirroring the pre-existing `CreateInvite`/`TryConsumeInvite` pair's exact shape and style.
- `Contracts.cs` — `WireGuardClientResponse` gained a `PickupCode` field (`string? PickupCode = null`, default-valued for non-breaking growth).
- `Program.cs` — `/admin/wireguard/clients` handler now also calls `CreateWireGuardPickupCode` and includes it in the response; new `GET /onboarding/pickup/{code}` (public, no `IsAdminAuthorized` check); new `GET /download/onboarding` + `GET /download/onboarding.ps1`; new `POST /admin/upload/ops?name=`; `DownloadPageHtml` gained an `onboardingSection` parameter and HTML block; the `/download` handler gained an `onboardingAvailable` existence check.

### Domain
- `Interfaces/Services/IRelayAdminService.cs` — `CreateWireGuardClientAsync`'s return type changed from `Task<string>` to `Task<(string ConfigurationText, string? PickupCode)>`; doc comment extended to explain the new pickup-code mechanism and its security rationale.

### Presentation
- `Infrastructure/InputFocusCrashGuard.cs` — new file this session (Crash #1 fix): `#if ANDROID`-guarded, `ModifyMapping("IsFocused", GuardIsFocused)` on 3 handlers. Patched later the SAME session (Crash #3 fix): `baseAction(handler, view)` → `baseAction?.Invoke(handler, view)`.
- `MauiProgram.cs` — `QuestPDF.Settings.License = ...` wrapped in `#if WINDOWS`; `ILibraryTranslationService` DI registration split into `#if WINDOWS`/`#else` branches; `InputFocusCrashGuard.Register()` call added right after `FontScaling.Register()`.
- `SecureApp.Presentation.csproj` — `PdfPig`/`QuestPDF` `PackageReference`s each gained `Condition="'$(TargetFramework)'=='net10.0-windows10.0.19041.0'"`; version bumped 4 separate times across the session, 1.51(54)→1.52(55) [rebuilt in-place 4 more times under this same version string]→1.53(56).
- `Translation/LocalAiLibraryTranslationService.cs` — entire file body wrapped in `#if WINDOWS`/`#endif`.
- `Translation/UnsupportedLibraryTranslationService.cs` — new file this session: both interface methods throw `PlatformNotSupportedException`, zero QuestPDF/PdfPig dependency.
- `Transport/HttpRelayAdminService.cs` — `CreateWireGuardClientAsync`'s signature and body updated for the tuple return; private `WireGuardClientResult` record gained a matching `PickupCode` field.
- `ViewModels/SettingsViewModel.Updates.cs` — `DownloadWireGuardConfigAsync`'s filename sanitizer replaced with a direct regex against WireGuard's own allowed charset; new `WireGuardPickupCode`/`HasWireGuardPickupCode` property pair; new `CopyWireGuardPickupCodeCommand` (this app's first-ever use of `Clipboard.SetTextAsync`); `CreateWireGuardAccessAsync` updated for the tuple return.
- `Views/SettingsPage.xaml` — new bordered block showing the pickup code + copy button, right after the existing `.conf`-download button.

### Ops scripts
- `relay/ops/new-pc-onboarding.ps1` — new file this session (Round 1), extended twice more the same session (defense-in-depth re-sanitization; then the `-Code` parameter for Round 3).
- `relay/ops/new-pc-onboarding.bat` — new file this session (Round 2), rewritten twice more (self-fetching `.ps1`; then the interactive pickup-code prompt as primary flow).
- `relay/ops/README.md` — new "New PC onboarding" section.

### Docs
- `KNOWN_ISSUES.md` — new item #0 (the QuestPDF startup crash, documented Fixed immediately on discovery); item #4's status updated to note its verification was blocked by item #0 until that one shipped.

### Not touched (growing debt, now worse)
- `DEVELOPMENT_PLAN.md`/`NOTIFICATION_HUB_SPEC.md` — now missing FOUR distinct subsystems: the 2026-10-05 Windows multi-profile login, the local-AI translation feature, this entire session's 3-bug crash-fix saga, and the brand-new self-service WireGuard onboarding system.

## User Feedback & Preferences (REQUIRED — never omit)

- *"Apka pada sprav to prosim..."* — minimal detail given; correctly treated as "go find it from the logs yourself," not a prompt to immediately ask clarifying questions before investigating.
- *"Musis mi updatenout v s23 sam ja to pres apku nedam kdyz pada..."* — an explicit instruction to push fixes directly via `adb` whenever the app itself can't self-update because it's the one that's broken; established the entire direct-deployment workflow used repeatedly for the rest of the crash-fix saga.
- *"Doufam ze nebyl uninstall a install? Ztratim vstchny data...."* — a real, urgent, specific anxiety about data loss expressed mid-task; answered with concrete evidence (`firstInstallTime` unchanged vs `lastUpdateTime` updated) rather than a purely verbal reassurance.
- *"Porad pada..."* / *"Ano porad pada"* / *"Ikdyz apku vymazu z pameti pada"* — each successive short "still crashing" report correctly triggered a FRESH live-log investigation from scratch rather than assuming the previously-diagnosed bug simply hadn't been fixed properly; this discipline is specifically what caught Crash #2 and Crash #3 as genuinely distinct bugs.
- *"Pres tunel"* — a two-word redirect, given when USB wasn't viable, to retry the wireless `adb` path; handled by retrying the known-last-good port first, and when that failed, explaining precisely WHY rather than failing silently.
- *"Si mi nerozumel chci jednoduchy bat ktery mi sam stahne a nainstaluje apku a stahne a nainstaluje chybejici komponenty abych nemusel uzivatele nutit stahovat vice souboru manualne a navic vysvetlovat co s tim..."* — a direct "you misunderstood me" correction. The prior answer wasn't WRONG, it just hadn't addressed the deeper ask — zero files AND zero explanation for a THIRD PARTY. Correctly triggered a scope-clarifying `AskUserQuestion` rather than guessing at a bigger architecture change.
- *"Ok a je to k dispozici v relay? A nemuze se stahnout pouze bat nebo exe ktery vse zaridi tedy stazeni a spusteni instalace a instalace potrebnych komponent po detekci co chybi?"* — pushed explicitly for the ops scripts to be relay-hosted rather than repeatedly re-sent via `SendUserFile`; now genuinely true going forward.
- *"Ok tunel jiz funguje potrebuji ten instalator..."* — a short confirmation that the underlying bug was fixed, immediately followed by a direct ask for the deliverable itself; correctly read as "stop explaining, just hand it over" rather than continuing to narrate the fix.
- **Both `AskUserQuestion` rounds this session** (installer scope; who ultimately runs the `.bat`) were answered with the clearly-labeled Recommended option each time. Small sample, but worth factoring into how confidently a Recommended option can be offered next time a similarly consequential fork comes up.

## Where We're Going

1. **Live-verify the full onboarding flow with an actual second PC and a genuinely separate, non-admin person.**
   - Everything so far was tested by the admin against their own test WireGuard client.
   - The real `.bat` double-click experience on unfamiliar hardware, by someone who didn't build it and holds no admin credentials, has never been observed.
   - Watch specifically for: does the interactive code prompt read clearly without further explanation; does the WireGuard client install cleanly without a UAC-savvy user present; does the tunnel actually come up as a Windows service afterward.
2. **Confirm the local-AI translation feature still works on real Windows hardware** now that it sits behind an `#if WINDOWS` gate.
   - Only sanity-checked in Debug configuration this session.
   - Never run end-to-end against a live Ollama/LM Studio instance, not this session and not the parent session either.
   - A Release Windows build specifically has not been produced or tested since the `#if WINDOWS` refactor landed.
3. **Design direction decision (K/L/M mockups)** — now a 2nd full session with zero progress; worth a direct conversation about whether this still matters or has quietly been superseded.
4. **The 6 remaining carry-over backlog items** — now 3 consecutive sessions deep. Worth a direct conversation: dedicate a session, or formally accept it as permanent background debt rather than silently re-carrying it a 4th time.
5. **Documentation catch-up** — now missing four distinct, real subsystems. The longer this compounds, the more expensive an eventual catch-up pass becomes; worth raising proactively rather than waiting for the user to notice.
6. **Process fix worth proposing**: bump the Android build number on every distinct binary pushed to the relay, even multiple same-day fixes that would otherwise share one version string.
7. **Correct the stale `adb-wireless-technique` memory** — its "fixed port 5555" claim is demonstrably no longer true; the real port changed 4 times in one session this time.

## Risks & Blockers

- **The relay's self-redeploy pipeline continues to require manual, multi-step verification every time** — 3rd consecutive session in this chain to independently hit the same `docker inspect StartedAt`-unreliable gotcha first documented 2026-09-24.
- **Two harmless but real leftover test rows** remain in `pending_wireguard_peers` and `wireguard_pickup_codes` — both already consumed/inert, cleanup blocked by the same root-owned-file permission wall.
  - `WireGuardOnboardingSweepService` may eventually touch the stale `pending_wireguard_peers` row on its own and no-op harmlessly, since the real underlying wg-easy peer is already deleted.
  - Neither row can be queried or matched to any real device, so the risk of accidental confusion with genuine data is low.
- **The pickup-code feature has never been exercised by an actual second human on a genuinely separate real PC.**
  - Only the admin-side creation call and the public redemption endpoint were tested directly via `curl` against the live relay.
  - The full interactive `.bat` double-click experience, end-to-end, on real unfamiliar hardware, has not yet been observed by anyone.
- **The same version number (1.52/55) legitimately shipped 4 different binaries this session** — correlating `device_app_logs.app_version` against "which exact code was running" for this window is ambiguous without this handoff's own table.
- **The new `SettingsPage.xaml` pickup-code block has never actually been seen rendered on a real device or emulator** — only compiled successfully. XAML can compile clean and still render wrong (wrong binding path typo that silently no-ops, a layout overlap, an `IsVisible` that never flips) in ways a 0-error build cannot catch.
- **The Windows build of the translation feature (now behind `#if WINDOWS`) was only sanity-checked in Debug configuration** — a Release Windows build, and an actual runtime exercise of `LocalAiLibraryTranslationService` (vs. the `UnsupportedLibraryTranslationService` stub now used everywhere else), has not happened at all this session, only in the parent session before this refactor existed.

## Open Questions

- Does the 2nd WireGuard tunnel-name fix, combined with the pickup-code flow, actually resolve "PC v praci" for the user in real practice?
- Is the translation feature's new Windows-only gate confirmed working end-to-end on an actual Windows machine?
- Will a non-admin colleague find the `.bat`'s interactive code prompt self-explanatory without further guidance, or will it still need a short "watch me do it once" moment the first time?
- Should the leftover test rows be cleaned up via a proper mechanism, or is this genuinely not worth building?
- Does the new `SettingsPage.xaml` pickup-code block actually render correctly (layout, copy button, visibility toggle) on a real device — genuinely unknown, never visually checked this session?
- Is the 24-hour pickup-code expiry window the right duration in practice? It was chosen as "generous enough to hand someone a code today or tomorrow" with no real user input on the specific number — worth revisiting once a real colleague has actually used one.
- Is a 24-character lowercase-hex code (e.g. `5b68af404e4d2e30a4452804`) comfortable to type correctly from a chat message on a phone screen, or would a shorter/segmented format reduce transcription errors for a real non-technical colleague?

## Dependencies Noted This Session

- **Clipboard access** (`Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard`) — this app's first-ever use of the system clipboard.
  - No prior precedent existed anywhere in this codebase to follow.
  - The API is a plain built-in MAUI call, no new NuGet dependency, no manifest/permission changes needed on any of the 3 real targets.
- **WireGuard's own official documentation** (`www.wireguard.com/install/`, `git.zx2c4.com/wireguard-windows/tree/docs/enterprise.md`, `.../docs/adminregistry.md`) — fetched live via `WebFetch` 3 separate times this session, specifically to avoid guessing at undocumented behavior for a script that runs with admin privileges on a real PC.
  - Confirmed: the real installer URL, the real `/installtunnelservice` CLI syntax and its service-naming behavior, and the explicit ABSENCE of a documented silent-install flag for the bootstrapper `.exe`.
- **The official WireGuard Windows installer** (`https://download.wireguard.com/windows-client/wireguard-installer.exe`) — an external, unversioned-in-this-repo dependency the `.ps1` downloads fresh every time it runs on a machine that needs it; if WireGuard ever changes this URL or the installer's own behavior, `new-pc-onboarding.ps1` would need a corresponding update, with no local test coverage to catch the drift automatically.
- **No new NuGet/package dependencies** were added this session — every fix either REMOVED a dependency's reach (QuestPDF/PdfPig, now Windows-only) or used APIs already present in the MAUI/BCL surface.

**Device identification, the near-miss before any install action (via `adb shell getprop`/`pm list packages`):**

| Property | USB device (`22dab4387e0b7ece`) | Wireless device (correct target) |
|---|---|---|
| `ro.product.model` | `SM-G965F` | `SM-S916B` |
| Real identity | Samsung Galaxy S9+ (a different, older project test phone) | Samsung Galaxy S23+ (Vilém's own phone, the one crashing) |
| Installed SecureApp version at time of check | not checked — correctly abandoned before going further | 1.51 (54), matching the crash report |

**`uiautomator` UI dump excerpt used to find real on-screen tab coordinates for the Crash #3 live verification** (bottom nav bar, `bounds="[x1,y1][x2,y2]"` in device pixels):

```
content-desc="💬 Chaty"     bounds="[216,2067][432,2214]"
content-desc="📂 Soubory"   bounds="[432,2067][648,2214]"
content-desc="☎ Kontakty"  bounds="[648,2067][864,2214]"
content-desc="More"         bounds="[864,2067][1080,2214]"   selected="true"
```

Tap coordinates used were each bar's horizontal+vertical center (e.g. Kontakty: `(648+864)/2=756`, `(2067+2214)/2≈2140`) — the "More" tap at `(972,2140)` was still technically inside that bar's own bounds, so the stray hit on the Claude Code companion app happened at the OS/window-manager level (a different app was frontmost at that exact moment), not from a coordinate-math error.

**Build warning baseline, confirmed unchanged by every rebuild this session (i.e. none of this session's fixes introduced NEW warnings, only the pre-existing classes already known from prior sessions):**

| Target | Warning count | Dominant classes |
|---|---|---|
| Android Release | 113 | `CA1416` (platform-availability, ~90%), `CS1574`/`CS0419` (doc-comment cref ambiguity), 1× `XA4301` (duplicate native lib entry), 1× `XA0141` (16KB page size, pre-existing QuestPDF-adjacent native libs still on Android for OTHER reasons) |
| Windows Debug | 12 | `CS0618` (`DisplayAlert` deprecated), 1× `CS0169` (unused field) |

**The exact multipart field-name mistake and correction for `/admin/upload/android`** (re-discovered this session, not remembered from the parent):

```
# First attempt (wrong):
curl -X POST .../admin/upload/android -F "file=@release.apk" -H "X-Admin-Secret: ..."
  -> {"error":"Missing 'apk' file."}

# Corrected:
curl -X POST .../admin/upload/android -F "apk=@release.apk" -F "versionCode=55" -F "versionName=1.52" -H "X-Admin-Secret: ..."
  -> {"uploaded":true,"sizeBytes":...,"versionCode":55,"versionName":"1.52"}
```

## Verification Status Matrix — Every Major Change This Session

| Change | Verified how | Confidence |
|---|---|---|
| Crash #1 fix (InputFocusCrashGuard, initial) | Build 0 errors both platforms; NOT live-verified before shipping (the regression, Crash #3, was found on the very next retest) | **Low at ship time** — corrected same session |
| Crash #2 diagnosis (QuestPDF) | Live `logcat` capture showing exact `dlopen`/`TypeInitializationException` trace | **High** — direct observation |
| Crash #2 fix | APK zip-content check (native lib absent), signature check, `adb shell am start` + `dumpsys activity activities` showing `ResumedActivity`, fresh `logcat` with zero `FATAL` | **High** — multiple independent signals |
| Crash #3 diagnosis (regression) | Live `logcat` capture showing exact `NullReferenceException` + offset `+0x0` in `GuardIsFocused` | **High** — direct observation |
| Crash #3 fix | `uiautomator`-driven tap-through of Kontakty/Soubory, same PID survived, zero `FATAL` | **High** — personally driven, not user-reported |
| 2nd WireGuard tunnel-name fix | Regex logic verified against WireGuard's own documented charset (WebFetch); rebuilt/reinstalled; NOT re-tested against a real WireGuard import after this specific fix (the pickup-code flow superseded direct testing) | **Medium** — code-correct, not field-confirmed in isolation |
| `new-pc-onboarding.bat`/`.ps1` logic | Isolated tests against dummy scripts for every branch (auto-detect, drag-and-drop, blank, code-as-arg, code-at-prompt, `.conf`-fallback) | **High** for script logic, **None** for the real end-user double-click experience |
| Pickup-code feature (relay side) | Full live create→redeem→re-redeem(fails)→cleanup cycle against production relay | **High** — direct, repeatable, real data |
| Pickup-code feature (client UI) | Build 0 errors; XAML binding paths visually reviewed; NOT exercised on a real device (no screenshot/live run of the new Settings UI block this session) | **Medium** — code-correct, not visually confirmed |
| Relay redeploys (×3) | `deploy.log` completion timestamp + live endpoint behavioral test each time | **High** — after initially being misled by `docker inspect` alone twice |
| Windows build (translation feature's `#if WINDOWS` gate) | `dotnet build` Debug config only, 0 errors | **Low** — never run, only compiled |
| Signing certificate on every single rebuild (6 times this session) | `keytool -printcert -jarfile`, SHA256 compared byte-for-byte each time | **High** — directly observed every time, zero deviation |
| Data safety on every single install (6 times this session) | `dumpsys package` `firstInstallTime` compared before/after each install | **High** — directly observed every time, zero data loss |

## Exact Verbatim Quotes, Hard to Re-Derive From Paraphrase

- *"Apka pada sprav to prosim..."* — the entire initial bug report; note the complete absence of any screen, action, or reproduction detail. Set the precedent for "go find it yourself from the logs" that held for the rest of the crash-fix saga.
- *"Doufam ze nebyl uninstall a install? Ztratim vstchny data...."* — sent WHILE an install was already in progress in the background, not after; the real-time anxiety is why the answer led with concrete `firstInstallTime`/`lastUpdateTime` evidence rather than a general reassurance.
- *"Musim to predat z tel."* (quoted here from the PARENT session, re-confirmed as still operative this session) — the reason every single install this session had to go through wireless `adb` to the phone rather than a simpler path; the admin generates WireGuard configs FROM the phone, not from a PC directly.
- *"Si mi nerozumel..."* — the exact word "nerozumel" (you didn't understand) rather than a softer phrasing is what correctly signaled this was a correction of scope, not a request for refinement of the existing answer.
- *"Spis udelej jednoduchy instalator jednoduche kopirovani rozbaleni, kontrola jestli je nainstalovane co ma byt a kdyz tak stahnout a nainstalovat."* — the exact three-part spec that became the `.ps1`'s three real steps (copy/unpack, check-if-installed, download-and-install-if-missing) — worth re-reading verbatim if the onboarding script ever needs a 4th capability, to check it still satisfies this original framing.

## Exact Signature/API Changes This Session (for future grep/reference)

- `SecureApp.Domain.Interfaces.Services.IRelayAdminService.CreateWireGuardClientAsync`: `Task<string> CreateWireGuardClientAsync(Uri, string, string, CancellationToken)` → `Task<(string ConfigurationText, string? PickupCode)> CreateWireGuardClientAsync(Uri, string, string, CancellationToken)`.
- `SecureApp.Relay.WireGuardClientResponse` (record, `Contracts.cs`): `WireGuardClientResponse(string ConfigurationText)` → `WireGuardClientResponse(string ConfigurationText, string? PickupCode = null)`.
- `SecureApp.Presentation.Transport.HttpRelayAdminService`'s private `WireGuardClientResult` record: `WireGuardClientResult(string ConfigurationText)` → `WireGuardClientResult(string ConfigurationText, string? PickupCode)`.
- New public surface, `SecureApp.Relay.RelayDatabase`: `string CreateWireGuardPickupCode(string configText, string memberName, TimeSpan validFor)`; `(string ConfigText, string MemberName)? TryConsumeWireGuardPickupCode(string code)`.
- New public surface, `SecureApp.Presentation.Translation.UnsupportedLibraryTranslationService : ILibraryTranslationService` — both methods throw `PlatformNotSupportedException`, zero other members.
- `SecureApp.Presentation.ViewModels.SettingsViewModel` (via the `.Updates.cs` partial): gained `WireGuardPickupCode` (string?, observable), `HasWireGuardPickupCode` (bool, derived), `CopyWireGuardPickupCodeCommand` (new `[RelayCommand]`).
- `SecureApp.Presentation.Infrastructure.InputFocusCrashGuard.GuardIsFocused<THandler, TVirtualView>`: body changed from `baseAction(handler, view);` to `baseAction?.Invoke(handler, view);` inside the existing `try` block — signature itself unchanged.
- Relay `Program.cs` route table additions: `GET /onboarding/pickup/{code}` (public), `GET /download/onboarding`, `GET /download/onboarding.ps1`, `POST /admin/upload/ops` (query param `name`, raw body).
- `new-pc-onboarding.ps1` `param()` block: gained `[string]$Code = ""` alongside the pre-existing `$RelayBaseUrl`, `$InstallDir`, `$ConfigPath`.

## Reusable Technique Notes

These are the techniques most expensive to re-discover from scratch if a future session hits the same wall again without this record.

- **PowerShell cannot reliably pass nested double-quotes through to `ssh`/`sqlite3` as a single inline command.**
  - Symptom: a query that's perfectly valid SQL produces a confusing "syntax error near '('" or similar — looks like a SQL problem, is actually a PowerShell-to-native-exe argument-passing problem.
  - Robust pattern, used repeatedly this session: write the SQL/shell script to a LOCAL file with the `Write` tool, `scp` it to the remote host, run it via `ssh host "sqlite3 <db> < /tmp/file.sql"` (or `bash /tmp/file.sh`), delete the remote temp file afterward.
- **Always use `dotnet publish`, never `dotnet build`, when the goal is a fresh signed APK to actually install.**
  - `dotnet build` can succeed with 0 errors and give every appearance of having worked, while leaving the ACTUAL signed `.apk` in `/bin/Release/.../publish/` completely untouched from a previous build.
  - Caught this session only by independently checking the file's own timestamp, and more reliably, by opening it as a zip and verifying an expected change (a removed native library) was actually present.
- **`docker inspect -f '{{.State.StartedAt}}'` alone is not a trustworthy signal for "did my relay redeploy actually take effect."**
  - Always cross-check against (a) `deploy.log`'s own tail for a literal completion line with a timestamp close to "now," AND (b) the real, live endpoint's actual behavior (a distinguishing status code or custom response body).
  - Never trust either signal alone — this session hit the gotcha twice independently and both times `docker inspect` alone was misleading in a different direction (once stale-but-actually-done, once stale-and-genuinely-still-building).
- **Classic batch trap**: `%VAR%` inside a `for /f ... do (...)` block is expanded once at parse time, not per-iteration, so a variable both read AND set within that same block will always see the pre-loop value on every iteration. Fix: `setlocal enabledelayedexpansion` + `!VAR!` instead of `%VAR%` for any variable mutated inside the loop body itself.
- **wg-easy's own API accepts duplicate names and non-ASCII characters with zero validation** (re-confirmed indirectly this session) — every real name-safety constraint relevant to this project lives entirely client-side (this app's own filename sanitizer) or in the WireGuard Windows CLIENT's own tunnel-name validator, never in wg-easy itself.

## Session Timeline Summary (coarse, for orientation)

1. Onboarding + crash report → Crash #1 diagnosed and fixed (`ef880fb`).
2. Direct-to-phone deployment saga begins; wrong-device near-miss caught; 1.52(55) shipped (`ff9d967`).
3. "Still crashing" → Crash #2 (QuestPDF) diagnosed and fixed (`c2e93dd`, `6a319e2`).
4. "Still crashing, different screens now" → Crash #3 (regression) diagnosed and fixed (`4f6a088`).
5. WireGuard `.conf` attachment → 2nd distinct WireGuard bug diagnosed and fixed, Round 1 onboarding script born (`d6afc69`).
6. `.bat` wrapper requested and built (`4f23a06`).
7. Relay-hosting requested and built, new generic upload endpoint added (`ae6cd36`).
8. Full non-admin self-service pickup-code feature requested, scoped via `AskUserQuestion`, and built end-to-end (`11b8fb2`).
9. This handoff.

## Quick Start for Next Session

```powershell
# Reference docs
Get-Content "H:\Visual Studio\C#\Aplikace\plans\handoffs\HANDOFF_standalone-be860f68_ai-translation-wireguard-fix_2026-10-06.md"  # seq 7, still-open carry-over list (6 items remain)

# Confirm current git/relay state
$git = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
& $git log --oneline -10
& $git status

# Check current app version (expect 1.53 / build 56 as of this handoff)
curl.exe -sS http://192.168.50.8:8080/download/android/version

# Onboarding system - verify it's still live
curl.exe -sS -o nul -w "%{http_code}`n" http://192.168.50.8:8080/download/onboarding
curl.exe -sS http://192.168.50.8:8080/download | Select-String onboarding

# Key files if a 4th crash surfaces (same investigation pattern: live logcat first, never guess)
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\Infrastructure\InputFocusCrashGuard.cs"
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\MauiProgram.cs" | Select-String "WINDOWS" -Context 3

# Device log query pattern - PowerShell can't pass nested quotes to ssh/sqlite3 directly;
# write a .sql file, scp it, run via ssh (see "What We Tried" #2 for the exact working pattern)

# Next action - pick based on what the user asks for first:
# 1) Real-world test of the full onboarding flow with an actual second PC/person.
# 2) Live-verify the translation feature's Windows-only gate didn't break anything.
# 3) Finally address the 6-item carry-over backlog (now 3 sessions deep) - or explicitly accept it as permanent debt.
# 4) Documentation catch-up (4 undocumented features deep now).
```

## Session Closed
**Closed at:** 2026-10-07
**Commit:** `b3d1b4c` (pushed to `pi` and `github`)
**Session status:** Handed off to next session
