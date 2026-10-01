# SecureApp: pinch-zoom stutter fix (measured, 5 builds) + Document Library content-approval workflow (new feature, uncommitted)

**Date:** 2026-10-01
**Status:** IN PROGRESS
**Bead(s):** none
**Epic:** none
**Chain:** `standalone-be860f68` seq `2`
**Parent:** `plans/handoffs/HANDOFF_identity-recovery-doc-zoom_2026-09-30.md` (seq 1)
**Prior chain:** `HANDOFF_identity-recovery-doc-zoom_2026-09-30.md` > this

---

## Since Last Handoff

- Parent's #1 priority ("get user confirmation on the v4 zoom tuning, `86befd7`/1.25-26") got answered immediately and negatively: the very first message this session was the user reporting it was **worse** ("Je to horsi"), not fixed — a brand-new 4th distinct zoom bug, exactly as parent's own Risks section warned might happen.
- Parent's prediction in Risks ("treat the next report as potentially revealing a 4th distinct issue") was correct, and so was Open Questions' implicit worry — it took FIVE more builds (1.26/27 through 1.28/31) and a hard pivot from guessing to on-device measurement to actually close it out.
- Parent's other "Where We're Going" items (peer-assisted history resync, S9+/S25/PC version catch-up, background-notification shortcut, `NOTIFICATION_HUB_SPEC.md` §26, Phase 2b/3) were **not touched at all** this session — the zoom fix consumed the first half, then the user pivoted hard into a completely unplanned, unrelated large feature (Document Library content-approval workflow) for the second half. None of parent's deferred items are any closer to done.
- A real discovery this session invalidates part of parent's own "Evidence" section: the S23+'s device id recorded there (`c0b7bfbd-7383-455c-b53c-aa0a0b4b7569`) is **stale** — querying the relay DB directly this session revealed the device was actually re-registered **twice** on 2026-09-29 (not once as parent implied), and its CURRENT id is `6b4bf8e9-131b-4304-8368-c831a32c5a2b`. See Stale References below.
- Net trajectory: priorities shifted hard mid-session, not by plan drift but by the user pasting an entirely new, unplanned feature request. The zoom fix is now genuinely closed (pending final user confirmation on the last build). The new Document Library feature is fully implemented and build-verified but **completely unverified on a live device and not yet committed to git** — this is now the dominant open item, bigger than anything left over from parent.

## Stale References

- `c0b7bfbd-7383-455c-b53c-aa0a0b4b7569` (the S23+'s device id per parent handoff's "Evidence & Data") — **superseded**. The relay's `devices` table shows THREE rows for "Zařízení S23+ uživatele Vilém": `c0b7bfbd-...` (created 2026-09-24, pre-incident), `4f0daec8-3951-4345-918f-1e042ca8e74a` (created 2026-09-29T18:26:51, an intermediate re-registration parent never mentioned), and `6b4bf8e9-131b-4304-8368-c831a32c5a2b` (created 2026-09-29T20:07:57, the device's CURRENT id, confirmed by its `device_app_logs` entries matching this session's own zoom-telemetry timestamps). Any future relay-DB query for "the S23+" must use `6b4bf8e9-...`, not the id in the parent handoff.

## The Goal

SecureApp is a .NET MAUI (Android/iOS/Windows) Clean-Architecture app for a hospital anesthesiology/ICU team: E2EE chat, a shared encrypted document/procedure library, a duty logbook, a company phone directory, a personal work-schedule module, and a notification hub with an Android widget. This session had two almost entirely unrelated halves. **Half 1** (continuing directly from parent): finish verifying and fixing the pinch-to-zoom-and-pan feature in the document viewer, which parent had left in an explicitly unverified state after 3 rounds of bug fixes — this took 5 MORE rounds (v5 through v9) before landing on a measured, evidence-based fix. **Half 2** (a hard pivot mid-session): the user pasted a generic "AIM Medical Mobile App Architecture & Specifications" document (apparently sourced from somewhere else — it assumes a completely different tech stack, e.g. WatermelonDB, a JS/React Native library) and asked to rework the app's Document Library accordingly. After scoping this down heavily via `AskUserQuestion`, a full content-approval workflow (draft → submit → review → approve/reject → publish, with versioning and an audit trail) was designed via `EnterPlanMode` and then fully implemented across Domain/Relay/Presentation — but it is **uncommitted, undeployed, and never tested on a real device**. The session ended mid-flow: the user typed "Pokracujem" (let's continue) and then invoked `/handoff` instead, meaning the very next session should pick up exactly where this one stopped — committing and deploying the new feature.

## Where We Are

- **Zoom fix: DONE, pending final user confirmation.**
  - Current code (`fff579b`, committed+pushed to `pi`+`github`, deployed to the S23+ as 1.28/build 31).
  - NO amplification — `PinchSensitivity` constant deleted entirely.
  - Light exponential smoothing applied: `_smoothedDelta = _smoothedDelta * 0.5 + e.Scale * 0.5`.
  - The `[0.8, 1.25]` implausible-reading reject (added in build 29) is unchanged and still in place.
  - The `_ignoreNextPinchDelta` guard (added in build 27, a gesture-restart priming theory later disproven by measurement) is STILL present in the code but confirmed to have made zero measurable difference — kept because it's harmless, not because it's known to help.

