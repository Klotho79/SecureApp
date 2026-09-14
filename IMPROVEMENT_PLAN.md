# SecureApp — Improvement Plan

A living, systematic plan. Worked in phases, top to bottom. Updated as things land.
Principle: **measure → find the cause in code → fix → verify by the metrics log.** No
one-off guesses; no "why it's hard" prose instead of a fix.

## Goals (user's words, 2026-09-13)
1. **Functionality** — everything works, reliably.
2. **Speed** — fast, fluid response; no jank.
3. **Nice UI (líbivá grafika)** — not started yet; deliberate visual design.
4. **Easy customization** — theming/settings the user can change without code.
5. **Prior requirements** — E2EE chat, shared library, logbook, RBAC, auto key
   distribution, message deletion, history paging, image fit, etc. (already built).

Cross-cutting requirement: **durable on-device Error log + Metrics log** must exist,
because tuning after the build is otherwise just random code changes.

---

## Diagnosis so far (measured, not guessed)
Chat-open jank (S23+, gfxinfo framestats):
- GPU is fine (≤ ~16 ms/frame). Not a rendering/GPU problem.
- The cost is the **measure/layout phase**: ~150–200 ms per full-page layout pass,
  and **3–4 such passes** per open.
- **Independent of message count** (4 vs 8 initial cells → same time) → it is **fixed
  page/CollectionView overhead**, not per-cell work.
- Present on **both** 1:1 and group chats → it is the CollectionView/page layout
  itself, not the group-specific member card.
- Pages are `AddTransient` → a fresh page + fresh CollectionView is built and
  laid out on **every** open; nothing is kept in memory.

Confirmed helpers already shipped: animate:false open (no janky slide), Grid bubbles
instead of nested StackLayouts, deferred member-chip build, single-batch apply, 15-msg
first page. User confirms "znatelně menší latence" but not gone.

Structural notes: `Styles.xaml` = 491 lines / 29 implicit styles / 184 VisualState
entries; 6 custom fonts incl. a variable font (first-render weight).

---

## Phase 0 — Diagnostics infrastructure (DO FIRST)  ✅ mostly done
Durable, on-device, structured logs so tuning is data-driven and self-serve.
- [x] `AppLog`: local rotating files in AppData — `logs/errors.log`, `logs/metrics.log`.
- [x] Error log: global unhandled-exception hook + explicit `AppLog.Error(source, msg, ex)`.
- [x] Metrics log: `AppLog.Metric(name, valueMs, tags…)` — chat/group open now record
      `chat.open.load` / `group.open.load` (bg/cells/members) + page ctor timing.
- [x] Keep the existing relay reporter for opt-in centralized reporting.
- [ ] In-app viewer + export (Settings → Diagnostika) — pull via `adb` for now:
      `run-as com.companyname.secureapp.presentation cat files/logs/metrics.log`.

## Phase 1 — Speed: kill the chat-open cost
Systematic isolation on the S23+ (each ruled out by measurement, not guessed):
- **Message count** — ruled out (4 vs 8 initial cells = same open cost).
- **Page reuse / keep-in-memory** — IMPLEMENTED and reuse engaged (page.reuse.hit,
  no ctor, no rebuild), but the open was still slow → Android re-lays-out the page's
  views on every show regardless of caching. So caching the page does NOT avoid the
  cost. (Kept: it still saves the ctor + rebuild, a small win.)
- **CollectionView** — ruled out: hiding the message list left the open cost.
- **Variable font** — CONFIRMED a major cost. Every Label/Button/Entry used the
  variable `IBMPlexSans-Variable.ttf` ("PlexSans"); variable-font text rendering is
  heavy on Android. Aliasing "PlexSans" to the static OpenSans made the open
  **"rozhodně lepší"** (user, 2026-09-13). ✅ shipped.

Note: per-phase framestats parsing (25-col format) proved unreliable here (garbage
values) — trust the summary's janky-% / GPU numbers and the user's feel, not my
per-phase attribution.

**RESOLVED (2026-09-13):** the real fix was the user's own insight — the chat LIST
is fast because it's a persistent tab (built once, shown/hidden), while the THREAD
was pushed as a transient page and rebuilt every open. Now both 1:1 and group are
hosted persistently in a phone overlay in ChatListPage (reused view per conversation,
shown/hidden, not pushed). Measured: group reopen dropped ~80-120ms → ~23ms, and
open/close are uniform with no push animation. Cold open floor is now just the data
(~65-85ms, decrypt-bound). User confirmed "dobry".

Remaining polish:
- [x] Unify chat view models — shared ChatThreadViewModelBase<TItem> holds the
      duplicated thread scaffolding; 1:1 and group derive from it (−169/+128 lines).
      Message/crypto/member logic deliberately kept per-subclass. Verified: both open
      clean, no errors. (2026-09-13)
