# SecureApp: shipped admin-only local-AI PDF translation end-to-end, ran 4 rounds of visual-design exploration (still unresolved), found+fixed a real WireGuard onboarding bug live, released 1.51 (54)

**Date:** 2026-10-06
**Status:** IN PROGRESS
**Bead(s):** none
**Epic:** none
**Chain:** `standalone-be860f68` seq `7`
**Parent:** `plans/handoffs/HANDOFF_standalone-be860f68_widget-wireguard-multiprofile_2026-10-05.md` (seq 6)
**Prior chain:** `HANDOFF_identity-recovery-doc-zoom_2026-09-30.md` > `HANDOFF_zoom-fix-library-workflow_2026-10-01.md` > `HANDOFF_library-redesign-subcategories_2026-10-02.md` > `HANDOFF_standalone-be860f68_library-video-gcs-tools_2026-10-03.md` (seq 4) > `HANDOFF_standalone-be860f68_murray-das-chat-pairing_2026-10-04.md` (seq 5) > `HANDOFF_standalone-be860f68_widget-wireguard-multiprofile_2026-10-05.md` (seq 6) > this (seq 7)

---

## Since Last Handoff

Parent's (seq 6) own "Where We're Going" had 8 items — ALL eight remain untouched this session, for the second session running:
1. Pairing-loop root cause (PC ↔ Vilém's S23+, 20-min recurring resync) — **not touched**. This session's onboarding DID re-read `App.xaml.cs`'s `RunStaleSessionSweepAsync`/`MessagingService.CreateSessionAsync` per the parent's own Quick Start instructions, but the user pivoted to a completely different ask (`"A musi byt orc?"`) before any further investigation happened.
2. Live-verify unverified Android UI fixes (Settings tab speed, font-scale wrapping) — **not touched**.
3. Petr Faltus's still-silent phone — **not checked** (status unknown, last known silent since 2026-10-03T13:20:02Z per seq 6).
4. `WebSocketException` investigation (1957×, dominant in `KNOWN_ISSUES.md`) — **not touched**, now 4 sessions running.
5. `SearchBar` `ObjectDisposedException` — **not touched**.
6. Documentation debt (`DEVELOPMENT_PLAN.md`/`NOTIFICATION_HUB_SPEC.md`) — **got measurably worse again**: a second entire new subsystem (local AI translation) shipped this session with zero entry in either file. Only `IMPROVEMENT_PLAN.md` got updated (twice, see below) — that file tracks UX/perf work, not architecture, so the gap in the other two docs is now Windows-multi-profile-login-sized AND translation-feature-sized.
7. Windows multi-profile login distribution to the real shared PC — **not touched**.

**Net trajectory: the session abandoned the parent's entire carry-over list again**, on the user's own fresh, unrelated requests — same pattern as seq 5→6. Three genuinely new things happened instead: (a) a real, shipped, built-and-released feature (local AI PDF translation), (b) an unresolved, still-iterating visual-design exploration (4 rounds, no direction chosen yet), and (c) a real live bug found and fixed in the WireGuard onboarding flow, released as 1.51(54). The carry-over backlog is now large enough (7 items, 2+ sessions deep each) that it's worth a direct decision with the user: dedicate a session to it, or explicitly accept it as permanently low-priority background debt.

## Reference Documents

- `DEVELOPMENT_PLAN.md` / `NOTIFICATION_HUB_SPEC.md` — still not updated; see "Since Last Handoff" above.
- `IMPROVEMENT_PLAN.md` — updated twice this session (see Key Decisions + Evidence). Now has Phase 3 (Nice UI) fleshed out and Phases 6+7 (local AI translation, document reformatting) marked done.
- `KNOWN_ISSUES.md` — not touched, not re-queried.
- Plan file for the translation feature (approved, full design): `C:\Users\dvora\.claude\plans\nested-crafting-bee.md`.
- Design exploration Artifact (private, canvas, 9 published versions so far): `https://claude.ai/artifact/YSzBS3h95qordofUsimg6t`.
- Prior seq 6 handoff for the still-open pairing-loop/WebSocketException/multi-profile-distribution items — not duplicated here.

## The Goal

SecureApp is a .NET MAUI (Android/iOS/Windows) Clean-Architecture app for a hospital anesthesiology/ICU team: E2EE chat, a shared encrypted document/procedure library, a duty logbook, a company phone directory, a personal work-schedule module, and a notification hub with an Android widget. This session, on three separate fresh user requests, (1) fixed a Windows-download-page gap, (2) designed and shipped an entirely new admin-only feature letting a local LLM (Ollama/LM Studio on the admin's own PC) translate Library PDFs into the existing content-review workflow with zero relay changes, (3) ran four full rounds of visual-redesign exploration on a Claude Artifact canvas without yet landing on a direction, and (4) diagnosed and fixed a real live WireGuard-onboarding bug purely through direct evidence (live wg-easy API testing + code reading), shipping the fix as release 1.51 (54).

## Where We Are

- **Windows download-page fix: shipped (commit `0469293`).** `DownloadPageHtml` in `relay/SecureApp.Relay/Program.cs` only ever took an Android-availability flag.
- A Windows portable build already sat on the relay, fully served at `/download/windows` — the PAGE itself just never linked to it. That was the whole bug.
- Added a parallel `windowsAvailable`/`windowsSize` check + a conditional "Stáhnout pro Windows" section with its own 2-step install note.
- Deployed via the existing self-redeploy pipeline (`git push` to `pi`/`github` + `POST /admin/deploy`), confirmed live by re-fetching `/download` and finding the new link text.
- **Three new `IMPROVEMENT_PLAN.md` backlog items added (commit `2706517`)**, user's own ask: "do developer planu 3 velke veci" (new visual design, local AI doc translation, doc reformatting).
- Phase 3 (pre-existing, empty) fleshed out with a concrete "AI-mockup-first" plan.
- New Phase 6 (local AI translation) and Phase 7 (document reformatting) added as backlog, not yet built at that point.
- Both Phases 6+7 marked `[x]` v1-done later the SAME session (commit `3fca2f1`), once the real feature actually shipped — see Key Decisions for the exact scope resolution that let this happen in one session.
- **Admin-only local-AI PDF translation: fully built, committed (`f4764c2`), builds clean on Windows+Android, NOT yet live-verified against a real Ollama/LM Studio instance.**
- Trigger: a new 🌐 action on any PDF Library item, wired into BOTH `LibraryPage.xaml` (search-results list) AND `LibrarySubcategoryDetailPage.xaml` (the actual primary "browse a category" screen post-2026-10-02-redesign).
- Pipeline: admin types a target language → `LocalAiLibraryTranslationService.TranslateAndSubmitAsync` downloads+decrypts the original PDF → reads its real embedded text layer page-by-page via **PdfPig** (no OCR — confirmed mid-session these are electronic/born-digital documents, not scans).
- Each page's extracted text goes to a configurable local endpoint via plain OpenAI-compatible `/v1/chat/completions` — works unchanged against Ollama or LM Studio, no vision model needed.
- Output is a NEW PDF built via **QuestPDF**, interleaving each original page's rendered image with its own translated-text page.
- Result is uploaded and wrapped in a brand-new `LibraryDocument` Draft (never a new *version* of the original — see Key Decisions), then immediately submitted for review.
- Lands in the **unmodified** `LibraryReviewQueuePage`/review workflow from 2026-10-01 — zero relay changes, zero new review UI needed.
- **New Settings card "Admin: Místní AI překlad"** (`SettingsViewModel.LocalAiTranslation.cs`, `SettingsPage.xaml`) — base URL (default `http://localhost:11434`) + model name fields, "Uložit"/"Testovat připojení" buttons.
- Gated `IsAdmin && DeviceInfo.Current.Platform == DevicePlatform.WinUI` — Windows-only, since the local AI only ever runs on the admin's own PC.
- **Visual design exploration: 5 rounds total on one Artifact canvas, still unresolved.** Round 1 (A/B/C, card-based) — found "too similar, just colors."
- Round 2 (+D, field-manual/TOC metaphor) — **D liked**.
- Round 3 (D kept, E/F/G replace A/B/C: monitor, card-catalog, clipboard-form) — all discarded without specific feedback given.
- Round 4 (D kept, H/I/J replace E/F/G: tile dashboard, wayfinding signage, engineering blueprint) — discarded because the user wanted the REAL app layout styled differently, not new invented layouts.
- Round 5 (current, K/L/M replace D/H/I/J): all three reproduce the EXACT real `LibraryPage.xaml` structure (SearchBar → emoji category tiles → "Akutní stavy" pill row → 2-column subcategory grid → tab bar), varying only color/type/material (K=warm teal/serif, L=dark/amber/mono, M=cool navy/mono).
- **Awaiting user feedback on K/L/M — this is the single biggest open thread of the whole session.**
- **Real WireGuard onboarding bug found, root-caused, fixed, and released as 1.51(54) (commits `1da0e3f`, `8492c6c`)** — see What We Tried + Code Analysis for the full mechanism.
- Not yet confirmed fixed by the user on a real device — they were mid-troubleshooting when the session ended (see Open Questions).
- **Version progression this session:** 1.50(53) at start → 1.51(54), one release, for the WireGuard fix specifically — not bundled with the (much bigger) translation feature or the design work, consistent with the user's own "releases come as their own standalone ask" pattern confirmed yet again.
- **Relay deployed once this session** (Windows-download-page fix only) — the translation feature and the WireGuard fix are both pure client-side changes, no relay deploy needed for either.

## What We Tried (Chronological)

1. **Onboarding (parent seq 6 → this session)** — read the parent handoff per its own Quick Start instructions: verified git HEAD, confirmed relay serving 1.50(53), re-read `App.xaml.cs`'s `RunStaleSessionSweepAsync`/`OnPairingInviteReceived` and `MessagingService.cs`'s `CreateSessionAsync`/`AcceptSessionAsync` for the carried-over pairing-loop investigation, plus explored `SessionRecoveryHelper.cs`, `ChatSession.cs`, `ChatListViewModel.cs`, `WebSocketMessageTransport.cs` as adjacent context. Traced a real structural finding (the initiator's own session is never `.Activate()`'d by any code path — only the responder does) but did not reach a root cause before the user redirected. **Abandoned mid-investigation, never resumed.**
2. **User: "A musi byt orc? Budou to elektronicke dokumenty hlavne pdf?"** — mid-turn correction while a design-mockup-method background agent was still being set up (this was actually asked INSIDE the previous conversation segment, about whether OCR is needed for translation). Answered directly: no, since these are electronic/born-digital PDFs, real text-layer extraction (PdfPig) beats OCR — more accurate, faster, and opens up plain (non-vision) translation models. This correction reshaped the whole translation-feature design before it was built.
3. **User: "Ok do developer planu 3 velke veci a to novy design ocenil bych navrhy... dale vyuziti lokalni ai k prekladu... a potom uprava dokumentu aby byla pekna v apce... pujde to?"**
   - Read `DEVELOPMENT_PLAN.md` (226 lines) and `IMPROVEMENT_PLAN.md` (full) before answering.
   - Feasibility read on design: yes, same Artifact-mockup precedent as the 2026-09-06 redesign already used.
   - Feasibility read on translation: yes, with the on-device-vs-Pi-hosted tradeoff explicitly flagged (not yet resolved at this point).
   - Feasibility read on document reformatting: flagged as the actual GATING dependency for translation, since there's no text-reflow renderer at all today.
   - Added all 3 as new/expanded `IMPROVEMENT_PLAN.md` phases, no code changed yet (commit `2706517`).
4. **User: "Ok muzeme zacit fazi 2 byla by pristupna pouze pro admin... potreboval bych aby dochazelo ke spolupraci s tebou a lokalni ai aby byl preklad co nejdokonalejsi..."**
   - Genuinely ambiguous on two real axes: does "collaboration with you" mean a runtime Claude-API call (breaking the app's "nothing leaves the LAN" principle)? Does "spustí překlad" mean automatic in-app or session-assisted?
   - Used `AskUserQuestion` on the collaboration/data-boundary model first — user picked NONE of the 3 offered options cleanly, instead revealing the real setup (Ollama + LM Studio already running on their own PC, RTX 5060 16GB), which simplified the architecture more than any option offered (no cloud dependency needed at all).
   - Used `AskUserQuestion` a second time on the trigger mechanism — user picked "Appka/relay sama zavolá Ollama/LM Studio na PC (automaticky)" — fully automatic, not session-assisted.
5. **Full Plan Mode cycle for the translation feature**:
   - `EnterPlanMode`, launched 3 `Explore` agents IN PARALLEL (single message, multiple tool calls).
   - Agent 1: Document Library review workflow internals (`ILibraryReviewService`, relay tables, `LibraryReviewPolicy`).
   - Agent 2: rendering/import/OCR-feasibility (`DocumentRenderingService`, `IDocumentImportService`, confirmed zero existing text-extraction anywhere).
   - Agent 3: HTTP-client/RBAC/Windows-gating patterns (`Http*Service` shape, `DeviceInfo` idiom, full `RbacAction` enum).
   - Then 1 `Plan` agent to synthesize a concrete file-by-file design from all 3 reports.
   - Reviewed the Plan agent's output, overrode its vision-model/OCR assumption with the PdfPig text-extraction approach (per item 2 above).
   - Wrote the final plan to `C:\Users\dvora\.claude\plans\nested-crafting-bee.md`, got explicit approval via `ExitPlanMode`.
6. **Implemented the full translation feature** — see Files Changed for the complete list.
   - Hit build error #1: invalid `--` inside an XML `<!-- -->` comment in the `.csproj` (XML comments can't contain `--`) — fixed by rewording.
   - Hit build error #2: `CS1061` on `.Page()` — fully-qualifying `QuestPDF.Fluent.Document.Create` doesn't pull in its own extension methods; fixed with explicit `using QuestPDF.Fluent;`/`QuestPDF.Helpers;`/`QuestPDF.Infrastructure;`.
   - Built clean on both Windows (0 errors) and Android (0 errors, only pre-existing/harmless warning classes) after each fix.
   - Committed + pushed to `pi`+`github` (`f4764c2`).
   - Updated `IMPROVEMENT_PLAN.md` to mark Phases 6+7 done with the real resolved design (commit `3fca2f1`).
7. **User: "Ok zkontroluji zitra ted pokracuj v planu..."** — the one remaining item from the original "3 big things" is the new visual design. Loaded the Artifact `design` canvas type via `quickstart`, published a new canvas, built 3 mockups (A/B/C) of the Knihovna browse screen — card-list-based, each a different palette (warm/teal, dark/amber, cool/navy).
8. **User: "To jsou graficke ramce?"** — clarifying question (are these just static visual frames?). Answered yes, confirmed they're non-interactive comps for comparison only.
9. **User: "No nevidim nejake rozdily mezi jednotlivymi navrhy krom ze je jeden tmavy druhy svetly...."** — real, substantive critique: A/B/C's differences (corner-radius, color-temperature) were too subtle to register at a glance, especially since A and C are both light backgrounds. Revised A (added a colored header band, pill-shaped fully-rounded cards, circular icon badges) and C (rebuilt entirely from a card list into a real compact data TABLE — column headers, thin dividers, right-aligned mono sizes) to be structurally distinct, not just recolored.
10. **(mid-revision) User: "Nebo to je jen jeden navrh?"** — asked while the A/C rewrite was still in flight; clarified that 3 separate artboards genuinely exist side-by-side on the canvas and that the "single artboard" appearance was likely a zoomed-in/focused view, not a real limitation. Finished the A/C revision, republished.
11. **User: "No myslim zkus odlisny navrh..."** — wanted something genuinely different tried, not more tweaks. Used `AskUserQuestion` rather than guessing which axis ("jiná osobnost/styl"? "méně komerční"? "jiná obrazovka"? "konkrétní inspirace"?) — user picked "Jiná osobnost/styl celé appky" (a different personality/style for the whole app, since all 3 were variations on "cards + round icons").
12. **Built D ("Terénní manuál" — field-manual/binder metaphor)** — added as a 4th artboard alongside A/B/C: colored binder-divider tabs instead of a search bar + chip row, a numbered TOC-style index list with dotted leader lines to category code + file size instead of boxed cards, bracketed `[STAT]`/`[PŘEKLAD]` annotations instead of icon badges, flat ink-black footer with outline-only icons.
13. **User: "D lepsi A az C vyhod a zkus dalsi navrhy"** — D confirmed as the liked direction.
    - Removed A/B/C from the canvas — both the `boards`/`order`/`notes` entries in `canvas.json` AND the actual `.dc.html` files via the `files: {"path": null}` removal mechanism.
    - Built E: bedside vital-signs monitor — black screen, phosphor green, `[F1]`-style softkey footer, red-alarm-tinted row for the critical item.
    - Built F: library card-catalog — notched/rotated index cards on kraft paper, Courier Prime typewriter font, ruled-paper texture via `repeating-linear-gradient`, circular rubber-stamp badges.
    - Built G: clinical clipboard form — a drawn paper-clip graphic, checkbox category selector, numbered-circle checklist rows on ruled form lines, rotated rectangular stamp.
14. **User: "Nech d a zkus jine 3 ...nove navrhy"** — kept D, discarded E/F/G (same null-file-removal pattern).
    - Built H: Windows/Fluent-style tile dashboard — asymmetric colored tile grid for categories, a secondary minimal "recent documents" list.
    - Built I: hospital wayfinding/directory-board signage — full-bleed colored bands per category like a floor directory, bold-bordered "room plaque" document entries.
    - Built J: engineering blueprint — navy with a faint CSS-grid background texture, dashed rules, cyan thin-line icons, bill-of-materials-style spec annotations per item, red `[!] KRITICKÉ` flag.
15. **User: "No podivej asi bych nechtel menit rozlozeni spis mi zkus rozlozeni ktere mame ukazat v tech navrzich..."** — the critical correction: every mockup so far (A through J) had INVENTED a new structural layout; the user actually wanted the REAL current app layout, just restyled.
    - Read the real `LibraryPage.xaml` in full to extract its exact structure: `SearchBar` → horizontally-scrolling emoji category tiles (`HeightRequest="100"`) → `"Akutní stavy"` label + horizontally-scrolling red-tinted pill row (`HeightRequest="50"`) → 2-column `GridItemsLayout` of plain centered-text subcategory cards.
    - Extracted the exact real copy too: category names, Akutní-stavy entries, emoji icons (📚⭐❤💉📖🧮).
    - Discarded D/H/I/J entirely.
    - Built K (warm paper/teal, Spectral serif on subcategory titles — carries D's "paper" material forward), L (dark/amber, IBM Plex Mono uppercase tile labels — carries E's "monitor" energy forward), M (cool navy/gray, sharp 4px corners, mono throughout) — same real content, only color/type/material varying.
16. **User: "Kdyz dam vytvorit novy wireguard tak mi to pise wireguard[1] neplatny nazev nemuze se tunel menovat jako nazev clena?"** — live bug report, user's own hypothesis (can't name a tunnel the same as an existing member).
    - Read the real code first: `relay/SecureApp.Relay/Program.cs`'s `/admin/wireguard/clients` handler, `HttpRelayAdminService.CreateWireGuardClientAsync`, `SettingsViewModel.Updates.cs`'s `CreateWireGuardAccessAsync`/`DownloadWireGuardConfigAsync`.
    - Tested empirically against the LIVE wg-easy instance via SSH+curl scripts — reading the admin secret internally inside a Pi-side bash script, never materializing it in my own output (same safe pattern already used for the wg-easy password earlier in the project's history).
    - Listed all 8 real registered clients first (see Evidence & Data).
    - POSTed a test client with a DUPLICATE name ("Petr Faltus") directly against wg-easy's own API — **200 OK, no rejection**.
    - POSTed a test client with Czech diacritics ("Zuzana Dvořák") — **200 OK, no rejection**. Both disproved the user's own hypothesis before guessing further.
    - Re-read the actual `DownloadWireGuardConfigAsync` code and found the real cause (see Code Analysis).
    - Cleaned up all 5 diagnostic test peers afterward, confirmed the remaining list exactly matched the original 8.
17. **Fixed the real bug, built, committed (`1da0e3f`)** — captured the member name into a new `_lastWireGuardMemberName` field before `CreateWireGuardAccessAsync` clears the bound property, had `DownloadWireGuardConfigAsync` read that field instead of the now-empty one. Windows build 0 errors.
18. **User: "Porad to pise ze nelze implantovat konfiguraci: Nazev tunelu je neplatny: wireguard[1]"** — explained this is expected: the fix is source-only, the running app hadn't been rebuilt.
    - Also flagged a separate, code-independent issue: if a tunnel literally named `wireguard` already exists on the target Windows machine from before the fix, any OLD leftover `wireguard.conf` file would keep colliding regardless of the fix.
19. **User: "Musim to predat z tel."** — revealed the actual workflow: the admin creates/downloads the WireGuard config FROM THE PHONE, then transfers the file to the target PC manually.
    - This meant the bug (and fix) needed an ANDROID rebuild, not a Windows one, since `SettingsViewModel.Updates.cs` is shared cross-platform code.
20. **User: "Udelej release"** — standalone release request, matches the project's own well-established pattern (releases always come as their own short message, never bundled into a feature-request turn).
    - Bumped `ApplicationDisplayVersion`/`ApplicationVersion` 1.50/53 → 1.51/54.
    - Ran `dotnet publish -f net10.0-android -c Release`.
    - Verified the signing certificate via `keytool -printcert -jarfile` — confirmed `CN=SecureApp`, the real release key, not a re-signed/different one.
    - Hit a real deployment snag: `localhost:8080` unreachable from the Pi itself (see Code Analysis) — had to switch to the Pi's own LAN IP.
    - Hit a `curl -s` gotcha: it silently swallows connection-refused errors too, not just progress output — switched to `-sS` to actually see the real error.
    - Uploaded successfully via the Pi's own LAN IP, verified via `/download/android/version` returning `{"VersionCode":54,"VersionName":"1.51",...}`.
    - Committed + pushed the version bump (`8492c6c`).

## Key Decisions

- **PdfPig text extraction instead of vision-model OCR.**
  - Why: the user's own mid-session correction — these are electronic/born-digital PDFs, not scans.
  - Benefit: simpler, more accurate, faster, and opens up plain (non-vision) local models for translation quality.
  - Rejected alternative: a vision-capable LLM reading rendered page IMAGES directly (the original Plan agent's own design) — would have worked but is strictly worse once text extraction is available.
  - Deferred, not built: OCR/vision fallback for a genuinely scanned page — handled gracefully as a placeholder note ("[Tato strana nemá rozpoznatelný text — viz obrázek originální strany.]") rather than an error, but no actual OCR path exists yet.
- **A translation becomes a brand-new `LibraryDocument` (`CreateDraftAsync`), never a new version of the original (`AddVersionAsync`).**
  - Why: `ApproveLibraryDocumentVersion` on the relay UNLISTS the previous version's `library_files` row when approving a new one.
  - Rejected alternative: `AddVersionAsync` (versioning the original) — would make the ORIGINAL document disappear the moment the translation gets approved, since the two need to coexist.
  - This was flagged as a deliberate design choice IN THE PLAN itself, not discovered as a bug after building.
- **Admin-only gating via a direct `Role == Role.Admin` check, not a new `RbacAction`.**
  - Why: confirmed via an Explore agent that this codebase's `RbacAction`/`RoleAccessPolicy` matrix structurally cannot express "Admin but not Modifier" — every existing action fuses them as equivalent.
  - Precedent followed: true admin-only features here (e.g. "Admin: Nasazení relay serveru") already bypass `RbacAction` entirely the same way.
  - Rejected alternative: a new orthogonal `DevicePolicy` boolean (mirroring `IsDocumentReviewer`) — would work for a GRANTABLE per-device capability, but the user asked for a fixed admin-only gate, so the simpler direct-role-check won.
- **Output PDF built as real interleaved pages via QuestPDF (original page image, then its translated-text page, repeated), not a bespoke SkiaSharp renderer.**
  - Why: a genuine simplification discovered mid-planning — generating a real PDF means the EXISTING `DocumentRenderingService`/`DocumentViewerPage` rasterize-and-page machinery handles display with ZERO new viewer code.
  - Rejected alternative: the original plan's Phase 7 item (a hand-built SkiaSharp page-layout renderer) — became unnecessary once this was realized, saving the single biggest piece of planned work.
- **All-or-nothing translation failure policy** — a sustained AI-call failure mid-document aborts the WHOLE translation; nothing partial gets uploaded, no Draft created.
  - Rejected alternative: upload a partial draft with failed pages flagged inline — simpler for the user to salvage partial work, but risks a half-garbage draft entering the review queue; deferred as a possible v2 change.
  - Explicitly flagged to the user as a v1 choice in the plan rather than silently decided; not yet revisited after building.
- **Design exploration: kept iterating structurally rather than just swapping palettes once feedback said "can't tell them apart."**
  - Each round of user feedback ("too similar" → "try something with a different personality" → "keep D, discard the rest, try more" → "actually, don't invent new layouts, style the REAL one") was treated as a signal to change APPROACH, not just inputs to vary within the same approach.
  - Rejected approach (implicitly, after round 4): continuing to invent new structural layouts (the H/I/J direction) — abandoned once the user clarified they wanted the REAL layout restyled, not new IA exploration.
  - The round-4 correction was the most consequential — it invalidated rounds 1-3 entirely (A through J) as not actually answering what was asked.
- **WireGuard diagnosis: tested the user's own hypothesis empirically against the live system BEFORE reading code.**
  - Confirmed/disconfirmed via direct API calls (duplicate name, diacritics) rather than reasoning from documentation or assumption.
  - Rejected approach: assuming the user's hypothesis (duplicate member name) was correct and building a workaround for it — would have "fixed" a non-problem and left the real bug live.
  - This ruled out two plausible-sounding theories in under a minute each, instead of going down either rabbit hole.
- **Release bundling: the WireGuard fix got its own standalone release (1.51/54), not bundled with the (much bigger) translation feature.**
  - Why: the release request came as its own explicit, separate message ("Udělej release"), matching the project's established pattern from every prior session.
  - Rejected alternative: holding the release until the translation feature was also ready to ship — rejected because the user's own request was specific and immediate, tied to an active bug they were blocked on.

## Evidence & Data

**Commit log, this session (chronological, `pi`+`github`):**

| Commit | What |
|---|---|
| `0469293` | Relay: show a Windows download link on the `/download` page |
| `2706517` | Plan: add 3 backlog items (new design pass, local AI doc translation, doc reformatting) |
| `f4764c2` | Admin-only local-AI PDF translation for the Document Library |
| `3fca2f1` | Plan: mark Phases 6+7 (local AI translation) done, v1 |
| `1da0e3f` | Fix WireGuard .conf download always being named wireguard.conf |
| `8492c6c` | Release 1.51 (54) |

**Design exploration iteration history (all on one Artifact canvas, `https://claude.ai/artifact/YSzBS3h95qordofUsimg6t`, 9 published versions):**

| Round | Artboards | Shape | Outcome |
|---|---|---|---|
| 1 | A, B, C | Card-list, 3 palettes (warm/teal, dark/amber, cool/navy) | "Can't tell them apart except dark vs light" |
| 1.5 | A, C revised | A: header band + pill cards + circular badges. C: rebuilt into a real data table | Still too similar in spirit |
| 2 | +D | Field-manual/binder metaphor — tabs, numbered TOC, dotted leaders | **D liked** |
| 3 | D kept; E, F, G replace A/B/C | Monitor (terminal), card-catalog (library drawer), clipboard-form | Discarded, no specific feedback given |
| 4 | D kept; H, I, J replace E/F/G | Tile dashboard, wayfinding signage, engineering blueprint | Discarded — "don't invent new layouts" |
| 5 (current) | K, L, M replace D/H/I/J | REAL `LibraryPage` structure, 3 color/material treatments (warm, dark, navy) | **Awaiting feedback** |

**wg-easy live client list (confirmed via direct API query, `GET api/wireguard/client`, 2026-10-06) — the real registered devices, for reference:**

| Name | Address | Created |
|---|---|---|
| Klotho | 10.8.0.2 | 2026-08-30 |
| S23+ | 10.8.0.3 | 2026-09-06 |
| PC | 10.8.0.4 | 2026-09-09 |
| Rudolf Mana | 10.8.0.8 | 2026-09-24 |
| Sluzebni mobil | 10.8.0.9 | 2026-09-24 |
| Petr Faltus | 10.8.0.5 | 2026-09-24 |
| PC2 | 10.8.0.6 | 2026-10-04 |
| PC v praci | 10.8.0.7 | 2026-10-06 |

**WireGuard diagnostic test results (direct wg-easy API calls, before reading the real bug in code):**

| Test | Request | Result |
|---|---|---|
| Duplicate name | `POST {"name":"Petr Faltus"}` (already exists) | `200 {"success":true}` — no rejection |
| Fresh unique name | `POST {"name":"ZZZ Diagnostic Test"}` | `200 {"success":true}` |
| Czech diacritics (full) | `POST {"name":"Test Čeština"}` | `200 {"success":true}` |
| One accented letter (plain) | `POST {"name":"Zuzana Dvorak"}` | `200 {"success":true}` |
| One accented letter (accented) | `POST {"name":"Zuzana Dvořák"}` | `200 {"success":true}` |

All 5 test peers deleted afterward; remaining client list re-verified to exactly match the original 8 above.

**Translation feature — the exact OpenAI-compatible chat-completions request shape used** (`LocalAiLibraryTranslationService.TranslatePageTextAsync`):
```json
{
  "model": "{ModelName from Settings}",
  "temperature": 0.2,
  "messages": [
    {"role": "system", "content": "You are a precise medical-document translator. Translate the given page text into {TargetLanguage}. Preserve exact medical terminology, drug names, dosages, units, and numeric values exactly as written — never round or convert them. If the text includes a table rendered as plain text, keep it as readable running text (the original page image is shown separately for exact layout). Output ONLY the translated text, no commentary."},
    {"role": "user", "content": "{extracted page text}"}
  ]
}
```
Parses `choices[0].message.content`. Preflighted with `GET {baseUrl}/v1/models` before any page work.

**Release verification, 1.51(54):**
```
keytool -printcert -jarfile com.companyname.secureapp.presentation-Signed.apk
  Owner: CN=SecureApp, OU=ARIM KNTB Zlin, O=SecureApp Community, L=Zlin, C=CZ
  SHA256: 6E:75:39:28:45:B5:A2:91:6B:90:6C:AE:64:7B:5E:05:38:67:A4:2F:B6:FF:4F:49:43:29:EB:F2:03:41:B2:7C

POST /admin/upload/android -> {"uploaded":true,"sizeBytes":83068650,"versionCode":54,"versionName":"1.51"}
GET  /download/android/version -> {"VersionCode":54,"VersionName":"1.51","ReleasedAtUtc":"2026-10-06T14:43:57Z"}
```

**Full `RbacAction` enum (19 members, confirmed by an Explore agent reading `src/SecureApp.Domain/Enums/RbacAction.cs`) — only 2 have a Viewer-specific carve-out, everything else fuses Admin+Modifier:**

| Action | Viewer exception? |
|---|---|
| `CreateFolder`, `RenameFolder`, `DeleteFolder`, `MoveFolder` | No |
| `ImportDocument`, `RenameDocument`, `DeleteDocument`, `MoveDocument` | No |
| `CreateChatSession`, `SendMessage` | No |
| `UploadLibraryFile`, `DeleteLibraryFile` | No |
| `InviteGroupMember`, `RemoveGroupMember` | No |
| `ManageLogbookChecklists`, `ManageLogbookProcedureCatalog` | No |
| `RecordLogbookProcedure` | **Yes** — Viewer allowed |
| `ViewLogbookStatistics` | **Yes** — Viewer allowed |
| `EditWorkAssignment` | No |

No member here means "review a document" or "translate a document" — confirms the matrix structurally has no room for a 3rd Admin-only-but-not-Modifier tier, hence bypassing it entirely for the new translation gate.

**Build error round-trips (translation feature implementation):**

| # | Error | File | Fix |
|---|---|---|---|
| 1 | `MSB4025`: "An XML comment cannot contain '--'" | `SecureApp.Presentation.csproj` | Replaced `--` inside a `<!-- -->` comment with plain commas/words |
| 2 | `CS1061`: `IDocumentContainer` has no `Page` | `LocalAiLibraryTranslationService.cs` | Added `using QuestPDF.Fluent;`/`QuestPDF.Helpers;`/`QuestPDF.Infrastructure;` — fully-qualifying `Document.Create` doesn't pull in its extension methods |

Both caught by the same build-then-fix-then-rebuild loop (`dotnet build -f net10.0-windows10.0.19041.0`); final state 0 errors on Windows AND Android.

**`AskUserQuestion` rounds this session, exact options offered:**

| Question | Options offered | User picked |
|---|---|---|
| "Jak přesně by měla probíhat ta 'spolupráce' mezi mnou a lokální AI?" | (1) Poloautomaticky přes session se mnou (2) Plně automaticky, pouze lokálně (3) Plně automaticky, včetně cloudu (Claude API) | **None cleanly** — answered with "Ne" + revealed the real Ollama/LM Studio-on-PC setup instead |
| "Když admin v apce 'spustí překlad', co se tím přesně stane?" | (1) Appka/relay sama zavolá Ollama/LM Studio na PC (automaticky) (2) Je to flag/fronta — zpracuju to s tebou v session | **(1)** — fully automatic |
| "Co přesně by měl ten jiný návrh udělat jinak?" | (1) Jiná osobnost/styl celé appky (2) Méně 'komerční', víc klinické/vážné (3) Jiná obrazovka, ne Knihovna (4) Konkrétní inspirace/odkaz | **(1)** — different personality/style |

## Code Analysis

- **The real WireGuard bug, exact mechanism:**
  - `SettingsViewModel.Updates.cs`'s `CreateWireGuardAccessAsync` (success path) sets `NewMemberNameText = string.Empty;` right after creating the wg-easy client.
  - The SEPARATE `DownloadWireGuardConfigAsync` command re-reads that SAME bound property to build the `.conf` filename (`Path.GetInvalidFileNameChars()`-sanitized).
  - By the time a user clicks the download button, the field is already empty — `safeName` is empty, so the code falls back to the literal `"wireguard"` name, EVERY time, for EVERY member.
  - Importing a second/third identically-named `wireguard.conf` into the Windows WireGuard client makes it auto-suggest a deduplicated tunnel name like `wireguard[1]` to avoid colliding with the first import.
  - THAT generated name then fails the client's own tunnel/adapter-name validation (square brackets aren't a valid character there), surfacing as "Název tunelu je neplatný: wireguard[1]".
  - Nothing to do with duplicate MEMBER names at all — confirmed by two live negative tests against wg-easy's own API before finding this.
- **wg-easy's relay HTTP port is bound to the Pi's LAN IP specifically (`192.168.50.8:8080`), not to loopback.**
  - Confirmed empirically: `curl http://localhost:8080/...` from a shell ON THE PI ITSELF gets `curl: (7) Failed to connect ... Could not connect to server`.
  - `curl http://192.168.50.8:8080/...` from the SAME shell works fine.
  - Worth remembering for any future Pi-side script that assumes `localhost` reaches the relay.
- **`curl -s` suppresses BOTH the progress meter AND error/diagnostic messages** — not just progress, as its one-line man-page description implies.
  - A failed connection with `-s` alone produces ZERO output, not even to stderr.
  - This cost real debugging time this session before switching to `-sS` (silent but show errors) and finally seeing the real `curl: (7)` message.
- **`ILibraryTranslationService` lives in `SecureApp.Domain.Interfaces.Services`, pure BCL-typed interface** — same Domain/Presentation split as `IDocumentRenderingService`/`IUpdateService`.
  - The interface has zero NuGet dependencies.
  - The one real implementation (`LocalAiLibraryTranslationService`, Presentation layer) needs `HttpClient`+PdfPig+QuestPDF.
- **`LocalAiLibraryTranslationService` registered `AddScoped`**, mirroring `IDocumentRenderingService`'s own registration (which it depends on).
  - Injected directly into the Transient `LibraryViewModel`/`LibrarySubcategoryDetailViewModel`/`SettingsViewModel`.
  - Same direct-injection pattern `DocumentViewerViewModel` already used for `IDocumentRenderingService`.
- **`QuestPDF.Settings.License = LicenseType.Community;`** is a mandatory one-time call before QuestPDF's first use (throws at runtime otherwise) — placed in `MauiProgram.cs`, right at the top of `CreateMauiApp()`.
- **Fully-qualifying `QuestPDF.Fluent.Document.Create(...)` does NOT pull in its extension methods** (`.Page()`, `.Content()`, `.Text()`, etc.).
  - Those need `using QuestPDF.Fluent;`/`QuestPDF.Helpers;`/`QuestPDF.Infrastructure;` actually in scope — qualification at the call site doesn't help C#'s extension-method resolution.
  - Cost one build-error round-trip.
- **`LibraryFileItem` (the shared record between `LibraryViewModel`'s search-results list and `LibrarySubcategoryDetailViewModel`'s file list) gained a `CanTranslate` field with a DEFAULT value (`= false`).**
  - Specifically so the one existing positional-constructor call site that wasn't updated in the same edit still compiled.
  - Later updated anyway for correctness, but the default-parameter trick itself is this codebase's established non-breaking-record-growth pattern — see `DevicePolicy.IsDocumentReviewer`'s own precedent from an earlier session.

- **`LocalAiLibraryTranslationService` retry/timeout constants** (all private `const`/`static readonly` in the class): `MaxRetries = 2`, `RetryDelay = TimeSpan.FromSeconds(3)`, `PingTimeout = TimeSpan.FromSeconds(5)`, main `HttpClient.Timeout = TimeSpan.FromMinutes(3)` (one vision-free text call per page — generous since actual latency depends entirely on the admin's own hardware/model choice, not anything this app controls).
- **Translation-call JSON shape** uses 3 private nested records inside the service, not shared DTOs: `ChatCompletionRequest(string Model, double Temperature, List<ChatMessageRequest> Messages)`, `ChatMessageRequest(string Role, string Content)`, `ChatCompletionResponse(List<ChatChoice> Choices)` → `ChatChoice(ChatResponseMessage Message)` → `ChatResponseMessage(string Content)` — deliberately NOT reusing any OpenAI SDK type (none is referenced anywhere in this codebase), just enough shape to deserialize `choices[0].message.content`.
- **Constructor dependencies of `LocalAiLibraryTranslationService`**: `IDocumentRenderingService`, `ISharedLibraryService`, `ILibraryReviewService`, `ICryptoService` — notably NOT `IDocumentRepository` directly, since `DownloadAndImportAsync`'s returned `Document` already carries `EncryptedContent` for `ICryptoService.DecryptAsync` to use.

## Plan-Mode Research Detail (the 3 Explore agents + 1 Plan agent for the translation feature)

All launched via `EnterPlanMode`, full reports preserved in this session's transcript; key facts not already captured elsewhere in this handoff:

- **Agent 1 (Document Library review workflow):**
  - Confirmed there is NO `LibraryDocument`/`LibraryDocumentVersion` ENTITY — the whole feature is modeled as immutable `record`s (`LibraryDocumentSummary`, `LibraryDocumentVersionSummary`, `LibraryDocumentReviewEntry`, `LibraryDocumentDetail`).
  - The relay owns ALL mutable state; the client only ever sees DTOs.
  - `LibraryReviewPolicy.CanReview(isReviewer, isAdmin, reviewerDeviceId, submitterDeviceId)` is pure/static and blocks self-review UNCONDITIONALLY, even for Admin — directly informed the verification step "attempt to approve from the same admin device that submitted it, confirm it's still blocked."
  - `ILibraryReviewService` has exactly 7 methods: `CreateDraftAsync`, `AddVersionAsync`, `SubmitForReviewAsync`, `GetMyDocumentsAsync`, `GetPendingReviewAsync`, `GetDetailAsync`, `ReviewAsync`.
  - Staging a new version is LITERALLY `ISharedLibraryService.UploadAsync(..., listed:false)` — the same call a private chat attachment already uses, zero new crypto code needed.
- **Agent 2 (rendering/import/OCR feasibility):**
  - Confirmed, by direct inspection of the installed `PDFtoImage` 5.4.0 package's own doc-comments, that its ENTIRE public surface is `SavePng`/`SaveJpeg`/`SaveWebp`/`ToImage`/`ToImages`/`GetPageCount` — rasterize-only.
  - The only "Text" hit anywhere in the package is the unrelated `PdfAntiAliasing.Text` rendering-quality enum.
  - Broad repo-wide grep for `PdfPig|iText|Tesseract|ExtractText` etc. found literally zero existing text-extraction/OCR code anywhere — confirmed the feature needed a brand-new dependency, not an unused existing one.
  - Found a stale reference worth flagging: `IMPROVEMENT_PLAN.md`'s own Phase 7 (written earlier the SAME session) said to reuse "the same toolkit already used for the Milestone 4 moving watermark" — but the watermark feature was fully DELETED on 2026-09-30 (`PixelWatermark.cs` and the standalone decoder tool both removed, confirmed zero matches in current `src/`).
  - The agent flagged the REAL live SkiaSharp-drawing precedent is `DocumentRenderingService.RenderTextPage`/`WrapText`/`PaginateText` (used for `PlainText` documents), not the watermark — ended up moot anyway once QuestPDF's own text layout was chosen, but a good example of catching a stale self-reference before acting on it.
- **Agent 3 (HTTP-client/RBAC/Windows-gating patterns):**
  - Confirmed EVERY existing `Http*Service` in this codebase (`HttpSharedLibraryService`, `HttpLibraryReviewService`) uses a plain inline `private readonly HttpClient _httpClient = new() { Timeout = ... }`.
  - This project has NEVER used `AddHttpClient`/`IHttpClientFactory` anywhere, confirmed by a full-codebase check, not an assumption.
  - Confirmed the Windows-only gating idiom used by the 2026-10-05 multi-profile-login feature is a plain runtime `DeviceInfo.Current.Platform == DevicePlatform.WinUI` boolean check at the point of use (`ActiveProfile.cs`, `TrustedAdminDevices.cs`).
  - NEVER `#if WINDOWS` compile-time exclusion anywhere in this codebase for a feature-level gate (that form IS used, but only for registering a platform-only service TYPE in `MauiProgram.cs`, a different concern).
  - Found the full current `RbacAction` enum (19 members, see Evidence & Data table) and confirmed its `RoleAccessPolicy.IsAllowed` switch has exactly 2 Viewer-specific exceptions, otherwise fuses Admin+Modifier as fully equivalent — directly caused the decision to bypass `RbacAction` for the translation feature's admin-only gate.
- **The Plan agent's synthesis flagged 2 deviations from my own draft assumptions, both adopted:**
  - (1) Wire the 🌐 trigger into BOTH `LibraryPage.xaml` (search-results list) AND `LibrarySubcategoryDetailPage.xaml` (the actual primary "browse a category" screen post-2026-10-02-redesign) — my own first-pass plan had only considered the former.
  - (2) Use QuestPDF's own native text layout/overflow for the translated pages rather than porting `DocumentRenderingService.WrapText`.
  - The "Překlad — strana n/N originálu" header is stamped on every translated page specifically so the original↔translation pairing survives even if QuestPDF's own pagination pushes one translated page onto 2+ physical pages.

## Design Direction — Exact Colors/Fonts Per Round (for picking back up any discarded element)

**D ("Terénní manuál", liked, now structurally discarded but its MATERIAL carried into K):** bg `#F7F4EC`, ink `#1C1A16`, spot/binder color `#2E4B3C` (forest green), urgent `#8C2F2F` (muted oxblood). Fonts: `Source Serif 4` (headings), `Source Sans 3` (body), `IBM Plex Mono` (codes/numbers/leader-line labels). Binder tabs: active tab solid `#2E4B3C` fill white text, inactive outlined. Index rows: dotted leader (`border-bottom: 1px dotted #C9C2AC`) from category code to file size, bracketed annotations `[STAT]`/`[PŘEKLAD]` instead of icon badges.

**E (Monitor, discarded):** bg `#0A0E0C`, phosphor green `#39FF88`, dim green `#1F6B43`, alarm red `#FF4D4D`. Font: `JetBrains Mono` throughout. Critical row got `background: rgba(255,77,77,0.10)` tint + `⚠STAT` marker. Footer styled as softkeys `[F1] SOUBORY` etc.

**F (Katalogová karta, discarded):** bg `#E8E0CB` (kraft), card `#FBF8EE`, ink `#2B2518`, brass `#A8752B`, stamp-red `#8C2F2F`. Font: `Courier Prime` (typewriter) throughout. Cards had a corner-notch via `clip-path: polygon(0 0, calc(100% - 18px) 0, 100% 18px, 100% 100%, 0 100%)`, slight alternating rotation (`-0.6deg`/`0.4deg`/`-0.3deg`), circular rotated rubber-stamp badges.

**G (Klinický formulář, discarded):** bg `#F4F5F6`, ink `#1D2226`, border `#C7CCD1`, official-stamp blue `#2F5C8A`, stamp-red `#8C2F2F`. Fonts: `IBM Plex Sans` + `IBM Plex Mono`. Drawn CSS-only paper-clip graphic at the top (two stacked rounded rects, no image asset). Checkbox category selector (real bordered squares + SVG checkmark), numbered-circle rows on ruled form lines.

**H (Dlaždice, discarded):** bg `#F2F2F2`. Flat solid category tiles, no gradients: Doporučení `#0F6E8C`, Resuscitace `#B23A48`, Postupy `#3D4F91`, Výuka `#C98A1B`, Nástroje `#55606B` — asymmetric grid (one wide tile + 4 smaller), white line-icon + bold label bottom-left (Metro/Fluent convention).

**I (Navigační cedule, discarded):** bg `#F7F8F9`. Same 4 category colors as H, but as FULL-WIDTH bands (not tiles) — `Archivo` font (weight 700/800) for the confident signage look. Document entries as bold-bordered (`2px solid #1C1C1C`) white plaques with big numerals.

**J (Technický výkres, discarded):** bg `#0C2D4A` (blueprint navy) with a faint grid texture via DOUBLE `repeating-linear-gradient` (one horizontal, one vertical, `rgba(191,227,240,0.07)` lines every 24px). Cyan `#5FD1E8`, critical-red `#FF6B6B`. Font: `IBM Plex Mono` throughout. Items annotated like a parts list: `KAT: DOP   VEL: 2.4MB` under each title.

**K (current, warm — the one carrying D's material into the REAL layout):** bg `#F6F3EC`, surface `#FFFFFF`, border `#E4DECD`, accent `#0D4E64` (today's actual app teal). `Spectral` serif ONLY on subcategory-grid card titles; everything else `Source Sans 3`. Category tiles: real emoji (📚⭐❤💉📖), selected tile gets `#D9E8EA` bg + `#0D4E64` border. Akutní-stavy pills: `#F3E2DD` bg (matches the real app's `DangerSoft` token).

**L (current, dark):** bg `#121417`, surface `#1C2024`, border `#2A2F34`, accent amber `#FFB454`. `Inter` body, `IBM Plex Mono` UPPERCASE for every tile/tab label. Akutní-stavy pills: `#2A1414` bg, `#FF8080` text.

**M (current, navy/technical):** bg `#EDEFF1`, surface `#FFFFFF`, border `#D7DBDF`, accent navy `#16324F`. `IBM Plex Sans` + `IBM Plex Mono` UPPERCASE, sharp `4px` corner radius throughout (vs. K's `10-12px` and L's `10px`). Akutní-stavy pills: `#FBE9E5` bg, `#E2572B` border, `#B33A1D` text — the ONE place red appears in M, matching the real app's own existing `DangerSoft`-adjacent convention.

## Reusable Technique Notes

- **Reading a secret from a remote `.env` for USE, without ever materializing it in Claude's own output**: write a local `.sh` file that does `set -a; source ~/path/.env 2>/dev/null; set +a`, then uses `$VAR` only INSIDE a `curl -H "Header: $VAR"` call — `scp` the script to the Pi, `ssh ... "bash /tmp/script.sh"`, then delete it. The variable's value never appears in any tool-visible output, only its EFFECT (an authenticated HTTP call) does. Used successfully both for `SECUREAPP_WGEASY_PASSWORD` (listing/creating/deleting wg-easy clients) and `SECUREAPP_RELAY_ADMIN_SECRET` (the Android release upload) this session — confirms this pattern generalizes to ANY secret living in that `.env` file, not just the one used in a prior session.
- **`curl -s` alone hides connection failures silently — always use `-sS` for any unattended/scripted call where you need to know WHY it failed.** Cost a real debugging detour this session: two separate `curl -s -i ... > log` calls against a genuinely unreachable `localhost:8080` produced a completely empty log (not even a blank-looking error), which looked exactly like "the command didn't run" rather than "the command failed silently." Switching to `-sS` immediately surfaced the real `curl: (7) Failed to connect` message.
- **wg-easy's relay container publishes its port bound to the Pi's specific LAN IP (`192.168.50.8`), not `0.0.0.0`/loopback** — any FUTURE Pi-side script that wants to hit the relay's own HTTP API must use `192.168.50.8:8080`, never `localhost:8080`, even when running ON the Pi itself.
- **`keytool -printcert -jarfile <apk>` is the correct one-line signature check** for a signed APK (NOT `-list -printcert`, which errors with "Only one command is allowed", and NOT `-list -v -jarfile` alone, which demands a keystore path you don't have). This is the exact command every release this project has ever shipped should be checked with before upload.
- **A native Windows exe (git.exe, ssh.exe) invoked from PowerShell with a multi-line here-string commit message containing embedded DOUBLE QUOTES will silently mis-parse** — PowerShell's own re-quoting for native-process argument passing breaks on literal `"` characters inside an otherwise single-quoted `@'...'@` here-string, splitting the message into bogus extra "pathspec" arguments. Fix: avoid embedded double quotes in commit messages entirely (use plain words or single quotes instead) — hit this twice this session (once for `git commit`, confirmed the exact same failure mode both times) before remembering to just not use `"` in commit message bodies.

## Exact Verbatim Quotes, Hard to Re-Derive From Paraphrase

- *"Ok muzeme zacit fazi 2 byla by pristupna pouze pro admin... potreboval bych aby dochazelo ke spolupraci s tebou a lokalni ai aby byl preklad co nejdokonalejsi klidne v jakemkoli formatu citelny v pc, tel a podobne... co nejvice podobny originalu co se grafiky tyce preklad co nejpresnejsi bude se jednat o lekarske nekomercni texty nebo zakoupene knihy k bezplatnym vyukovym ucelum..."* — the full original ask, before any clarifying questions; "co nejvíce podobný originálu co se grafiky týče" (as close to the original as possible regarding graphics) directly drove the interleaved-original-page-image design.
- *"Ne, mam na pc olamu i lm studio na rtx 5060 s 16gb by to mohlo trochu fungovat... a sposti preklad admin uklada se v cloudu v neschvalenych ale adminovi dostupnych dokumentech ktery pokud bude preklad dobry a vizualne spravne (grafy, diagramy tabulky atd) tak je schvali nebo posle k sirsimu schvaleni..."* — the answer that revealed the real hardware AND independently described (without being asked) almost exactly the existing Draft→PendingReview→Published workflow, in the user's own words, before I'd mentioned reusing it.
- *"No podivej asi bych nechtel menit rozlozeni spis mi zkus rozlozeni ktere mame ukazat v tech navrzich..."* — the exact phrasing of the design-exploration course-correction; note "rozložení, které MÁME" (the layout we ALREADY HAVE) — an explicit pointer to the real existing app, not a request for new exploration.
- *"Kdyz dam vytvorit novy wireguard tak mi to pise wireguard[1] neplatny nazev nemuze se tunel menovat jako nazev clena?"* — the user's own hypothesis, phrased as a question, about why the error occurred; worth noting the exact bracket-and-number format `wireguard[1]` was the literal clue that (once tested empirically rather than assumed) pointed to the Windows WireGuard client's own auto-dedup naming, not a wg-easy/relay-side validation rule.
- *"Musim to predat z tel."* — four words that reframed which platform needed the fix; this exact brevity is consistent with this project's established pattern of very short, high-information corrections.

## Exact Signature/API Changes This Session (for future grep/reference)

- `Program.cs` (relay): `DownloadPageHtml(bool androidAvailable, string? androidSize)` → `DownloadPageHtml(bool androidAvailable, string? androidSize, bool windowsAvailable, string? windowsSize)` — the `/download` handler now computes both flags and passes all 4 args.
- New public surface, `SecureApp.Domain.Interfaces.Services.ILibraryTranslationService`: `Task<LibraryDocumentSummary> TranslateAndSubmitAsync(Guid sourceLibraryFileId, string sourceTitle, string sourceFolderPath, string targetLanguage, IProgress<double>? onProgress = null, IProgress<string>? onStatusText = null, CancellationToken ct = default)`, `Task PingAsync(CancellationToken ct = default)`.
- New public surface, `SecureApp.Presentation.Translation.LocalAiTranslationSettings` (static): `GetBaseUrl()`/`SetBaseUrl(string)`, `GetModelName()`/`SetModelName(string)`, `DefaultBaseUrl` = `"http://localhost:11434"`.
- `LibraryViewModel.ToItem`: gained a `bool canTranslateDocuments` parameter — `ToItem(SharedLibraryFileSummary summary, Guid? myDeviceId)` → `ToItem(SharedLibraryFileSummary summary, Guid? myDeviceId, bool canTranslateDocuments)`.
- `LibrarySubcategoryDetailViewModel.ToFileItem`: same shape change — gained `bool canTranslateDocuments`.
- `LibraryFileItem` record: gained a trailing `bool CanTranslate = false` field (default value keeps old positional-constructor call sites source-compatible).
- `SettingsViewModel`'s constructor: gained a new required parameter `ILibraryTranslationService libraryTranslationService` (inserted before the existing optional `INativeAppInstaller?`/`INativeUpdateDownloader?` trailing params, so DI resolution order is unaffected).
- `SettingsViewModel.Updates.cs`: new private field `_lastWireGuardMemberName` (string, defaults to `string.Empty`) — `DownloadWireGuardConfigAsync`'s filename-building line changed from reading `NewMemberNameText` to reading `_lastWireGuardMemberName`.
- `MauiProgram.cs`: gained `QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;` as the very first statement inside `CreateMauiApp()`, before `FontScaling.Register()`'s neighboring calls.

## Files Changed

### Relay (`SecureApp.Relay`)
- `Program.cs` — `DownloadPageHtml` gained a Windows section (parallel to the Android one); `/download` handler now checks both files' existence.

### Domain
- `Interfaces/Services/ILibraryTranslationService.cs` — new. `TranslateAndSubmitAsync(...)` + `PingAsync(...)`.

### Presentation — new files
- `Translation/LocalAiLibraryTranslationService.cs` — the orchestrator: decrypt → PdfPig extraction → per-page render+translate → QuestPDF build → upload → draft → submit.
- `Translation/LocalAiTranslationSettings.cs` — `Preferences`-backed base-URL/model-name config.
- `ViewModels/SettingsViewModel.LocalAiTranslation.cs` — the new Settings card's properties/commands.

### Presentation — changed
- `MauiProgram.cs` — QuestPDF license line + `AddScoped<ILibraryTranslationService, ...>`.
- `SecureApp.Presentation.csproj` — new `PdfPig`/`QuestPDF` package refs; version bump 1.50(53)→1.51(54).
- `ViewModels/LibraryViewModel.cs` — `CanTranslateDocuments`/`IsTranslating`/`TranslationProgress`/`TranslationStatusText` properties, ctor DI, `ToItem` signature, `LibraryFileItem` record `+CanTranslate`.
- `ViewModels/LibraryViewModel.Actions.cs` — `ComputeCanTranslateDocuments()`, `TranslateCommand`.
- `ViewModels/LibrarySubcategoryDetailViewModel.cs` — same gating/command, mirrored (this is the actual primary browse screen).
- `ViewModels/SettingsViewModel.cs` — ctor DI for `ILibraryTranslationService`.
- `ViewModels/SettingsViewModel.Updates.cs` — **the WireGuard bug fix**: new `_lastWireGuardMemberName` field, captured before `NewMemberNameText` is cleared, used by `DownloadWireGuardConfigAsync` instead of the now-empty bound property.
- `Views/LibraryPage.xaml` — 🌐 button + `ProgressBar`/status-text block in the search-results item template.
- `Views/LibrarySubcategoryDetailPage.xaml` — same, in the Files item template.
- `Views/SettingsPage.xaml` — new "Admin: Místní AI překlad" card.

### Docs
- `IMPROVEMENT_PLAN.md` — Phase 3 fleshed out; Phases 6+7 added then marked `[x]` done same session.

### Not touched (growing debt)
- `DEVELOPMENT_PLAN.md`/`NOTIFICATION_HUB_SPEC.md` — now missing BOTH the 2026-10-05 Windows multi-profile login AND this session's local AI translation feature.

## User Feedback & Preferences (REQUIRED — never omit)

- *"A musi byt orc? Budou to elektronicke dokumenty hlavne pdf?"* — a precise, well-informed technical correction mid-planning (OCR isn't needed since these are electronic documents) that reshaped the whole feature's architecture before any code was written — the user caught a wrong assumption early, worth taking this kind of correction seriously and re-deriving the design from it rather than patching around it.
- *"Ne, mam na pc olamu i lm studio na rtx 5060 s 16gb by to mohlo trochu fungovat..."* — answered my own clarifying question with a concrete fact (real hardware/software already in place) that simplified the architecture more than any of the 3 options I'd offered — a reminder that an open-ended "what do you actually have" answer can beat a multiple-choice guess.
- *"To jsou graficke ramce?"* and *"Nebo to je jen jeden navrh?"* — both genuine confusion about how the Artifact canvas UI presents multiple artboards (zoomed/focused single view vs. full canvas) — not a complaint about the designs themselves; worth proactively explaining canvas navigation when presenting multi-artboard work.
- *"No nevidim nejake rozdily mezi jednotlivymi navrhy krom ze je jeden tmavy druhy svetly...."* — direct, correctly-targeted critique: subtle palette/corner-radius variation isn't enough differentiation; structural differences register, color-temperature alone doesn't.
- *"No myslim zkus odlisny navrh..."* — wanted genuine variety, not refinement; correctly treated as "try a different approach" rather than "the same approach, different numbers."
- *"D lepsi A az C vyhod a zkus dalsi navrhy"* — decisive, specific feedback (keep exactly one, discard the rest, want more) — easy to act on precisely.
- *"No podivej asi bych nechtel menit rozlozeni spis mi zkus rozlozeni ktere mame ukazat v tech navrzich..."* — the single most important correction of the design-exploration thread: ALL prior mockups (including the liked D) had been inventing new structural layouts instead of restyling the real one — a scope misunderstanding on my part that cost 3 full rounds of exploration. Worth remembering: when a user asks for "design directions," confirm explicitly whether that means new layout/IA exploration or visual-material restyling of the existing layout — don't assume.
- *"Kdyz dam vytvorit novy wireguard tak mi to pise... nemuze se tunel menovat jako nazev clena?"* — the user offered their OWN hypothesis for a bug, which turned out to be wrong, but asking "is this constraint real?" rather than just reporting the symptom raw was still useful context — testing the hypothesis directly and explicitly reporting it was disproven built trust before landing on the real cause.
- *"Porad to pise..."* — a terse "still broken" after a fix was shipped — correctly NOT alarming, since the fix was source-only and hadn't been rebuilt/released yet; worth explicitly flagging "this needs a rebuild to take effect" whenever a fix is committed but not yet built/deployed, so a retry-without-rebuild doesn't read as a failed fix.
- *"Musim to predat z tel."* — a short clarification that fully reframed which PLATFORM needed the fix (Android, not Windows) — a reminder that "which device is the user actually using for this action" is worth confirming explicitly for any cross-platform shared-code bug, rather than assuming the platform from context.
- *"Udelej release"* — matches the long-established pattern (confirmed again, 3rd+ session running): release requests come as their own short, standalone message, never bundled into a feature-request turn, and should be actioned as a normal build→sign→upload→verify→commit→push cycle without re-asking for confirmation.
- **The session-start onboarding instruction itself** (paste prompt continuing from seq 6) asked for a specific ritual before acting: summarize understanding of the parent handoff, verify git/relay/device state, read the key files it names, explore 2-3 ADJACENT files not explicitly listed, state the planned first action, then WAIT for explicit go-ahead before executing. Followed exactly this once (the pairing-loop onboarding at the start of this session) before the user redirected to something else entirely — worth remembering this ritual is the user's own explicitly-requested process for picking up ANY handoff, not a one-off ask.

## Where We're Going

1. **Design direction decision (K/L/M) — the single most important open item.**
   - Awaiting feedback on whether any of K (warm/teal/serif), L (dark/amber/mono), or M (navy/cool/mono) lands, now that they faithfully restyle the REAL `LibraryPage` layout rather than inventing a new one.
   - If one lands: implement it for real across the app's actual `Styles.xaml`/`Colors.xaml`, not just this one mockup screen — that's a much bigger pass than the mockup itself.
   - If none land: ask explicitly what specific ELEMENT (not just "try again") is wrong — color temperature, font pairing, density, something else — before building a 6th round.
2. **Confirm the WireGuard fix actually resolves the live issue.**
   - Needs the user to retest from their phone once 1.51(54) is installed.
   - Separately: clean up any stray `wireguard`-named tunnel already sitting on the target Windows PC's WireGuard client from before the fix — that leftover won't self-heal even with the fix installed.
3. **Live-verify the translation feature end-to-end.**
   - Code builds clean but has never been run against a real Ollama/LM Studio instance.
   - Needs: set the Settings card, "Testovat připojení", translate a real short Library PDF, confirm it lands correctly in `LibraryReviewQueuePage`.
   - Also worth checking: does the translated PDF actually look right when opened (original page, then translation, correctly paired)?
4. **The carried-over 7-item backlog from seq 6 is now 2 full sessions deep.**
   - Items: pairing-loop root cause, Android UI fix verification, Petr Faltus's phone, `WebSocketException`, `SearchBar` crash, documentation debt, Windows multi-profile distribution.
   - Worth a direct conversation with the user about whether to finally dedicate a session to it, or explicitly accept it as permanently-deferred background debt.
5. **Documentation debt is now compounding.**
   - `DEVELOPMENT_PLAN.md`/`NOTIFICATION_HUB_SPEC.md` are missing TWO entire subsystems: the 2026-10-05 Windows multi-profile login, and this session's local AI translation feature.
   - The longer this goes, the more an eventual catch-up pass costs — worth raising proactively rather than waiting for the user to notice the gap.

## Verification Status Matrix — Every Change This Session

| Change | Verified how | Confidence |
|---|---|---|
| Windows download-page link | Re-fetched `/download` live, confirmed new link text present | **High** — direct observation |
| Translation feature — builds | `dotnet build` on both Windows and Android targets, 0 errors each | **High** — direct, repeatable |
| Translation feature — actual AI call/output quality | Never run against a real Ollama/LM Studio instance | **None** — code-reasoning only |
| Translation feature — review-workflow integration | Never exercised (would need a real translation to run first) | **None** |
| WireGuard root-cause diagnosis | Live negative tests against wg-easy's own API (duplicate name, diacritics) + direct code reading | **High** — empirically disproved the wrong theory, then confirmed the right one in code |
| WireGuard fix itself | `dotnet build` Windows, 0 errors; logic traced by hand, not executed live | **Medium** — sound reasoning, not yet observed fixing the real symptom |
| Release 1.51(54) upload | `keytool -printcert` confirmed real signing cert; `/download/android/version` confirmed `54`/`1.51` live | **High** — direct observation of both signature and server state |
| Release 1.51(54) actually fixing the WireGuard issue on-device | Not yet — user was still on the OLD build when the session ended | **None** |
| Design mockups K/L/M | Published to the Artifact canvas; visual correctness never confirmed by the user | **Unknown** — awaiting feedback |

## Risks & Blockers

- **The translation feature's all-or-nothing failure policy is untested under real failure conditions.**
  - If a real Ollama/LM Studio instance times out or errors mid-document, the actual retry/abort behavior has only been designed, never observed live.
- **Design exploration has now gone 5 rounds without landing.**
  - If K/L/M also don't land, worth stepping back and asking what dimension of feedback is still missing, rather than producing a 6th round on pure guesswork.
- **The WireGuard fix is unverified live.**
  - It's a confident, code-confirmed root-cause fix, but the user was still actively blocked when the session ended.
  - If retesting surfaces a SECOND distinct issue, don't assume it's the same bug recurring — re-diagnose from scratch.
- **Petr Faltus's phone status is unknown.**
  - Last confirmed silent as of seq 6 (2026-10-03T13:20:02Z); not re-checked this session, now potentially 3+ days silent.

## Open Questions

- Does the WireGuard fix, once actually installed on the phone and retested, fully resolve the reported error? Not yet confirmed.
- Does the stray leftover `wireguard`-named tunnel (if one exists on the target PC) need to be manually removed, or did the user already handle that? Not confirmed either way.
- Which (if any) of K/L/M is the right visual direction — or does a 6th round + more specific feedback dimension (what exactly about K/L/M works or doesn't) need to happen first?
- Is the stuck 20-minute pairing-loop (PC ↔ Vilém's S23+) still recurring? Not re-checked since seq 6 found it still live.
- Is Petr Faltus's phone still silent, and for how long now?
- Does a real Ollama/LM Studio translation actually produce medically-sensible output, or does the prompt need tuning once a live run happens?

## Dependencies Noted This Session

- **PdfPig** (new NuGet dependency, MIT-licensed) — pure text-layer extraction from an already-electronic PDF, no OCR. Depends on the source PDF genuinely being born-digital; a scanned page inside an otherwise-electronic document degrades gracefully (placeholder note) but is NOT translated.
- **QuestPDF** (new NuGet dependency, Community license — free for this app's non-commercial internal use) — authors the translated-output PDF. Requires the one-time `QuestPDF.Settings.License` call; the installed version pulled in a native `libqpdf.so` that triggered a NEW Android build warning (`XA0141`, 16KB page-size requirement on Android 16) — not an error, not yet acted on, but worth knowing if a future Android OS target bump makes it one.
- **Ollama/LM Studio** (the user's own existing local setup, RTX 5060 16GB) — the translation feature's entire quality ceiling depends on whatever model the admin has loaded there; this project has no control over or visibility into that beyond the configured base URL + model name.
- **wg-easy's own API behavior** (confirmed empirically, not from its docs) — accepts duplicate client names and non-ASCII names without validation; this project's own client-side code is the only place any name-safety logic exists (the `.conf` filename sanitization that had the bug).

## Quick Start for Next Session

```powershell
# Reference docs
Get-Content "H:\Visual Studio\C#\Aplikace\plans\handoffs\HANDOFF_standalone-be860f68_widget-wireguard-multiprofile_2026-10-05.md"  # seq 6, still-open carry-over list
Get-Content "C:\Users\dvora\.claude\plans\nested-crafting-bee.md"  # approved translation-feature plan, full detail

# Confirm current git/relay state
$git = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
& $git log --oneline -8
& $git status

# Check current app version (expect 1.51 / build 54 as of this handoff)
curl.exe -s http://192.168.50.8:8080/download/android/version

# Design exploration — open the canvas to see current K/L/M state, or ask the user for feedback directly
# https://claude.ai/artifact/YSzBS3h95qordofUsimg6t

# WireGuard fix — key files if retesting surfaces something new
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\ViewModels\SettingsViewModel.Updates.cs"  # CreateWireGuardAccessAsync (~line 142), DownloadWireGuardConfigAsync (~line 202)

# Translation feature — key files if live-testing surfaces a bug
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\Translation\LocalAiLibraryTranslationService.cs"
Get-Content "H:\Visual Studio\C#\Aplikace\src\SecureApp.Presentation\ViewModels\LibraryViewModel.Actions.cs"  # TranslateAsync (~line 199)

# Next action — pick based on what the user asks for first:
# 1) Design direction: get feedback on K/L/M, or ask what specifically to vary next.
# 2) Confirm WireGuard fix works once the phone is on 1.51(54).
# 3) Live-test the translation feature against a real Ollama/LM Studio instance.
# 4) Finally address the seq-6 carry-over backlog (pairing loop, Petr Faltus, WebSocketException, docs debt) — now 2 sessions deep.
```

## Session Closed
**Closed at:** 2026-10-06
**Commit:** `e565738` (pushed to `pi` and `github`)
**Session status:** Handed off to next session