- **Version/build history this session**, in order:
  1. 1.25(26) — parent's end state, unconfirmed.
  2. Reported "horsi" (worse).
  3. 1.26(27, `5a5e824`) — ignore-first-callback + clamp.
  4. Reported "stejne" (same, no change).
  5. 1.26(28, `90b3b3a`) — instrumentation only, no behavior change.
  6. Measured data pulled (#1).
  7. 1.27(29, `a830e68`) — reject-outright fix.
  8. Reported "lepsi ale porad skace" (better but still jumps).
  9. 1.27(30, `2fa6411`) — re-instrumented to measure the NEW jumpiness.
  10. Measured data pulled again (#2).
  11. User explicit frustration ("uz to opravujeme pres 5 nebo 6 build... jen palime tokeny").
  12. 1.28(31, `fff579b`) — amplification removed + smoothing added, current/final.
  13. User asked for a plain-language analysis (answered).
  14. **No final confirmation received yet that 1.28/31 actually fixed it** — session moved to the Document Library pivot immediately after.

- **Document Library feature: fully implemented, 0 build errors, UNCOMMITTED.**
  - `git status` shows 19 modified files + 12 new untracked files, zero of it staged or committed.
  - Nothing has been deployed to the relay.
  - The live relay (which the whole team is connected to) is completely untouched by this feature.

- **Domain layer additions** (all new files, all compiling):
  - `Enums/LibraryDocumentStatus.cs` — Draft/PendingReview/Published/Rejected.
  - `Policies/LibraryReviewPolicy.cs` — `CanReview`, blocks self-review.
  - `Interfaces/Services/ILibraryReviewService.cs` — 7 methods.
  - `ValueObjects/LibraryDocumentSummary.cs`, `LibraryDocumentVersionSummary.cs`, `LibraryDocumentReviewEntry.cs`, `LibraryDocumentDetail.cs`.
  - `ValueObjects/DevicePolicy.cs` edited: `DevicePolicy`/`ManagedDevice` records both gained `IsDocumentReviewer` APPENDED LAST (not inserted), to avoid breaking existing positional constructor calls elsewhere (e.g. `MemberPolicyItem`'s own ctor chain) — a correction made DURING plan review, not something the first draft design got right.

- **Relay additions** (`relay/SecureApp.Relay/`):
  - `RelayDatabase.cs` +391 lines — guarded `ALTER TABLE device_policy ADD COLUMN document_reviewer`.
  - 3 new tables (`library_documents`, `library_document_versions`, `library_document_reviews`) + 2 indexes.
  - ~15 new methods (`CreateLibraryDocument`, `AddLibraryDocumentVersion`, `SubmitLibraryDocumentForReview`, `ApproveLibraryDocumentVersion`, `RejectLibraryDocumentVersion`, `GetLibraryDocument`, `SearchLibraryDocumentsByOwner`, `SearchPendingLibraryDocuments`, `GetLibraryDocumentVersions`, `GetLibraryDocumentReviews`, `SearchLibraryDocumentReviews`, plus updated `GetDevicePolicy`/`SetDevicePolicy`/`GetManagedDevices`).
  - A NEW private `Execute(connection, transaction, sql, params)` overload — the existing one had no transaction parameter at all; needed since several new operations require atomic multi-statement transactions (e.g. approving a version touches both `library_files.is_listed` and `library_documents.status` together).
  - `Program.cs` +131 lines — 8 new endpoints under `/library-documents/*` + `/admin/library-documents/audit`, plus `/me/policy`/`/admin/users`/`/admin/users/{id}/policy` all extended to carry the new field.
  - `Contracts.cs` +40 lines — `DevicePolicyResponse`/`ManagedDeviceDto`/`SetDevicePolicyRequest` extended, 6 new DTOs added.

- **Client additions:**
  - `HttpLibraryReviewService.cs` (new, `src/SecureApp.Presentation/Library/`) — implements `ILibraryReviewService`, same DI/HttpClient pattern as `HttpSharedLibraryService`.
  - `LibraryReviewQueueViewModel.cs` (new) + `LibraryReviewQueuePage.xaml`/`.xaml.cs` (new).
  - `SettingsViewModel.LibraryDocumentAudit.cs` (new) — structural twin of `SettingsViewModel.DocumentDownloads.cs`.
  - Edited: `HttpDevicePolicyService.cs`, `HttpRelayAdminService.cs` (new `SearchLibraryDocumentAuditAsync` + extended `SetDevicePolicyAsync`/`GetManagedDevicesAsync`).
  - Edited: `LibraryViewModel.cs`/`.Actions.cs` (new `IsDocumentReviewer`/`MyDocuments` state, `UploadDraftAsync`/`SubmitDraftForReviewAsync`/`OpenReviewQueueAsync` commands, search-latency telemetry).
  - Edited: `SettingsViewModel.Community.cs`/`MemberPolicyItem` (new `IsDocumentReviewer` bool), `MauiProgram.cs` (DI registrations), `AppShell.xaml.cs` (route registration).
  - Edited: `LibraryPage.xaml` (reviewer button, draft-upload button, "Moje koncepty" section), `SettingsPage.xaml` (reviewer checkbox, audit-search card).

- **Telemetry (Phase C of the plan):**
  - TTI logged via `AppLog.Metric("tti.library_page", ...)` / `"tti.library_review_queue_page"` in both new/edited pages' `OnAppearing`→first-load-complete.
  - Search latency via `AppLog.Metric("search_latency.library", ...)` wrapping `LibraryViewModel.SearchAsync`'s relay call.
  - Scroll-FPS explicitly deferred and documented as such (no cross-platform MAUI frame callback) — matches the video/HLS deferral's own "flag and defer, don't over-engineer" reasoning.

- **Docs updated:**
  - `DEVELOPMENT_PLAN.md` — new bullet under Milestone 5 (full what/why/Domain/relay/UI/Verified narrative, ~400 words) + updated `### Current position` closing sentence.
  - `IMPROVEMENT_PLAN.md` — new `## Phase 5 — Document Library telemetry` checklist + a dated `2026-10-01` session-log narrative entry.

- **Builds run and passed this session:**
  - Relay project alone — 0 errors.
  - Domain project alone — 0 errors.
  - Presentation for `net10.0-windows10.0.19041.0` — 0 errors, confirmed 3 separate times as code was added across phases.
  - Presentation for `net10.0-android` — 0 errors.
  - Full-solution build (`SecureApp.slnx`) — 2 errors, both `CS0246: NativeNotificationService not found` on `net10.0-ios`/`net10.0-maccatalyst` — confirmed PRE-EXISTING and unrelated (that class only ever had `Platforms/Windows/` and `Platforms/Android/` implementations; iOS/MacCatalyst never had one, consistent with this app's historical Windows/Android-only focus per memory `ios-playstore-constraints.md`).

- **Migration safety verified empirically, not assumed:**
  - Copied the LIVE production `relay.db3` to a throwaway `/tmp` file on the Pi.
  - Applied the exact new schema statements (the guarded `ALTER TABLE` + 3 `CREATE TABLE` + 2 `CREATE INDEX`).
  - Confirmed all 4 pre-existing `library_files` rows were byte-identical before and after (same ids, filenames, `is_listed` values).
  - Production `relay.db3` itself was never touched — the test ran entirely against a disposable copy, deleted at the end.

- **One intermediate compile error, self-corrected mid-session:** `HttpLibraryReviewService.cs` initially failed with `CS0103: RelayDeviceVaultKeys not found` (missing `using SecureApp.Presentation.Transport;` — that class is `internal`, same-assembly-only). Fixed immediately, confirmed via rebuild.

- **One Claude Code tooling outage this session:** the Edit tool's safety classifier returned "no verdict (error)" twice in a row on the exact same edit (adding a `_smoothedDelta` field to `DocumentViewerPage.xaml.cs`), then succeeded on the third attempt after the user sent an unrelated message in between. Same transient-outage pattern parent handoff already documented — handled the same correct way (retry once, then wait/move on, don't hammer).

## What We Tried (Chronological)

1. **(carried over from parent, context) v4 state (1.25/26).**
   - `PinchSensitivity = 1.6` exponent amplifying `e.Scale`.
   - `MaxScale = 3`, render resolution 1800×2400.
   - Parent shipped this UNCONFIRMED.
   - User's first message this session: *"Je to horsi pri max zvetseni rychle preblikava obraz z max do normal zvetseni rychle se opakujici"* (it's WORSE — at max zoom it rapidly flickers between max and normal zoom, repeating fast).

2. **v5 (`5a5e824`, 1.26/27) — hypothesis: gesture-restart priming glitch.**
   - Reasoned (not measured) that reaching max zoom needs multiple separate pinch gestures, and the platform's `ScaleGestureDetector` might emit a stale/priming value on the very first `Running` callback after a fresh `Started`.
   - Added `_ignoreNextPinchDelta` (skip exactly one callback per gesture restart).
   - Tightened the raw-delta clamp from unclamped to `[0.5, 2.0]` before amplifying.
   - Built, deployed, asked user to retest.
   - **Result: "Je to stejne"** (it's the same) — zero observed change. First concrete signal that guessing wasn't working.

3. **User pushback**: *"Nuzes nevymyslet kraviny a proste projet kod nebo pomerit co je blbe????"* (Can't you stop inventing nonsense and just go through the code or MEASURE what's wrong?) — explicit, frustrated instruction to stop guessing and start measuring. The pivot point for the rest of the zoom saga.

4. **v6 (`90b3b3a`, 1.26/28) — pure instrumentation, no behavior change.**
   - Added `AppLog.Metric` calls logging every pinch callback.
   - `zoom.pinch.started` — the Started event.
   - `zoom.pinch.ignoredDelta` — the skipped first-callback value.
   - `zoom.pinch.applied` — raw `e.Scale`, amplified delta, before/after `_currentScale`.
   - `zoom.pinch.rejected` — values caught by the `[0.5,2.0]` clamp (at this point still clamp-and-apply, not reject-outright).
   - Built and deployed specifically so the NEXT user repro would produce real data instead of another guess.

5. **Data pull #1** (queried relay DB directly via SSH+sqlite3; device id `6b4bf8e9-...` found by querying the `devices` table for S23+ rows, since the parent handoff's recorded id was stale).
   - Logs from **2026-09-30 15:10:36–37**, ~17ms apart.
   - `e.Scale` alternated almost exactly between `3x` and `0.3x` on literally every single callback, for as long as the user kept pinching near max.
   - `3x` amplified to ~3.0-3.1 → `_currentScale` snapped to `MaxScale`.
   - `0.3x` amplified to ~0.33 → `_currentScale` snapped to `1`.
   - Math check: `Math.Pow(2.0, 1.6)≈3.03`, `Math.Pow(0.5, 1.6)≈0.33` — the v5 clamp bound `[0.5, 2.0]` was mathematically GUARANTEED to still produce a full 1↔3 swing every frame once fed these extreme raw values.
   - This is exactly why v5 showed "no change" — the clamp bound was too loose to matter.

6. **v7 (`a830e68`, 1.27/29) — measured fix: reject, don't clamp-and-apply.**
   - Replaced the clamp-and-multiply with an outright `if (e.Scale < 0.8 || e.Scale > 1.25) return;` — discard the whole callback rather than apply a squashed version of clearly-implausible data.
   - Accidentally removed the by-then-useless `zoom.pinch.applied` logging in the same edit, causing a compile error (`rawDelta`/`before` no longer existed) — caught and fixed before the build.
   - Deployed.
   - **Result: "Lepsi ale porad skace"** (better, but it still jumps) — the catastrophic full-range flicker was gone, but a milder stutter remained.

7. **v8 (`2fa6411`, 1.27/30) — re-instrumented for the NEW symptom.**
   - Re-added `zoom.pinch.applied` logging (correctly this time — `amplified`/`before`/`after` tags).
   - Specifically aimed at seeing what was driving the milder "jumps back and forth" now that the catastrophic case was fixed.
   - Built and deployed for another repro.

8. **Data pull #2** — logs from **2026-09-30 15:46:18–24**, two distinct patterns found:
   - Pattern (a): a long smooth monotonic run of `e.Scale≈0.9` repeated ~15× in a row, `_currentScale` descending cleanly 3.0→2.6→2.3→...→1.0 — this part WAS smooth.
   - Pattern (b): interspersed bursts where `e.Scale` alternated `0.9/1.1/1.2/1.0/0.9/1.1...` within the SAME accepted `[0.8,1.25]` window.
   - Because `PinchSensitivity=1.6` was still amplifying these, a single accepted frame like raw `1.2` → amplified `1.426` produced a **43% zoom jump in one ~17ms frame** (`before=1.3, after=1.854`).
   - A second instance: `before=2.103, after=2.961` — another ~41% single-frame jump from a perfectly plausible raw `1.2`.
   - Conclusion: the amplification itself — not noise outside the accepted range — was now the driver of the residual stutter.

9. **User, explicit and frustrated**: *"Lepsi ale porad skace... muzes prosim uz to vyresit uz to opavujeme pres paty nebo 6 build ... jen palime tokeny na blbem zumu..."* (Better but still jumps... can you please just fix it already, we're on the 5th or 6th build fixing this, we're just burning tokens on a stupid zoom). Second explicit frustration signal; after this, no further instrumentation-only round-trips were done — the v9 fix below was shipped directly from already-gathered data.

10. **v9 (`fff579b`, 1.28/31) — final fix: remove amplification, add smoothing.**
    - Reasoned from BOTH measured data (step 8) AND historical cross-reference (parent's own v2/v3, plain unamplified 1:1 tracking, NEVER reported as jumpy — only slow to reach max zoom over 2-3 gestures, a much smaller complaint).
    - Deleted the `PinchSensitivity` constant and its `Math.Pow` call entirely.
    - Added `_smoothedDelta = _smoothedDelta * 0.5 + e.Scale * 0.5` (simple exponential moving average), reset to `1` on every gesture `Started` and in `ResetZoom`.
    - Applied AFTER the `[0.8,1.25]` reject but BEFORE multiplying into `_currentScale`.
    - Removed all the now-unneeded `zoom.pinch.*` diagnostic logging (root cause confirmed, no longer needed).
    - Hit the SAME Edit-tool "no verdict" classifier outage noted above, mid-edit — retried once more, succeeded.
    - Built (0 errors), deployed to S23+ as 1.28/31, committed (`fff579b`), pushed to `pi`+`github`.
    - **User's final message on this topic asked for an EXPLANATION of why it was stuttering** (not confirmation the fix worked) — answered in plain language: phone reports pinch delta ~60/sec → normal values naturally wobble a little → the 1.6-exponent amplification was blowing up ordinary wobble into big jumps → removed it, added smoothing.
    - **No explicit "yes it's fixed now" was ever received** before the session pivoted to the Document Library request.

11. **Pivot.**
    - A `/usage-credits` local-command output appeared, immediately followed in the SAME user message by a large pasted "AIM Medical Mobile App Architecture & Specifications" document.
    - Spec contents: RBAC with Viewer/Modifier/Reviewer/Admin content-approval roles, WatermelonDB offline storage, H.265/AV1+HLS video, TTI/FPS/search-latency telemetry targets, a "Driven by Plugins" modular architecture.
    - User's request: *"Ok zkusme dokumenty atd predelat podle nasledujiciho promptu,dokumenty zachovej"* (let's try to redo the documents etc. per this prompt, but keep/preserve the existing documents).
    - Closing line: *"Nevim jestli pracujes na tom co jsem poslal?"* (I don't know if you're working on what I sent?) — implies the user believed this had already been sent/pending from outside this conversation, though it had just arrived in-turn.

12. **Immediate technical flag before any code.**
    - WatermelonDB is a JS/React Native library, incompatible with this .NET MAUI/C# app outright — flagged plainly rather than silently trying to approximate it.
    - Also flagged the sheer scale mismatch (RBAC overhaul + plugin engine + video pipeline + telemetry framework = multi-week rewrite of a live production app) before proceeding.

13. **Scoping via `AskUserQuestion`** (3 questions, see the full question/option table in Evidence & Data — only the answers are summarized here):
    - Q1 scope → *"Jen knihovna dokumentu nicmene i dev impruvmet plan take"* (just the Document Library, but also update the dev improvement plan) — free-text, combining the recommended option with an extra ask.
    - Q2 RBAC scope → `"1"` — documents/guidelines only, rest of app keeps its current simple model.
    - Q3 video pipeline → *"Ne, odložit"* (No, defer) — the recommended option.

14. **Research before design** — one `Explore` agent, background, ~45K tokens returned. Confirmed facts (full detail in Evidence & Data's "Pre-implementation research findings"):
    - `Document` entity has NO status/version/author fields at all.
    - `Role` enum is explicitly documented as GLOBAL/single-user/app-wide; Admin and Modifier fully equivalent in `RoleAccessPolicy`.
    - No draft/review/approve concept exists anywhere — "publish" today means only flipping `library_files.is_listed`.
    - No calculator/flowchart/video content-type modeling exists at all (grep for "calculator" returned zero hits).
    - `AppLog` provides generic metric/error logging but no TTI/FPS/search-latency tracking specifically.

15. **Design before code** — `EnterPlanMode`, then one `Plan` subagent given the full research context + the 3 locked scope decisions. Returned a complete design:
    - Reviewer as a NEW orthogonal `device_policy.document_reviewer` boolean, not a 4th `Role` value.
    - 3 new relay tables layered alongside `library_files`, never migrating it.
    - 4-state status model (`Draft→PendingReview→Published|Rejected`), Approve=Publish in one step.
    - Self-review blocked by a new `LibraryReviewPolicy`.
    - Full endpoint table, full client VM/page plan, telemetry plan, docs-update plan, 5-point verification plan.

16. **Plan review and one real correction found.**
    - Directly re-verified the Plan subagent's factual claims against actual code: `device_policy` table schema, `DevicePolicy`/`ManagedDevice` record definitions, `MemberPolicyItem` constructor call site.
    - Found and fixed ONE real bug in the subagent's own draft design: it proposed inserting `IsDocumentReviewer` in the MIDDLE of the `DevicePolicy`/`ManagedDevice` record parameter lists, which would have silently broken every existing positional constructor call in the codebase.
    - Corrected to append-last before writing the final plan file.
    - This is exactly the kind of error the plan-mode "Phase 3: Review — read critical files, verify against actual code" step exists to catch.

17. **Plan approved, implementation began immediately.**
    - `ExitPlanMode` → user approval was automatic/implicit per the tool's own flow; no separate confirmation message was logged before code-writing started.
    - Implementation order: Phase A (Domain+Relay+client plumbing) → build-check → Phase B (client UX/pages) → build-check → Phase C (telemetry) → build-check → Phase D (docs) → migration-safety test → full-solution build-check.
    - Deliberate choice to rebuild after EVERY phase rather than once at the end — catching a compile error immediately after the 2-3 files that caused it is far cheaper than debugging it across a 20-file diff later.

18. **Final state reported to user, explicitly asking before deploying.**
    - Summarized everything built/verified.
    - Explicitly flagged that NOTHING has been deployed or tested live.
    - Asked for go-ahead before pushing a large new feature to the production relay the whole team is connected to.
    - User began typing **"Pokracujem"** (let's continue/proceed) — a clear signal to proceed with deployment.
    - The message was interrupted mid-stream by the user invoking `/handoff` instead.
    - **The user's intent to continue/deploy is on record but was never completed as an actual instruction this session.**

## Key Decisions

- **Stopped guessing, started measuring, after the user's explicit correction** (step 3 above).
  - Every zoom fix from v6 onward was either pure instrumentation or a fix directly justified by data already pulled from the device, never another blind constant tweak.
  - This is the single most important behavioral lesson from this session's first half.
  - Should carry forward to ANY future "feels wrong on device" bug in this app: instrument via the existing `AppLog` pipeline and pull real data before proposing a SECOND fix for the same symptom — don't wait for a third failed attempt to escalate.

- **Removed amplification rather than further tuning its exponent (v9).**
  - Measured data showed amplification was mathematically guaranteed to turn in-range jitter into large swings — not just occasionally, but by its very nature for any value >1 or <1.
  - No exponent value could have both (a) meaningfully reduced "gestures needed to reach max zoom" and (b) avoided amplifying normal jitter.
  - Rather than search for a "just right" exponent, removed it entirely.
  - Solved the original "need 3 gestures" complaint's residual mildness with light smoothing instead.
  - Trades back some speed-to-max-zoom for correctness — which is what parent's own v2/v3 baseline (smooth, slower) already proved acceptable to the user.

- **Reviewer is a new orthogonal `device_policy` boolean, NOT a 4th `Role` enum value.**
  - The user explicitly scoped review capability to documents only.
  - Adding a new `Role` value would have forced every existing `RoleAccessPolicy.IsAllowed`/`CanDeleteMessage` call site across the ENTIRE app (chat, contacts, Rozpis, Logbook) to account for a role that only matters for one feature.
  - The existing `device_policy` table (admin-assigned, per-device, Role-orthogonal, built 2026-09-24 for hidden-tab assignment) is the directly-precedented alternative — rejected adding a new permission axis from scratch.

- **Three new relay tables layered ALONGSIDE `library_files`, never migrating or touching its existing rows.**
  - Chosen specifically because the user required zero data loss/re-upload for existing content.
  - An existing `library_files` row with `is_listed=1` and no matching `library_documents` row simply reads as "Published, no review history" — costs nothing, needs no backfill migration.
  - Verified empirically (see migration-safety test), not just asserted.

- **A version's content reuses the EXISTING encrypted-upload endpoint verbatim** — `ISharedLibraryService.UploadAsync(..., listed:false)`, the exact same call a private chat attachment already uses, rather than building any new crypto/storage path. Zero new crypto code exists anywhere in this feature.

- **Approve IS Publish — one step, not two.**
  - Rejected the generic spec's implied 5-state model (which would separate "Approved" from "Published") in favor of 4 states.
  - This app's existing `TryPublishLibraryFile` is already a single-step unilateral action.
  - A distinct "approved but not yet released" state would be a new concept nothing else in the app has, for no concrete benefit the user asked for.

- **Self-review blocked by a brand-new `LibraryReviewPolicy.CanReview`, not folded into `RoleAccessPolicy.IsAllowed`** — mirrors the exact precedent `RoleAccessPolicy.CanDeleteMessage` already set for "this isn't a plain role→action check, it also depends on the relationship between actor and target."

- **Video/HLS entirely out of scope, not even stubbed** — directly per the user's explicit `AskUserQuestion` answer ("Ne, odložit"); no `DocumentType.Video` enum value, no transcoding interface, nothing built toward it.

- **Scroll-FPS telemetry deferred rather than built with an Android-only hack.**
  - MAUI has no cross-platform frame callback.
  - Judged not worth the platform-specific risk for a metric nothing in this pass strictly needs.
  - Explicitly flagged in both the plan and the docs update rather than silently skipped — the rejected alternative (an Android-only `Choreographer` hook) was considered and explicitly set aside, not forgotten.

- **Rebuilt after every implementation phase, not just once at the end** — deliberate given the sheer size of the change (20 files); catching a compile error immediately after the 2-3 files that caused it is far cheaper than debugging a single end-of-session build failure across the whole diff. The rejected alternative (one big build-check at the very end) was explicitly not taken.

## Evidence & Data

**Zoom build/version/commit table, this session (chronological, all on `main`, pushed to both `pi` and `github`):**

| Commit | Version | What changed | User's report on the PREVIOUS build |
|---|---|---|---|
| (parent's `86befd7`) | 1.25(26) | sensitivity 1.6, MaxScale 3, resolution 1800×2400 | *(unconfirmed at session start)* |
| `5a5e824` | 1.26(27) | v5: ignore first callback after Started + clamp raw delta to [0.5,2.0] | "Je to horsi... rychle preblikava" (worse, rapid flicker) |
| `90b3b3a` | 1.26(28) | v6: instrumentation only (zoom.pinch.started/ignoredDelta/applied/rejected), no behavior change | "Je to stejne" (same, no change) |
| `a830e68` | 1.27(29) | v7: reject outright if e.Scale outside [0.8,1.25], no more clamp-and-apply | *(this build's own data, measured)* |
| `2fa6411` | 1.27(30) | v8: re-added zoom.pinch.applied logging (amplified/before/after) to measure the new symptom | "Lepsi ale porad skace" (better but still jumps) |
| `fff579b` | 1.28(31) | v9: removed PinchSensitivity amplification entirely, added 50/50 exponential smoothing of accepted delta | "Lepsi ale porad skace" (same report, pre-v9 data) |

**Data pull #1 — raw zoom.pinch.applied log excerpt, device `6b4bf8e9-131b-4304-8368-c831a32c5a2b`, 2026-09-30 15:10:36–37 (pre-v7, catastrophic flicker):**
```
15:10:37.264  zoom.pinch.applied  3x    rawDelta=2    before=1  after=3
15:10:37.260  zoom.pinch.applied  0.3x  rawDelta=0,5  before=3  after=1
15:10:37.243  zoom.pinch.applied  3x    rawDelta=2    before=1  after=3
15:10:37.226  zoom.pinch.applied  0.3x  rawDelta=0,5  before=3  after=1
15:10:37.210  zoom.pinch.applied  3x    rawDelta=2    before=1  after=3
```
(Pattern repeats for the full visible log window — literally every other callback, ~17ms apart, alternating `3x`/`0.3x` raw, clamped to the v5 bounds `[0.5,2.0]` → `2.0`/`0.5` → amplified `Math.Pow(2.0,1.6)≈3.03` / `Math.Pow(0.5,1.6)≈0.33` → full 1↔3 swing every single frame. This is the data that disproved the v5 "ignore first callback" hypothesis and proved the v5 clamp bound was mathematically too loose to matter.)

**Data pull #2 — raw zoom.pinch.applied log excerpt, same device, 2026-09-30 15:46:18–24 (post-v7, milder residual stutter):**
```
15:46:22.378  zoom.pinch.applied  1.2x  amplified=1,426  before=1,3    after=1,854   <- +42% in one frame
15:46:22.361  zoom.pinch.applied  0.9x  amplified=0,92   before=1,414  after=1,3
15:46:19.483  zoom.pinch.applied  1.2x  amplified=1,408  before=2,103  after=2,961   <- +41% in one frame
15:46:19.466  zoom.pinch.applied  0.9x  amplified=0,811  before=2,591  after=2,103
15:46:21.511  zoom.pinch.applied  0.9x  amplified=0,868  before=3      after=2,604   <- a long clean monotonic descent also appears in this same pull, 3.0 -> 2.604 -> 2.301 -> 2.063 -> 1.853 -> ... -> 1, all e.Scale~0.9, proving NOT every stretch is jumpy — only where direction keeps flipping frame-to-frame
```
(This data is what directly justified v9: real accepted-range values like raw `1.2`/`0.9` — perfectly plausible human finger jitter — were still being amplified into 40%+ single-frame jumps. Removing amplification was the only fix that addresses this root cause rather than further narrowing an already-correct acceptance window.)

**Relay devices table — the 3 rows for "S23+" found this session (explains the Stale Reference above):**
```
6b4bf8e9-131b-4304-8368-c831a32c5a2b|Zařízení S23+ uživatele Vilém|2026-09-29T20:07:57 (CURRENT)
4f0daec8-3951-4345-918f-1e042ca8e74a|Zařízení S23+ uživatele Vilém|2026-09-29T18:26:51 (intermediate, same day)
c0b7bfbd-7383-455c-b53c-aa0a0b4b7569|Zařízení S23+ uživatele Vilém|2026-09-24T08:33:39 (STALE — this is the one parent handoff recorded)
```

**Migration-safety test output (run against a throwaway copy of the LIVE relay.db3, production file never touched):**
```
--- before: library_files row count + sample ---
4
71a02d55-eae3-4774-a5d6-5d7c847f712d|FLIR0240.jpg|1
cf93e0e8-f8c0-4ac2-bf4c-d585a4ef1b83|Fibrooptická intubace (1).pdf|1
d07bb69e-8cbb-47ca-88fd-9aa0d6f3442b|DOAC vyšetřování hladiny antikoagulačních léků.pdf|1
--- after applying migration: identical ---
4
71a02d55-eae3-4774-a5d6-5d7c847f712d|FLIR0240.jpg|1
cf93e0e8-f8c0-4ac2-bf4c-d585a4ef1b83|Fibrooptická intubace (1).pdf|1
d07bb69e-8cbb-47ca-88fd-9aa0d6f3442b|DOAC vyšetřování hladiny antikoagulačních léků.pdf|1
--- new tables exist and are empty: 0 / 0 / 0 ---
```

**Build status matrix, this session:**

| Target | Result | When |
|---|---|---|
| `relay/SecureApp.Relay.csproj` | 0 errors | after Phase A relay changes |
| `SecureApp.Domain.csproj` alone | 0 errors | after Phase A domain changes |
| `SecureApp.Presentation.csproj` / `net10.0-windows10.0.19041.0` | 1 error (CS0103 RelayDeviceVaultKeys) → fixed → 0 errors | after Phase A client plumbing, x2 |
| `SecureApp.Presentation.csproj` / `net10.0-android` | 0 errors | after Phase B |
| `SecureApp.Presentation.csproj` / `net10.0-windows10.0.19041.0` | 0 errors | after Phase B, again after Phase C |
| `SecureApp.slnx` (full solution, all 4 targets) | 2 errors, both pre-existing/unrelated (`NativeNotificationService` missing on iOS/MacCatalyst) | final check |

**The `AskUserQuestion` scoping call, full question/option text (not just the answers already quoted in "What We Tried" step 13) — this is the exact decision record, reusable verbatim if the next session needs to confirm what was actually offered:**

| Question | Options offered | Answer given |
|---|---|---|
| "Co má tento prompt teď ovlivnit?" (Scope) | (A) "Jen knihovnu dokumentů (doporučeno)" — redo just the shared-document/guidelines/video/calculator module incrementally, content preserved; (B) "Celou aplikaci" — RBAC+plugin+telemetry+media across the WHOLE app, multi-week rewrite; (C) "Jen jako vize do plánu" — no code now, fold into DEVELOPMENT_PLAN/IMPROVEMENT_PLAN as future direction | Free-text: "Jen knihovna dokumentu nicmene i dev impruvmet plan take" (closest to A, but explicitly ALSO wanting C's doc-update side) |
| "Nové role Viewer/Modifier/Reviewer/Admin — kam mají platit?" (RBAC scope) | (1) "Jen dokumenty/guidelines (doporučeno)" — approval workflow scoped to the library only, rest of app keeps current simple model; (2) "Celá aplikace" — replace the role model everywhere | `"1"` |
| "Video strategie (H.265/AV1 + HLS) — má to smysl řešit teď?" (Video pipeline) | (1) "Ne, odložit (doporučeno)" — current simple encrypted video attachments suffice, HLS/transcoding is its own big project needing a server-side transcoder; (2) "Ano, chci to navrhnout hned" | "Ne, odložit (doporučeno)" |

**The original pasted "AIM Medical Mobile App Architecture & Specifications" document, key excerpts (primary source — too expensive to re-paste from scrollback if needed again; full document was longer, this is what was actually quoted/acted on):**
```
1. Project Overview & Core Philosophy
Build a high-performance, modular mobile application for Anesthesiology and Intensive Care (AIM).
Primary Non-Functional Requirement: Zero-latency experience (< 50ms interaction response).
Architecture Style: Offline-first, Server-Driven UI / Plugin-based Modular Architecture.

2. Role-Based Access Control (RBAC)
Viewer (End-User/Clinician): Read-only, bookmark, personal offline notes.
Modifier (Author/Content Creator): Draft/edit/update content, upload media, configure calculators.
  Cannot publish directly to production.
Reviewer (Medical Board/Board Certifier): Reviews draft submissions, validates medical accuracy,
  approves/rejects changes, signs off on publication versions.
Admin: Full access — users, roles, modules, telemetry, config.

3. Performance & Low-Latency Requirements
Data Storage: Offline-First SQLite / WatermelonDB.
Media: SVGs for flowcharts; H.265/AV1 + HLS adaptive streaming for video, background download.
Telemetry (Day 1): TTI per screen (<100ms target), FPS during scroll (60-120 target),
  Search Latency (<30ms for local indexed query), full audit trail (user id + timestamp + diffs).

4. Modular Architecture (Driven by Plugins)
The core engine must be detached from content domain modules.
```
(The document was pasted mid-message alongside an unrelated `/usage-credits` local-command output, and the user's own framing line was: *"Ok zkusme dokumenty atd predelat podle nasledujiciho promptu,dokumenty zachovej"* — "zachovej" = preserve the existing documents through whatever change happens.)

**Relay endpoint table — new `/library-documents/*` family added this session (`relay/SecureApp.Relay/Program.cs`):**

| Method + path | Auth | Purpose |
|---|---|---|
| `POST /library-documents` | Device | Create Draft from an already-uploaded (listed:false) library file |
| `POST /library-documents/{id}/versions` | Device | Stage a new version, resets status to Draft |
| `POST /library-documents/{id}/submit` | Device | Draft -> PendingReview |
| `GET /library-documents/mine` | Device | "Moje koncepty" — created or submitted by caller |
| `GET /library-documents/pending` | Device + reviewer/admin | The review queue |
| `GET /library-documents/{id}` | Device | Full detail: versions + review history |
| `POST /library-documents/{id}/review` | Device + `LibraryReviewPolicy.CanReview` | Approve (publishes) or Reject (comment required) |
| `GET /admin/library-documents/audit` | `X-Admin-Secret` | Admin search over all review decisions — twin of `GET /admin/document-downloads` |

**Pre-implementation research findings (Explore agent, ~45K tokens, the factual basis the whole design rests on — expensive to re-derive, kept verbatim-ish):**
- `Document` entity (`src/SecureApp.Domain/Entities/Document.cs`) full property list as of this session: `Title, FileName, DocumentType, OriginalSizeBytes, ContentHash (FileHash), EncryptedContent (EncryptedPayload), FolderId, IsFavorite, Tags, SourceLibraryFileId`. Mutators `Rename, MoveTo, ReplaceContent (overwrites in place, no version history kept), SetTags, ToggleFavorite` — confirmed NO status/state/author/version fields existed anywhere before this session.
- `DocumentType` enum: `Unknown=0, Pdf, Image, Spreadsheet, PlainText, Other` — no Calculator/Video/Flowchart variant; grep for "calculator"/"Calculator" across the whole repo returned zero hits before this session.
- `Role` enum (`Admin=0, Modifier=1, Viewer=2`) confirmed explicitly documented in its own doc comment as a GLOBAL, single-user, app-wide role — "per-folder or per-document role assignment was deliberately deferred," citing DEVELOPMENT_PLAN.md's own Milestone 3 note. This is the fact that justified NOT adding a 4th Role value for Reviewer.
- `RoleAccessPolicy.IsAllowed`: Viewer denied everything except `RecordLogbookProcedure`/`ViewLogbookStatistics`; Admin and Modifier are FULLY EQUIVALENT on every single `RbacAction` — no existing Admin-only tier in-app (Admin-only server actions like relay-deploy/bulk-import are gated by the wholly separate `X-Admin-Secret` mechanism, not this enum/matrix at all).
- `RoleAccessPolicy.CanDeleteMessage(actorRole, isOwnMessage, senderRole)` — the one existing precedent for "a rule that depends on more than a flat role→action lookup," directly modeled by the new `LibraryReviewPolicy.CanReview`.
- The shared/community Library (relay `library_files` table) BEFORE this session: `id, folder_path, file_name, tags_json, size_bytes, content_hash, uploaded_by_device_id, uploaded_at_utc, is_listed`. Content blobs live on disk (`LibraryFilesDirectory`), never in SQLite. `is_listed` already distinguished "real published library file" (1) from "private chat attachment" (0) — same encrypted storage, just hidden from the browser. This existing dual-use of one table/flag is exactly what the new workflow layers on top of.
- Confirmed BEFORE this session: zero draft/review/approve concept existed anywhere. "Publish" meant only flipping `is_listed`, unilaterally, by the uploader or an admin-override — no second-party review, no version history (`ReplaceContent`/`UPDATE` just overwrites), no audit trail beyond unrelated download-tracking (`document_downloads`, built 2026-09-30, the session before this one).
- `AppLog` (`src/SecureApp.Presentation/Infrastructure/AppLog.cs`) confirmed to already provide `Error`/`Metric`/`Event`/`Time` generic instrumentation, shipped to the relay via `AppLogUploader`/`POST /diagnostics/applog`, admin-readable via `GET /admin/applog/{deviceId}` — but NO existing TTI, FPS, or search-latency tracking anywhere in the app before this session (confirmed via a targeted grep across the whole Presentation project).
- `DEVELOPMENT_PLAN.md` structure confirmed: `# SecureApp...` > `## Development Roadmap...` > `### Milestones & Tasks Tracker` with `#### MILESTONE 1..5` (5 = E2EE chat + shared file library, "DONE + shared file library follow-up") — this is why the new bullet was added as a sub-bullet under Milestone 5 rather than a new Milestone 6 heading (a literal "Milestone 6" label already exists informally inside Milestone 5's own task numbering from an earlier, never-renumbered plan, so a fresh heading would collide).
- `IMPROVEMENT_PLAN.md` structure confirmed: phase+session-journal hybrid (`## Phase 0..4`, dated `## Session <date> — ...` entries) — this is why the new content landed as both a `## Phase 5` checklist AND a dated session narrative, matching the file's own established dual pattern.

## Code Analysis

- `DocumentViewerPage.xaml.cs` final (v9) pinch handler shape: `OnPinchUpdated` — `Started` sets `_ignoreNextPinchDelta=true` and resets `_smoothedDelta=1`; first post-Started `Running` callback is skipped; subsequent callbacks reject (`return`, no-op) if `e.Scale < 0.8 || e.Scale > 1.25`; otherwise `_smoothedDelta = _smoothedDelta * 0.5 + e.Scale * 0.5; _currentScale = Math.Clamp(_currentScale * _smoothedDelta, 1, MaxScale);`. `MaxScale = 3` (unchanged from parent's v4). `PinchSensitivity` constant REMOVED entirely (was `1.6`).
- `RelayDatabase.cs` new `Execute(SqliteConnection, SqliteTransaction, string, params)` overload added specifically because `ApproveLibraryDocumentVersion`/`RejectLibraryDocumentVersion` need atomic multi-statement transactions (flip old file's `is_listed=0`, flip new file's `is_listed=1`, update `library_documents.status`, insert into `library_document_reviews`, all-or-nothing).
- `LibraryReviewPolicy.CanReview(bool isDocumentReviewer, bool isAdmin, string reviewerDeviceId, string submitterDeviceId) => (isDocumentReviewer || isAdmin) && !string.Equals(reviewerDeviceId, submitterDeviceId, StringComparison.OrdinalIgnoreCase);` — pure, no I/O, matches `RoleAccessPolicy.CanDeleteMessage`'s existing shape.
- `DevicePolicy(Role? Role, IReadOnlyList<string> HiddenTabs, bool IsDocumentReviewer = false)` and `ManagedDevice(..., bool IsDocumentReviewer = false)` — field appended LAST with a default, specifically to not break existing positional constructor calls (`MemberPolicyItem`'s own ctor chain was the concrete call site that would have silently gotten the wrong value if this had been inserted mid-list).
- `IRelayAdminService.SetDevicePolicyAsync` signature changed from `(..., IReadOnlyList<string> hiddenTabs, CancellationToken ct = default)` to `(..., IReadOnlyList<string> hiddenTabs, bool? isDocumentReviewer = null, CancellationToken ct = default)` — optional/nullable so a null means "leave reviewer capability unchanged," and all existing call sites compile unchanged since it's appended before the (also-optional) `ct`.
- `library_documents.status` stored as plain TEXT (`"Draft"`/`"PendingReview"`/`"Published"`/`"Rejected"`), not an integer — matches this codebase's existing `document_downloads`-family convention of readable TEXT columns over opaque integers for anything an admin might ever query directly via sqlite3.
- "The latest version" of a document is ALWAYS the `library_document_versions` row with `MAX(version_number)` for that document — there is deliberately no separate "pending version" pointer column, since only one version is ever in flight at a time (enforced by `SubmitLibraryDocumentForReview` requiring `status='Draft'` first, and `Approve`/`RejectLibraryDocumentVersion` requiring `status='PendingReview'`).

## Files Changed

### Zoom fix (committed, `5a5e824`→`fff579b`)
- `src/SecureApp.Presentation/Views/DocumentViewerPage.xaml.cs` — rewritten pinch-handling logic 4 times this session (v5→v6→v7→v8→v9 collapsed into the 5 commits above); final state has no amplification, light exponential smoothing, `[0.8,1.25]` implausible-reading reject, first-callback-after-Started skip.
- `src/SecureApp.Presentation/SecureApp.Presentation.csproj` — version bumps 1.25(26)→1.28(31), 6 times.

### Document Library workflow — Domain (uncommitted, new files)
- `src/SecureApp.Domain/Enums/LibraryDocumentStatus.cs` — `Draft, PendingReview, Published, Rejected`.
- `src/SecureApp.Domain/Policies/LibraryReviewPolicy.cs` — one static method, `CanReview`.
- `src/SecureApp.Domain/Interfaces/Services/ILibraryReviewService.cs` — 7 methods: `CreateDraftAsync, AddVersionAsync, SubmitForReviewAsync, GetMyDocumentsAsync, GetPendingReviewAsync, GetDetailAsync, ReviewAsync`.
- `src/SecureApp.Domain/ValueObjects/LibraryDocumentSummary.cs` — the stable per-document record (id, title, folder, status, current version/file pointers, created/submitted-by device ids + timestamps).
- `src/SecureApp.Domain/ValueObjects/LibraryDocumentVersionSummary.cs` — one revision's record (id, parent doc id, version number, library_file_id, author, timestamp, change note).
- `src/SecureApp.Domain/ValueObjects/LibraryDocumentReviewEntry.cs` — one audit-trail row (id, doc id, version id, reviewer, decision, comment, timestamp) — direct twin of `DocumentDownloadEntry`.
- `src/SecureApp.Domain/ValueObjects/LibraryDocumentDetail.cs` — bundles a summary + its full version list + full review list, for the detail/audit screens.

### Document Library workflow — Domain (uncommitted, edited)
- `src/SecureApp.Domain/ValueObjects/DevicePolicy.cs` — `DevicePolicy` and `ManagedDevice` records both gained `bool IsDocumentReviewer = false` APPENDED LAST; `DevicePolicy.Unmanaged` updated to pass `false` for it.
- `src/SecureApp.Domain/Interfaces/Services/IRelayAdminService.cs` — `SetDevicePolicyAsync` gained `bool? isDocumentReviewer = null` (before the trailing `CancellationToken`); new `SearchLibraryDocumentAuditAsync(Uri, string adminSecret, string? query, CancellationToken)` method, twin of the existing `SearchDocumentDownloadsAsync`.

### Document Library workflow — Relay (uncommitted, edited)
- `relay/SecureApp.Relay/RelayDatabase.cs` (+391 lines) — guarded `ALTER TABLE device_policy ADD COLUMN document_reviewer`; 3 new `CREATE TABLE` + 2 `CREATE INDEX`; new record types `LibraryDocumentRecord`/`LibraryDocumentVersionRecord`/`LibraryDocumentReviewRecord` declared near the existing `LibraryFileRecord`; new private `Execute(SqliteConnection, SqliteTransaction, string, params)` transaction-aware overload; ~15 new public methods (`CreateLibraryDocument`, `AddLibraryDocumentVersion`, `InsertNextVersion` (private helper), `SubmitLibraryDocumentForReview`, `ApproveLibraryDocumentVersion`, `RejectLibraryDocumentVersion`, `InsertReview` (private helper), `GetLibraryDocument` (two overloads, one transaction-aware), `GetLatestVersion` (private), `SearchLibraryDocumentsByOwner`, `SearchPendingLibraryDocuments`, `GetLibraryDocumentVersions`, `GetLibraryDocumentReviews`, `SearchLibraryDocumentReviews`, plus 3 private `Read*` row-mapping helpers); `GetDevicePolicy`/`SetDevicePolicy`/`GetManagedDevices` all updated to carry the new column/field through their existing tuple return shapes.
- `relay/SecureApp.Relay/Program.cs` (+131 lines) — `using SecureApp.Domain.Policies;` added; 8 new `app.Map*` endpoints under `/library-documents/*` plus `/admin/library-documents/audit` (see the endpoint table above); `/me/policy`, `/admin/users`, `/admin/users/{id:guid}/policy` all extended to read/write the new field; 3 new `ToLibraryDocument*Dto` mapping functions added near the existing `ToLibraryFileDto`.
- `relay/SecureApp.Relay/Contracts.cs` (+40 lines) — `DevicePolicyResponse`, `ManagedDeviceDto`, `SetDevicePolicyRequest` each gained the reviewer field (appended/optional); 6 new records: `LibraryDocumentDto`, `LibraryDocumentVersionDto`, `LibraryDocumentReviewEntryDto`, `LibraryDocumentDetailDto`, `CreateLibraryDocumentRequest`, `AddLibraryDocumentVersionRequest`, `ReviewLibraryDocumentRequest` (that's actually 7 — the count includes both request and response shapes for the new workflow).

### Document Library workflow — Client (uncommitted, new files)
- `src/SecureApp.Presentation/Library/HttpLibraryReviewService.cs` — implements `ILibraryReviewService`; own private DTO records (duplicated from the relay's, not shared — same cross-boundary convention `HttpRelayAdminService` already established); own `AddDeviceAuthAsync`/`GetHttpEndpointAsync` helpers (duplicated per this codebase's stated per-`Http*Service` convention, not shared via a base class).
- `src/SecureApp.Presentation/ViewModels/LibraryReviewQueueViewModel.cs` — `PendingItems` collection, `LoadCommand`/`OpenItemCommand`/`ApproveCommand`/`RejectCommand`; `OpenItemAsync` fetches the detail, takes the highest-`VersionNumber` entry, calls the EXISTING `ISharedLibraryService.DownloadAndImportAsync` + navigates to the EXISTING `DocumentViewerPage` — zero new viewer code.
- `src/SecureApp.Presentation/ViewModels/SettingsViewModel.LibraryDocumentAudit.cs` — `LibraryDocumentAuditSearchText`/`Results`/`SearchLibraryDocumentAuditCommand`, structural twin of `SettingsViewModel.DocumentDownloads.cs`.
- `src/SecureApp.Presentation/Views/LibraryReviewQueuePage.xaml`, `.xaml.cs` — `RefreshView`+`BindableLayout` list of `ReviewQueueItem` cards, each with Otevřít/Schválit/Zamítnout buttons wired via `RelativeSource AncestorType` bindings back to the page's ViewModel (same established pattern as `ContactsPage.xaml`'s per-item commands); TTI telemetry in code-behind.

### Document Library workflow — Client (uncommitted, edited)
- `src/SecureApp.Presentation/Transport/HttpDevicePolicyService.cs` — `PolicyDto` record gained `DocumentReviewer`; `GetMyPolicyAsync` passes it into the returned `DevicePolicy`.
- `src/SecureApp.Presentation/Transport/HttpRelayAdminService.cs` — `ManagedDeviceSummary` gained `DocumentReviewer`; `SetDevicePolicyAsync`/`GetManagedDevicesAsync` updated; new `SearchLibraryDocumentAuditAsync` method added.
- `src/SecureApp.Presentation/ViewModels/LibraryViewModel.cs` — new fields `MyDocuments`/`HasMyDocuments`/`IsDocumentReviewer`; constructor gained `ILibraryReviewService`/`IDevicePolicyService` params; `SearchAsync` now also calls new `RefreshReviewWorkflowStateAsync` (best-effort, never blocks ordinary search) and logs `search_latency.library` via `AppLog.Metric`; new `ToDraftItem`/`StatusLabel` helpers; new `LibraryDocumentDraftItem` record.
- `src/SecureApp.Presentation/ViewModels/LibraryViewModel.Actions.cs` — new `UploadDraftAsync` (FilePicker + `DisplayPromptAsync` for a title + `UploadAsync(..., listed:false)` + `CreateDraftAsync`), `SubmitDraftForReviewAsync(LibraryDocumentDraftItem?)`, `OpenReviewQueueAsync` (Shell navigation) commands.
- `src/SecureApp.Presentation/ViewModels/SettingsViewModel.Community.cs` — `MemberPolicyItem` ctor gained `isDocumentReviewer = false` param + `IsDocumentReviewer` observable property; `LoadManagedMembersAsync`/`SaveMemberPolicyAsync` updated to read/write it through `SetDevicePolicyAsync`.
- `src/SecureApp.Presentation/Views/LibraryPage.xaml` — header `Grid` gained a "📋 Ke schválení" button (`IsVisible="{Binding IsDocumentReviewer}"`); upload card split into two buttons ("⬆ Nahrát" direct-publish unchanged, "📝 Jako koncept" new); new "Moje koncepty" `Border`+`BindableLayout` card with per-item status `Chip` + conditional "Odeslat ke schválení" button.
- `src/SecureApp.Presentation/Views/LibraryPage.xaml.cs` — TTI telemetry added (`PropertyChanged` subscription watching `IsLoading`, logged once per page visit via a nulled-out `Stopwatch` guard).
- `src/SecureApp.Presentation/Views/SettingsPage.xaml` — member-row template gained a "Reviewer dokumentů" `Switch`; new "Log schvalování dokumentů" `Border` card (search box + `CollectionView`), structural twin of the existing download-log card, inserted between it and the "Správa členů" card.
- `src/SecureApp.Presentation/MauiProgram.cs` — `AddSingleton<ILibraryReviewService, HttpLibraryReviewService>()`, `AddTransient<LibraryReviewQueueViewModel>()`, `AddTransient<LibraryReviewQueuePage>()`.
- `src/SecureApp.Presentation/AppShell.xaml.cs` — `Routing.RegisterRoute(nameof(LibraryReviewQueuePage), typeof(LibraryReviewQueuePage))`.

### Documentation (uncommitted)
- `DEVELOPMENT_PLAN.md` — Milestone 5 bullet + Current position sentence
- `IMPROVEMENT_PLAN.md` — Phase 5 + dated session entry
- `plans/handoffs/HANDOFF_notifications-background-widget_2026-09-28.md` — pre-existing unrelated local diff, NOT from this session (carried over, same as parent handoff noted)
- `.claude/settings.local.json` — pre-existing unrelated local diff, NOT from this session

### Not yet created
- `plans/polished-moseying-koala.md` — the approved implementation plan file (written by the plan-mode workflow itself, at `C:\Users\dvora\.claude\plans\polished-moseying-koala.md`, OUTSIDE the repo) — worth reading if continuing implementation details, though this handoff's own "Where We Are"/"Code Analysis" sections should cover everything needed.

## User Feedback & Preferences (REQUIRED — never omit)

- *"Je to horsi pri max zvetseni rychle preblikava obraz z max do normal zvetseni rychle se opakujici"* — the opening report that v4 (parent's final state) was worse, not fixed.
- *"Je to stejne"* — terse, after v5; zero observed change.
- *"Nuzes nevymyslet kraviny a proste projet kod nebo pomerit co je blbe????"* — explicit, frustrated instruction to stop guessing and measure instead. The single most important process correction this session.
- *"Lepsi ale porad skace..."* — after v7; progress acknowledged but not satisfied.
- *"Lepsi ale porad skace... muzes prosim uz to vyresit uz to opavujeme pres paty nebo 6 build ... jen palime tokeny na blbem zumu..."* — second explicit frustration, directly naming the build count and token cost; the signal to stop iterating-with-instrumentation and ship a final reasoned fix from already-gathered data.
- *"Ok zanalyzuj proc se to pri tom zvetsovani ev zmensovani seka"* — after v9 shipped, asked for an explanation rather than confirming the fix; answered in plain, non-technical language (phone reports ~60 values/sec, normal wobble exists, the amplification was exaggerating it, removed).
- *"Ok zkusme dokumenty atd predelat podle nasledujiciho promptu,dokumenty zachovej"* (pasted alongside the full AIM spec) — the pivot request; "zachovej" (preserve) explicitly meant keep existing document content through whatever redesign happens.
- *"Nevim jestli pracujes na tom co jsem poslal?"* — suggests the user believed the spec had been sent earlier/elsewhere; it had just arrived in this exact message.
- `AskUserQuestion` answers (verbatim): Q1 scope = *"Jen knihovna dokumentu nicmene i dev impruvmet plan take"*; Q2 RBAC scope = `"1"` (documents-only); Q3 video = *"Ne, odložit (doporučeno)"*.
- *"Pokracujem"* — final message of the session, cut off mid-send by the `/handoff` invocation; clearly signals intent to proceed with deployment, but was never completed as an actual instruction.
- (Process preference, reconfirmed from this session's own experience, not newly stated but newly RE-learned the hard way): the user wants measured fixes, not sequential guesses, once a bug survives one attempted fix — escalate to instrumentation immediately on the SECOND failed attempt, don't wait for a third.

## Where We're Going

1. **Get the user's actual confirmation on "Pokracujem"** — do they want to proceed with committing + deploying the Document Library feature right now? This was clearly their intent but was never confirmed as a completed instruction.
2. **Commit the Document Library workflow** (19 modified + 12 new files, currently fully uncommitted) — a sensible commit split would be: Domain layer, Relay layer, Client layer, Docs — or one large commit if the user prefers a single logical unit; ask or use judgment based on how this session's other large features were committed (they tended to be one commit per logical milestone).
3. **Deploy to the relay** (`git push pi main` → relay auto-deploy marker → systemd watcher rebuild) and run the plan's own verification step 2 (relay smoke test: draft → add version → submit → reviewer self-review rejected 403 → different-device reviewer approves → confirm `GET /library/files` shows only the latest version → reject-with-empty-comment rejected 400 → audit endpoint shows history) BEFORE touching any real device.
4. **Build + deploy the Android client** (1.29 or similar next version) to the S23+ and at least one other device (ideally Petr's S25, so there are two distinct devices to actually exercise the submit/review split — a single device can't meaningfully test "a different device reviews this").
5. **Run the plan's end-to-end manual test**: Modifier device drafts+submits, Reviewer device (needs an admin to flip `IsDocumentReviewer=true` for it first via the new Settings checkbox) opens/rejects-with-comment, original device sees Rejected+comment, stages a new version, resubmits, reviewer approves, confirm it now appears in ordinary `LibraryPage` search for a plain Viewer.
6. **Get final confirmation on the zoom fix (v9, 1.28/31)** — never explicitly confirmed this session; low priority compared to the Document Library work but still an open loop.
7. Everything still carried over unresolved from parent (peer-assisted history resync, S9+/S25/PC version catch-up — now ALSO blocking item 4 above since Petr's S25 needs the new build anyway, background-notification shortcut, `NOTIFICATION_HUB_SPEC.md` §26, Phase 2b/3) remains untouched.

## Risks & Blockers

- **The Document Library feature is a large, untested, uncommitted change to a live production app's data model.**
  - Relay schema changes (even additive ones) to a system the whole team is actively connected to warrant care.
  - Migration safety was verified, but the actual REVIEW WORKFLOW LOGIC (approve/reject state transitions, self-review blocking, the dual `is_listed` flip) has never been exercised against a running relay instance or a real device, only compiled.
  - The plan's own verification step 2 (a relay smoke test: draft→version→submit→self-review-rejected-403→different-device-approve→confirm listing→reject-with-empty-comment-400→audit-shows-history) was NEVER run this session — only step 1 (migration safety) was completed.

- **No device currently has `IsDocumentReviewer=true`** — until an admin uses the new Settings checkbox at least once, the entire review half of the workflow is unreachable/untestable even after deployment.

- **A reviewer opening a pending submission downloads+imports it as an ordinary LOCAL `Document`** via the existing `DownloadAndImportAsync` path (the same reuse that avoided writing new viewer code). If that submission is then REJECTED, the reviewer's device still has a local copy of the rejected content sitting in their personal document store — no cleanup of this was designed or built. Low severity (it's just a local cache, not a security issue, since the reviewer was already authorized to see it), but worth knowing about if a user asks "why do I still have this old rejected draft in my files?"

- **The zoom fix (v9) is unconfirmed** — if "Pokracujem" or whatever comes next doesn't circle back to it, there's a real risk it's silently assumed fixed without the user ever having said so.

- **`dotnet build -t:Run` must NEVER be used on any real phone** — unchanged hard rule from parent, still fully in force; this session's several Android builds all correctly used `dotnet publish -c Release`.

- **The Claude Code Edit-tool classifier outage recurred once this session** (same pattern as parent) — if it recurs again, same handling: retry once, then stop and wait, don't hammer.

- **The approved plan file lives OUTSIDE the repo** at `C:\Users\dvora\.claude\plans\polished-moseying-koala.md` — if that file is ever cleaned up/rotated by the Claude Code harness, the only durable record of the full design rationale is this handoff + the in-code comments (which are extensive, citing dates and reasoning throughout).

- **The full original "AIM Medical Mobile App" spec document is not saved anywhere in the repo** — only the excerpts quoted in this handoff's Evidence & Data section survive. If the user wants to reference the FULL original text again (e.g. to revisit the video/HLS or plugin-architecture sections that were deferred/declined), it may need to be re-pasted by them; it was never written to a file.

## Open Questions

- Does the user want to deploy the Document Library feature in the very next session, or review the design further first? ("Pokracujem" suggests yes, but was never confirmed as a completed instruction.)
- Is v9 (1.28/31) actually fixing the zoom stutter from the user's perspective? Never confirmed.
- How should the Document Library commit(s) be split — one commit per layer (Domain/Relay/Client/Docs) or one large commit? Not decided; this session's other large features (e.g. the 2026-09-30 zoom saga) tended toward one commit per logical round rather than one commit per architectural layer, which might argue for a single commit here too, but this change is larger and more heterogeneous than any single round of that saga.
- Should `IsDocumentReviewer` be granted to any specific device as part of initial rollout (e.g., Petr, or the admin's own S23+), or left for the user to decide per-member once the feature is live?
- Should the "Nahrát" (direct-publish) vs "📝 Jako koncept" (draft) button pair on `LibraryPage` get any in-app help text, since a Modifier now sees two visually similar upload buttons with very different consequences (one is instantly visible to everyone, the other needs review first)? Not raised by the user, but a plausible real-world confusion point worth a cheap tooltip/caption if it comes up.
- Does the rejected-draft local-copy issue (see Risks above) need an actual fix, or is it acceptable as-is? Not yet asked.

## Quick Start for Next Session

```powershell
# Reference docs
Get-Content "H:\Visual Studio\C#\Aplikace\DEVELOPMENT_PLAN.md"   # see the new Document Library bullet under Milestone 5
Get-Content "H:\Visual Studio\C#\Aplikace\IMPROVEMENT_PLAN.md"   # see new Phase 5 + the 2026-10-01 session entry
Get-Content "C:\Users\dvora\.claude\plans\polished-moseying-koala.md"  # the full approved implementation plan (outside the repo)

# Confirm current git state — everything from this session is STILL UNCOMMITTED except the zoom fix
$git = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
& $git status
& $git diff --stat

# Key files to read first if continuing Document Library work
Get-Content "H:\Visual Studio\C#\Aplikace\relay\SecureApp.Relay\Program.cs"   # new /library-documents/* endpoints
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\ViewModels\LibraryReviewQueueViewModel.cs"
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\Views\LibraryPage.xaml"

# Verify current relay + S23+ device state (note: use the CURRENT device id, not the stale one)
ssh secureapp-pi "curl -s http://192.168.50.8:8080/download/android/version"
$adb = "C:\Users\dvora\AppData\Local\Android\Sdk\platform-tools\adb.exe"
& $adb connect 10.8.0.3:5555
& $adb -s 10.8.0.3:5555 shell dumpsys package com.companyname.secureapp.presentation | Select-String versionName

# Next action
# Ask the user to confirm: proceed with committing + deploying the Document Library workflow now?
# If yes: commit (consider splitting by layer), git push pi main, verify relay auto-deploy picked it up,
# THEN run the plan's own relay smoke test (draft/submit/review/approve/reject cycle via curl) BEFORE
# touching any real device, THEN build+deploy the Android client and do a real two-device test.
# Also worth asking: did 1.28(31) actually fix the zoom stutter? Never confirmed this session.
```

## Session Closed
**Closed at:** 2026-10-01 (this session)
**Session status:** Handed off to next session, mid-flow (user said "Pokracujem" and was interrupted by /handoff)
