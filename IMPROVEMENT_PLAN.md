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

## Phase 1 — Speed: kill the chat-open layout cost
Root cause = fresh CollectionView built + laid out every open, ×3–4 passes.
- [ ] Reduce passes to 1: avoid redundant relayout triggers on open (ScrollTo,
      RefreshNames rebuild when names unchanged, IsLoading toggles).
- [ ] Keep pages/CollectionView in memory (page reuse / cache) so revisits do **zero**
      rebuild — the user's repeated ask; needs on-device verify (handler lifecycle).
- [ ] Verify each step by the metrics log (target: 1 pass, then ~0 on revisit).

## Phase 2 — Functionality audit
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
  open self-record timings. Single-batch apply on open (removed sync-populate). Next:
  Phase 1 — verify open metrics on-device, then cut layout passes / keep pages in memory.
