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

- [x] **2.1 User/registration management + no dead souls** ✅ (2026-09-17). Three parts, all
      confirmed in scope with the user before building:
      - **Founder succession.** `GroupChat.TransferFounder` (new) + two commands on
        `GroupChatViewModel`: explicit "👑 Předat vedení" per non-founder member row (founder OR any
        Admin/Modifier device, same `CanManageMembers` bar `RemoveAsync` already used) and "👑
        Zakladatel je nedostupný — převzít vedení" (self-claim, gated on `CanClaimFounder` — the
        CURRENT founder absent from the relay's active directory, not just offline right now; also
        requires `CanManageMembers` and `!IsArchived`, so an abandoned/archived group has nothing to
        claim). Both funnel through one `TransferFounderToAsync`: persist the new founder locally,
        then re-broadcast the SAME full membership snapshot every other membership change already
        uses (`GroupInviteBlob.FounderPublicKey` was already carried on every broadcast — no new wire
        message needed). **Real receiving-side bug found and fixed in the same pass**:
        `App.OnGroupInviteReceived`'s existing-group branch only ever checked the NAME for a change
        (`Rename`), never the founder — an incoming snapshot after a transfer/claim would have
        silently left every OTHER device's local copy pointing at the OLD founder forever, even
        though the broadcaster's own copy updated correctly. Fixed by adding the identical
        change-detection + `TransferFounder` call the rename branch already had. The old founder
        becomes an ordinary member the instant `IsFounder` recomputes false, so `CanRemove` (which
        already excluded the founder) applies to them with zero further change — transfer-then-remove
        is the only path, never a group left with no founder at all.
      - **Dead-registration detection, client side, on BOTH 1:1 and group.** New
        `DirectoryNameResolver.IsActive` (reuses the exact directory fetch every screen already does
        for name resolution — no second network round trip) — a peer absent from a freshly-fetched
        ACTIVE directory is stale (the relay's own `DirectoryActiveWindow`, 2 days, already excludes
        anyone who hasn't reconnected-and-republished that recently). `ChatSessionItem.IsUnavailable`
        (`ChatListViewModel`) and `GroupMemberItem.IsUnavailable` (`GroupChatViewModel`) both guard on
        `directoryNames.Count > 0` first — an EMPTY directory means "nothing fetched yet", never
        "everyone is dead". Surfaced as a small "⚠ nedostupný — zkuste přepárovat" label next to each
        1:1 row (paired with the row's existing ↺ resync button) and folded into the group member
        chip's existing `DisplayNameWithRoleSuffix` (e.g. "Jméno (zakladatel, nedostupný)").
      - **Relay-side admin device management** — addresses the actual root cause of the
        ghost-identity incident that started this whole phase (120 `outbox` frames permanently stuck
        for a device that would never come back, only discoverable by SSHing into the Pi and querying
        SQLite by hand). New `RelayDatabase.GetAllDevicesWithStatus()` (LEFT JOINs `devices` against
        `directory_entries` for staleness + a correlated `COUNT(*)` against `outbox` for pending
        depth) and `DeregisterDevice(id)` (deletes the `devices`/`directory_entries` rows and purges
        every `outbox` row still queued for it — idempotent, a double-tap or stale list is a no-op,
        not an error). New admin-gated endpoints `GET /admin/devices` / `POST
        /admin/devices/{id}/deregister` (same `X-Admin-Secret` convention as every other `/admin/*`
        route). Client: `IRelayAdminService.GetRegisteredDevicesAsync`/`DeregisterDeviceAsync` (same
        "secret is a plain per-call parameter, never persisted" discipline the 2026-09-07 security fix
        established for every other admin method), new "Admin: Zařízení" card on `SettingsPage`
        mirroring the existing "Admin: Čekající aktivace" card's shape — staleness text + pending-
        message count per device, confirm-guarded "🗑 Odregistrovat" (the confirmation dialog lives
        directly in the RelayCommand, same precedent `GroupChatViewModel.DeleteMessageAsync` already
        set, since this one's genuinely irreversible).
      - **Verified**: whole solution (`SecureApp.slnx` — Domain/Data/Relay + all 4 Presentation
        targets including Android) builds 0-error. **Not yet live-verified**: the founder-transfer/
        claim flow (needs 2+ devices, one genuinely stale), the unavailable badges rendering correctly
        on-device, and the new admin device-management card (needs the admin secret typed in against
        the real Pi relay) — none of this was click-tested or deployed this pass.
- [x] **2.2 Deleted chat stays deleted** ✅ (2026-09-14). Deleting a chat records the peer in
      RemovedPeersStore (Preferences); the stale-session sweep skips removed peers, and a fresh
      pairing invite from a removed peer is NOT auto-accepted — it's held in PendingInvitesStore
      and surfaced as a consent banner ("X vás chce znovu přidat — Přijmout/Odmítnout") at the top
      of the chat list. Accepting clears the removal + completes the handshake; declining keeps it
      removed. Starting a new chat / manually accepting an invite also clears the removal. (A
      removed peer re-added to a GROUP is left as a legitimate re-introduction — not gated.) Needs
      2 devices to test the accept/decline flow.
- [x] **2.3 Archive a chat that lost all its users** ✅ (2026-09-14). LOCAL archive
      (ArchivedChatsStore, Preferences) — a group auto-archives when it loses every other
      member; archived chats/groups move out of the main list into a collapsible "📦 Archiv (N)"
      section and open READ-ONLY (compose hidden, "Archivováno" footer). The archive is local
      (each device has its own = it was a participant; no server archive, which would break E2EE).
      Plus the user's ask: **move a file from the local archive to the GLOBAL library** — an
      archived attachment shows a →📚 button that promotes the private file to the community
      library (relay is_listed 0→1 via /library/files/{id}/publish, uploader/admin). Relay
      migration + both targets build clean. (1:1 archive UI/publish button is group-only for now.)
- [x] **2.4 Attach files not in the shared library** ✅ (2026-09-14). Chat "Nahrát nový
      soubor" now uploads the file PRIVATE — same encrypted storage + community-key encryption
      as a library file (user: "fungovalo by to stejně jako vkládání do knihovny"), but hidden
      from the community library browser (relay is_listed=0) and reachable only by the id in the
      E2EE message. Recipients open it via id as before. Relay migration verified live.
- [x] **2.5 Delivery feedback + reasoned logging.** Part A (logging) ✅. Part B: delivery
      receipts for BOTH 1:1 and group ✅ (user: "obecně pro chaty, jak 1:1 tak skupinový,
      odesláno i doručeno"). Recipient app sends an encrypted `delivery-ack:v1:<corr>`
      back; the bubble shows ✓ (sent) / ✓✓ (delivered), WhatsApp-style (delivery, not
      read). Group is per-leg: each member's ack upgrades only THAT member's fan-out leg
      (MarkDeliveredForSessionAsync), and ✓✓ shows only once EVERY member's leg is
      Delivered (GetOutboundAggregateStatusAsync = MIN status across legs). Group send now
      marks legs Sent (it didn't before). Needs 2+ devices to test end-to-end. Show whether participants RECEIVED a
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
- **Live-tested 2026-09-15 (PC ↔ S9+, real relay, via UI Automation/adb — see below): history
  migration confirmed working; resend-of-undelivered has a real scope gap, confirmed live too.**
  (a) CONFIRMED: sent a marker message in an empty 1:1 PC→s9+ thread, reset the pairing, reopened —
  the message was still there after the session was replaced (list showed "Čeká na připojení…",
  proving a genuinely new session, not a no-op). (b) NOT reproduced: a stuck-Pending ("🕓") group
  message from earlier the same day did NOT get resent after resetting that member's pairing.
  **Root cause, found and fixed same day**: `ReassignSessionAsync` only migrated from the ONE
  immediately-preceding session to the new one (single hop) — a message stranded on an EARLIER
  session, already superseded by a prior resync before this fix was even deployed, was never reached.
  User's own question ("neni lepsi/bezpecnejsi aby byl chat na serveru?") prompted confirming the
  right fix stays fully client-side (moving history server-side would mean the relay either sees
  plaintext or must retain key material — both break the E2EE/forward-secrecy model the whole app is
  built on; the relay's existing store-and-forward OUTBOX, which only ever holds already-ratchet-
  encrypted envelopes transiently until delivery, is unaffected and stays as-is). **Fixed**: new
  `IChatSessionRepository.GetAllByPeerPublicKeyAsync` (every session ever had with a peer, any state)
  + `SessionRecoveryHelper.ReassignAllHistoryAsync` (walks ALL of a peer's prior sessions — not just
  the latest — and re-parents each one's messages onto the fresh session). Called from `ResyncAsync`
  (initiator side) and both accept-a-fresh-invite sites (`App.OnPairingInviteReceived`,
  `ChatListViewModel.AcceptInviteAsync`), replacing the old single-hop calls. Verified with the
  scratchpad DB smoke test extended to 11/11 checks (added: messages stranded 2 AND 1 generations
  back both correctly land on a brand-new 3rd-generation session). Whole solution builds 0-error.
  **Still not live-verified**: whether this actually resolves the specific stuck 🕓 group message
  found during the live 2-device test (it predates even the single-hop fix, so it's a good real-world
  case for this) — worth checking next time either phone is available.

**2026-09-15 — quiet auto-heal + a real bug it led to (user: "reconnect zvládne, ale ta hláška už je
navíc... zpráva která nebyla přeposlána musí být vyhledána a vložena do chatu — na chyby je log"):**
- **Banner removed on successful auto-heal** — `ChatViewModel`/`GroupChatViewModel.TryAutoHealAsync`
  no longer show a red "connection recovered" banner when the background resync succeeds (only
  `AppLog.Event`, consistent with this app's existing "no user action" recovery policy); the FAILURE
  branch still shows a banner since that's a real actionable problem.
- **Real bug found tracing the report through: a resync always forked chat history onto a brand-new
  session id, and the thread view only ever queries the CURRENT session** — so every prior message
  became invisible (not deleted, just unreachable) the moment either side's `ChatListViewModel` picked
  up the fresh session; `ChatListViewModel`'s own 2026-09-13 comment ("nothing is deleted, so no
  history is lost") only covered the LIST row, not the thread content. Fixed with a new
  `IMessageRepository.ReassignSessionAsync(oldId, newId)` (verified with a real-DB scratchpad smoke
  test — 7/7 checks) that re-parents every message row onto the fresh session id, called both from
  `SessionRecoveryHelper.ResyncAsync` (the initiating side) and from every accept-a-fresh-invite site
  (`App.OnPairingInviteReceived`, `ChatListViewModel.AcceptInviteAsync`).
- **Also closes the literal "one message got lost" report**: a genuine ratchet decrypt failure really
  is unrecoverable for that one ciphertext (Double Ratchet forward secrecy), but its CONTENT isn't —
  the sender still holds its own outbound copy. New `SessionRecoveryHelper.ResendUndeliveredAsync`
  decrypts every not-yet-delivered outbound message on the just-migrated session (from local vault-
  encrypted storage, not the one-shot ratchet ciphertext) and re-sends it fresh. Deliberately only
  called from the ACCEPTING side of a re-pair (session fully established both directions by then) —
  the initiating side only migrates history, it does not resend, to avoid a message racing ahead of
  the peer's own invite-processing over the wire.
- **A separate real bug caught tracing this**: `ChatViewModel.TryAutoHealAsync` (unlike its sibling
  `ResyncAndRetrySendAsync`) never repointed `_chatSessionId` to the fresh session after a successful
  resync — the open thread kept listening on the now-Closed old session id and silently stopped
  receiving anything live until the page was closed and reopened. Fixed to match the existing
  `ResyncAndRetrySendAsync` precedent. (`GroupChatViewModel` was never affected — it filters incoming
  envelopes by the stable `GroupChatId`, not a single session id.)
- Whole solution builds 0-error on all 4 Presentation targets + Domain/Data/Relay.

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

**2026-09-15/16 — real live bug: chat compose-box keyboard pans the whole window up (S23+, S9+).**
Root cause: Android mandates edge-to-edge rendering once targetSdkVersion >= 35 (this app resolves to
36); the manifest-declared `WindowSoftInputMode="AdjustResize"` compiled correctly but had zero
runtime effect (`adb dumpsys window` showed the live window still `sim={adjust=pan}` — something,
most likely MAUI's own Android bootstrap, resets it after `Activity.OnCreate`). Pinning
`AndroidTargetSdkVersion` down turned out to be a dead end in this SDK release (the property isn't
read anywhere in `Microsoft.Android.Sdk.Windows`'s own `.targets`, confirmed by reading them
directly). **Fixed** by forcing `Window.SetSoftInputMode(SoftInput.AdjustResize)` + `WindowCompat.SetDecorFitsSystemWindows(Window, true)`
in code, re-asserted on every `OnResume` (not just `OnCreate`) so it always wins. Verified empirically
on S23+ via `dumpsys window` (now shows `sim={adjust=resize}`) and measured UI bounds before/after
opening the keyboard (header stays put, compose bar moves up exactly by the keyboard height). Deployed
to both S9+ and S23+. Full diagnostic trail in [[android-keyboard-edge-to-edge-fix]] (session memory).
Along the way: discovered and documented [[adb-wireless-technique]] (adb over WiFi/WireGuard tunnel,
no cable needed) and [[android-fast-deploy-gotcha]] (never `adb install` a debug APK directly — crashes
instantly, "No assemblies found... Fast Deployment" — always deploy via `dotnet build -t:Run`).

**2026-09-16 — merged the Knihovna and Dokumenty bottom tabs into one ("🗂 Soubory") — user's own ask:**
tab bar was crowding (Nastavení was already pushed into Android's "More" overflow on phone). Pure
navigation merge, no data/storage change — AppShell.xaml now nests both ShellContent under one Tab,
which Shell renders as a top sub-tab strip natively; DocumentBrowserPage/LibraryPage and their
ViewModels are untouched. `AppShell.xaml.cs`'s `LogbookTabInsertIndex` adjusted 4→3 to match the new
tab count. Deployed and confirmed on S9+ and S23+.

## Log of changes
- 2026-09-13: Plan created; Phase 0 (durable AppLog error + metrics) landed; chat/group
  open self-record timings. Single-batch apply on open. Page-reuse added (small win only).
- 2026-09-13: Phase 1 breakthrough — isolated the open cost to the **variable font** and
  switched "PlexSans" to a static face; user confirms "rozhodně lepší". Ruled out message
  count, page caching, and the CollectionView along the way, each by measurement.
- 2026-09-14: Ghost-identity incident (see Phase 2). Diagnosed via relay outbox (120 dead
  msgs → dead founder `6d7b56f3`), recovered manually (relay purge + recreate group). Logged
  the 5-item robustness backlog (Phase 2). Starting 2.5 (delivery feedback + logging).
- 2026-09-16: Compacted the group chat header — dropped the redundant Shell title bar
  (`Shell.NavBarIsVisible="False"` on `ChatListPage`, matching `ContactsPage`/`LogbookPage`),
  then (follow-up same day) put Members/Add/Leave on the SAME row as the phone overlay's own
  "‹ Zpět" button; found and fixed a real bug along the way — `GroupChatViewModel.LeaveGroupAsync`'s
  `Shell.Current.GoToAsync("..")` was a silent no-op on the overlay host, fixed via a new
  `LeftGroup` event each host handles its own way.
- 2026-09-16: Dropped the manual 1:1 pairing fallback (contact-card paste/QR/invite cards on
  `NewChatPage`) — user's ask: "1:1 by měl být jako skupinový", mirroring how adding a group
  member is already directory-only. The one-tap community directory list is now the only path.
- 2026-09-16: Settings gained per-tab show/hide for Chats/Files/Contacts (Nastavení itself
  never hideable) — generalized the existing Logbook-only toggle mechanism
  (`AppShell.ApplyTabVisibility`/`RebuildTabBar`) to all four hideable tabs.
- 2026-09-16: Fixed a real bug — "Local User" chats kept reappearing after deletion. Two
  automatic resync paths (`App.OnGroupInviteReceived`, `GroupChatViewModel.ResyncMissingMembersAsync`)
  were unconditionally re-initiating pairing with any unpaired group member, ignoring
  `RemovedPeersStore` (2.2's own removal mechanism) — as long as a removed/stale test member
  stayed in a shared group's roster, every membership sync or group open silently recreated
  the 1:1 chat. Both now skip a `RemovedPeersStore`-listed peer, matching `OnPairingInviteReceived`
  and the stale-session sweep, which already did.
- 2026-09-16: Fixed slow Contacts loading — collapsed sections LOOKED collapsed but
  `BindableLayout` doesn't virtualize, so all ~130 phone-book rows were built immediately on
  page open regardless of `IsVisible`. `ContactSectionGroup.VisibleEntries` now returns empty
  until a section is actually expanded, deferring the real cost to the tap.
- 2026-09-17: **Phase 2.1 done** — founder transfer/claim, "nedostupný" badges on both 1:1 and
  group member rows, and relay-side admin device management (list + deregister, purging a dead
  device's stuck outbox). See Phase 2's own entry above for full detail.