- [x] Persistent phone overlay for BOTH 1:1 and group (show/hide, no rebuild); photo/
      doc open made uniform (animate:false) + decrypt-once + JPEG/Skia warmup.
- [ ] Optional: static IBM Plex weight files to restore the intended look (keep static).
- [ ] Optional: unify the two thread VIEWS (XAML) too — bubbles/compose are near-identical.
- [ ] Release build (single ABI + trimming) — APK ~112MB Debug → ~30-45MB Release.
- [ ] Remove the diagnostic per-step bg/doc metrics once tuning settles.

## Phase 2 — Robustness & chat lifecycle (user's list, 2026-09-14)
Triggered by a real incident: a wiped/reinstalled device left a **ghost identity**
(`6d7b56f3` "Local User") that was the GROUP FOUNDER; messages to it black-holed in
the relay outbox (120 stuck), both directions silently failed, and the ghost 1:1
kept auto-reappearing because the group still listed it and the app kept recreating
the session. Manual recovery: purge relay + delete group everywhere + recreate. These
items make the app handle this itself. Do ONE at a time, each deployed + tested.

- [ ] **2.1 User/registration management + no dead souls.** Detect & drop stale device
      registrations; don't leave dead members hanging in chats/groups. Founder-transfer
      (or "remove an unreachable founder") so a group is never hostage to a dead identity.
      Surface a member whose device is gone as "nedostupný / přepárovat", not silently.
- [ ] **2.2 Deleted chat stays deleted.** When a user removes a chat it must NOT silently
      auto-reappear (today the sweeps / group-resync / unknown-sender auto-pair recreate
      it). It returns ONLY when the chat's creator re-invites AND the user consents
      (accept/decline). Needs a per-peer "removed/declined" suppression + a consent prompt.
- [ ] **2.3 Archive a chat that lost all its users.** When a chat/group loses every other
      participant, move it to an Archive instead of deleting/hanging. Read access to the
      archive: **Admin, Modifier, and a participant of that chat.** (New store + RBAC + UI.)
- [ ] **2.4 Attach files not in the shared library.** Let chat participants attach files
      that aren't already in the community library (today attach = library only). Decide:
      auto-import into library vs. a per-chat attachment store; keep E2EE.
- [ ] **2.5 Delivery feedback + reasoned logging.** Show whether participants RECEIVED a
      message (sent/delivered/failed), and log the reason on failure + any automatic
      remedy taken (reconnect, resync). This is exactly what would have made the ghost
      black-hole visible instead of silent. Needs relay delivery-ack + per-message status
      + AppLog entries.

Proposed order: 2.5 (visibility — catches these bugs) → 2.2 (stop silent reappear) →
2.1 (registration/ghost + founder transfer) → 2.3 (archive) → 2.4 (attachments).
Rationale: get diagnosability first, then stop the recurrence, then the deeper
lifecycle/RBAC features. Confirm order or override.

### Known gaps to fix in Phase 2
- **Leave-group from the phone overlay**: `GroupChatViewModel.LeaveGroupAsync` ends with
  `GoToAsync("..")`, which pops the Shell stack — but on phone the group now lives in
  ChatListPage's overlay (not a pushed page), so it won't close the overlay. Handle the
  overlay-hosted case (hide overlay) when leaving. (Introduced by the persistent-overlay refactor.)
- **Logs everywhere** (user ask, 2026-09-14): 2.5A covered send/receive/reconnect; this pass
  added resync, pairing accept/fail, group membership sync + per-member pairing, stale-sweep,
  and delete/leave/remove. Keep extending as new paths are added.

## Phase 2b — Functionality audit
- [ ] Systematic pass over each feature (chat, group, library, logbook, contacts,
      settings, pairing/resync, key distribution) for correctness + edge cases.
- [ ] Error log review to surface silent failures.

## Phase 3 — Nice UI (deliberate visual design)
- [ ] Design system review (spacing, type scale, color, dark/light), consistent
      components, empty/loading/error states.

## Phase 4 — Easy customization
- [ ] User-facing theming/settings (accent, density, font size, tab visibility)
      without code changes; persisted per device.

---

## Log of changes
- 2026-09-13: Plan created; Phase 0 (durable AppLog error + metrics) landed; chat/group
  open self-record timings. Single-batch apply on open. Page-reuse added (small win only).
- 2026-09-13: Phase 1 breakthrough — isolated the open cost to the **variable font** and
  switched "PlexSans" to a static face; user confirms "rozhodně lepší". Ruled out message
  count, page caching, and the CollectionView along the way, each by measurement.
- 2026-09-14: Ghost-identity incident (see Phase 2). Diagnosed via relay outbox (120 dead
  msgs → dead founder `6d7b56f3`), recovered manually (relay purge + recreate group). Logged
  the 5-item robustness backlog (Phase 2). Starting 2.5 (delivery feedback + logging).
