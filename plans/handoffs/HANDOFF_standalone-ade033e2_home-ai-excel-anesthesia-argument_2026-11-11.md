# Home AI exact Excel queries (0.1.29 shipped, 0.1.30 pending) + hospital anesthesia-staffing argument built from two exports

**Date:** 2026-11-11
**Status:** IN PROGRESS (two threads: (A) 0.1.30 code done but unreleased — waits for the user's explicit "Vydej"; (B) data analysis for the director, text/page not yet rewritten with final numbers)
**Bead(s):** none (bd not installed on this machine)
**Epic:** SecureApp Domácí AI (home LLM) + side project: argument for a 3rd anesthesia service
**Chain:** `standalone-ade033e2` seq `1`
**Parent:** `none — first in chain`
**Prior chain:** `none — first in chain`

---

## Related Handoffs

- `HANDOFF_standalone-24caf3f8_releases-0-1-5-tunnel_2026-10-30.md` — older release/tunnel work (0.1.5), separate work stream, reference only.

## Reference Documents

- `C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md` — index of persistent memories (release pipeline `release-artifact-paths.md`, infra paths `secureapp-infra-paths.md`, never-debug-deploy rule, batch-builds rule).
- `C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\4d855aea-f698-44c0-b3ec-8a4f187ba1af.jsonl` — full transcript of this session (4.–11. 11. 2026, ~27 MB, 115 user messages). User-message outline extracted to `C:\Users\dvora\AppData\Local\Temp\claude\22b5d484d26ebaa4\4d855aea-f698-44c0-b3ec-8a4f187ba1af\scratchpad\user-msgs.txt`.
- `relay/ops/README.md` (in repo) — relay deploy pipeline.

## The Goal

Two parallel goals. (A) SecureApp (.NET MAUI Android + Windows, Raspberry Pi relay) has an admin-only "Domácí AI" feature that chats with a home LM Studio model; the user wants it to answer questions about uploaded Excel exports **exactly** (counts, sums, filters, percentages) instead of the LLM guessing — the model only writes a JSON query, the program executes it. (B) The user (hospital anesthesiologist; Czech, writes without diacritics) wants concrete, honest arguments for a **third anesthesia service** (or a shorter extra shifted shift) from two exports: `KCC_1.xlsx` (angio/clinical events) and an operating-room export of operations running past 15:00. Standing rule: **release only on explicit "Vydej"** (and "Nasad" = deploy).

## Where We Are

### Thread A — Home AI (SecureApp)
- **0.1.29 / versionCode 135 released** (commit `c1e95ba`): schema-forced JSON query (`response_format` json_schema strict, enums, fallback on HTTP 400), `AnswerNumberCheck` (grey note "✓ Kontrola čísel…" / "⚠ …"), `HomeAiException.StatusCode`, `HomeAiClient.StreamChatAsync(..., object? responseFormat = null)`.
- **0.1.30 / 136 NOT released.** Uncommitted changes (git status: `M DocumentTextExtractor.cs`, `M TableQuery.cs`, +166/−22 lines):
  - `DocumentTextExtractor` is now `internal static partial class`; new `Typed(object?)` converts text-typed cells (dates, HH:MM times, numbers) to DateTime/TimeSpan/double; `ExtractSpreadsheet` uses `Typed(reader.GetValue(i))`; `CellText` handles TimeSpan; `[GeneratedRegex] TimeText/NumberText`; `Priority` also matches `\bdg\b`.
  - `TableQuery.cs`: `Filter` has `Ranges` (multi-range `between`, midnight crossing via `InRange`), `ToTime`, `TimeOf`, `Equal` does prefix match for diagnosis codes ("I64 - description"), `list` output, `Percent`, `FilterValueText`; `Describe` lists date/time/number/text columns; prompt rules (start-column rule: "začátek" ≠ "zápis nálezu").
- Worktree release pipeline for next release is in memory `release-artifact-paths.md`; next code **136**, version **0.1.30**, changelog entry still to write in `Resources\Raw\changelog.json`.
- Local LM Studio at `localhost:1234` has `google/gemma-3-12b` loaded; harness `qt-live` passes 14/14 questions (was 7/9 before prompt/schema fixes).
- The user is on 0.1.28→0.1.29 on the phone; the Google Play Protect "harmful app" warning on S23+ was raised twice (2026-11-10) and **never fully resolved** (not a code bug found; see Open Questions).

### Thread B — Anesthesia argument (analysis state at end of session)
- Latest data: `56f8efaa-saly_seznam.xlsx` (868 rows, 2.1.–10.11.2026; operations that **start before 15:00 and end after 15:00**; no `Pacient` column any more; ophthalmology "OKO_x" and 6. pavilon rows removed vs. the old export) + `a5fc87a6-KCC_1.xlsx` (115 events, KCC findings written 21.1.–3.11.2026).
- **KCC started in April** (user, 2026-11-11): Jan–Mar have 14 events and zero CMP; April–Oct 10–16 events/month, CMP 6–12/month. All angio/deficit arguments must use **1.4.–3.11.2026 (~155 weekdays)**.
- Orthopaedics and gynaecology are excluded everywhere (user: "do toho neplet").
- Final-ish argument set (details in Evidence): (1) long voluntary extra-team procedures (maxillofacial + vascular >60 min after 15:00): 32 výkonů / 107 h after 15:00 since April (44 / 154 h whole 21.1.–3.11.); (2) acute angio at handover 14:30–16:30: 11 angio, 10 during a running operation, 4–6 cases coinciding with an uncovered room; (3) model-based "souběhy" 33–49 of ~155 weekdays (21–32 %) with volunteers, 41–57 (26–37 %) without; deterioration from September (34–48 % of days Sep–Oct, October 45–59 %).
- **Not supported by the data:** weekend NCH >2/day (max 1/day), angio after 20:30 coinciding with a running operation (0 of 12), two angio running at the same time (only 27.4. midday overlap).
- The Artifact page `https://claude.ai/artifact/AVMLKbEjZPvsE2Uu7f1TNu` ("Přesahy sálů po 15:30", private, Version 5) is **stale** (old numbers 47–71 days, old export incl. ophthalmology, Jan–Mar angio). Not yet republished.
- The director text (one page) was offered twice, user has not yet said "yes" to the final version; five bullet points (the "5 odrážek" summary) were delivered in chat.
- Harness `qt-kcc` (scratchpad) has modes for everything below; all numbers in this document were reproduced with it on 2026-11-11.

## What We Tried (Chronological)

### Earlier in this session (4.–10. 11., from user-message outline + git log; details not mined)
1. 4.11: S23+ with disabled tunnel, recovery works; Opicentrum board zoom; admin log back navigation; pending-messages behaviour → releases (user said "Vydej" 4.11 07:59, 11:55). Redakce redesign discussion (phone left as is, PC has more options).
2. 5.–6.11: code review for duplicates/dead code; plan; weekend/holiday handling in the widget and monthly roster (no "ps" on weekends, light colour "víkend"/"svátek" not dark); moderators/admins can edit/delete board messages → release.
3. 7.11: Opicentrum roster pinch-zoom still not working, dynamic column width (anest. ambulance column too wide), removed A+/− buttons, "refresh" next to roster/free with last-updated time and rate limit; Windows PC at work (eduroam) update failed with SSL error (cert CN=212.111.15.246 inspected) → TLS 1.2 fix in 0.1.21.
4. 9.–10.11: widget without "ps" on weekends/holidays; library menu spinner lag; midnight reset of room states to "volno"; **Domácí AI** added (admin only, S23+): load/unload model, model choice, prompts via relay proxy (0.1.22–0.1.25), attachments + auto-unload (0.1.24), skills (0.1.28), collapsible settings (0.1.28), 0.1.26 re-release because package was not a clean build (same code as 0.1.25).
5. 10.11: user: "LLM ai neni schopne poradne pracovat s xlsx" → 0.1.27 (readable Excel extraction) → skills question → 0.1.28 → user asks for a KCC skill (row = výkon, columns main diagnosis + counts) and sums of selected rows → exact table query engine.

### This stretch (11.11)
6. **Schema-forced JSON + number check.** Hypothesis: free-form JSON queries cause wrong aggregates. Change: `response_format` json_schema (strict, enums from the table's column names) with fallback to unconstrained on HTTP 400 (`s_schemaUnsupported`), `AnswerNumberCheck`. Result: harness 7/9 → 14/14; released as 0.1.29.
7. **User's real KCC file returned 0 of 115 rows** for "diagnosis I64/I633/I636 and start 14:30–17:00 or 21:00–7:00". Cause 1: cells were text → added `Typed`. Cause 2: midnight-crossing `between` → `Ranges`/`InRange`. Cause 3: diagnosis cells contain "I64 - description" → prefix match. Cause 4: model chose "Datum zápis nálezu (HH:MM)" instead of the start column → prompt rule. Independent ground truth computed in `Program.cs`: 21 of 115 (18.3 %) — user had counted 21 by hand and confirmed ("vychází cca 18 %").
8. **Anesthesia analysis v1** (old export `komplet_p_esahy_15.xlsx`, 860 rows, with `Pacient` column and OKO/6. pavilon rows). Modes `--variants/--overruns/--kcctimes/--combined/--report`. Produced "93–130 days / 46–64 %" → superseded after ortho/gyn exclusion and again after the new export.
9. **Staffing corrections from user** (each changed the model): service 1 = NCH + trauma, service 2 = uro + chir (incl. vascular), shifted shift 13–21 weekdays only, day team until 15:30 + 20–30 min tail, weekend: no day team and no shifted shift, service 2 helps on JIP, voluntary extra team on long maxillofacial/vascular, exclude ortho + gyn. Model v2 (`--offshift`, `--weekend`, `--focus`, `--long`, `--wkspec`, `--nch`, `--bottleneck`).
10. A wrong claim ("25 acute angio at night/weekend") was caught and corrected: only 12 had documentation timestamps after 21:00 (7 at 23h) — nález time is a documentation time.
11. **New export (`saly_seznam.xlsx`) arrived** 11.11 15:29. Diff vs old (`--diff`): 32 rows only in old (ophthalmology OKO_1/2/4 at other sites, 6. pavilon plastics, UCOCH), 40 only in new (31.10.–10.11. operations + 1 reclassification). Deficit days dropped from 47–71 to 42–61 (more defensible: only COS rooms).
12. **Reference period question** ("co kdyby sis vybral spravne referencni obdobi?"): answered that period choice is legitimate only if disclosed and shown next to the whole period; Sep–Oct vs Apr–Aug comparison; summer dip caveat.
13. **User: "KCC začalo od dubna"** → confirmed with `--kccm` (zero CMP before April); all angio numbers restricted to 1.4.–3.11. via patch in `Bottleneck.cs` (`from` = max(…, 2026-04-01)).
14. **"Jak jsi zjistil že byly odkryté sály?"** → honest answer: model output, not observation; export has no staffing data. Rule: A (NCH+trauma) one covered, rest uncovered; B (chir+uro) one covered, rest uncovered; C (maxilo, plastics, ORL, other) all uncovered; deficit when `max(0,a−1)+max(0,b−1)+c+angio >= 2`; shifted shift = one extra capacity (Report.cs line 58).
15. **Angio window widened to 14:30** (user: last operations start at 14:30 and cannot be left): 8 → 11 angio, 10 with a running operation. Changed the segment key from window midpoint to window start (`Seg(win(a).S)`).
16. **Angio after 20:30 vs running operations** (`--late`): 12 angio after 20:30 (11 CMP; 7 weekday, 5 weekend), 0 coincide with any operation; operations running after 20:30 occur on only 7 days, all single long cases — **but the export lacks all operations that start after 15:00, so this is a data gap, not a negative finding**.
17. **Multiple acute angio per day** (`--multi`): 73 days, 61 with 1, 11 with 2, 1 with 3 (11.5.); windows overlap only 27.4. (11:32–12:32 & 12:18–13:18).

## Key Decisions

- **Model computes, LLM only writes the query** (JSON → program executes) — rejected: letting the LLM calculate in text (wrong sums, counted rows instead of summing Body).
- **Strict json_schema with fallback on HTTP 400** (older LM Studio builds) — rejected: hard-failing when the server rejects `response_format`.
- **`Typed` conversion in the extractor, not in the query engine** — so every consumer (text for the model, table engine, Describe) sees real dates/times/numbers.
- **Analysis period for angio = 1.4.–3.11.2026**; volunteer-procedure statistics are independent of KCC and may use the whole export (but both are reported).
- **Always show ranges for tail 20/30 min** and with/without volunteer teams, never a single "precise" number.
- **Model is an estimate and must be labelled so** (user-facing wording "souběh vyšší než kapacita směn", not "sál zůstal bez anesteziologa") — rejected: presenting "uncovered rooms" as an observed fact.
- **Do not claim**: weekend NCH >2/day, a 24/7 need, two simultaneous angio, late-evening angio+operation overlap. Strongest framing instead: volunteer work, handover angio 14:30–16:30, deterioration since September, workload concentrated 15:30–17:00 (74 %) → a shorter second shifted shift (~15–19 h) is more defensible than a full 24 h service.
- **Release only when the user says "Vydej"**; batch changes (memory `feedback-batch-builds`).
- **Exclude orthopaedics and gynaecology** everywhere (user instruction).

## Evidence & Data

### A. KCC events per month (nález date, `--kccm`)
| Month | Events | CMP (I63/I64) |
|---|---:|---:|
| 01 | 2 | 0 |
| 02 | 8 | 0 |
| 03 | 4 | 0 |
| 04 | 14 | 7 |
| 05 | 16 | 9 |
| 06 | 10 | 6 |
| 07 | 13 | 11 |
| 08 | 16 | 8 |
| 09 | 13 | 7 |
| 10 | 16 | 12 |
| 11 (to 3.11.) | 3 | 3 |

Acute angio definition: I63/I64 **or** request created the same day as the finding. From 1.4.: **86 acute angio, 63 CMP, in 73 days** (61 days with 1, 11 with 2, 1 with 3). 69 of 86 have planned duration exactly 30 min. Angio window = 60 min before "Datum zápis nálezu".

### B. Acute angio by start segment, from 1.4. (window start; handover 14:30–16:30)
| Segment | Angio | KCC hours | CMP |
|---|---:|---:|---:|
| A weekday 7–14:30 | 36 | 17.2 | 21 |
| **B1 handover 14:30–16:30** | **11** | 5.3 | 10 |
| B2 shifted shift 16:30–21 | 12 | 6.0 | 9 |
| C weekday 21–7 | 12 | 5.2 | 11 |
| D weekend | 15 | 8.7 | 12 |

- 10 of 11 B1 angio ran during an operation (3–6 rooms). Before the 14:30 change (15:00–16:30): 8 of 8.
- Angio + uncovered room at the same time 15:30–21 (nobody free): 4 (20 min tail, with volunteers) / 6 (30 min, without): **2.4. 15:00–16:00 (COS_2/B, COS_3/B), 15.4. 15:47–16:47 (COS_1/B, COS_7/A, COS_6/A), 21.4. 15:03–16:03 (COS_6/A, COS_3/B, COS_7/A), 13.10. 15:30–16:30 (COS_5/B, COS_2/B, COS_6/A)**; without volunteers also 14.5. 16:25–17:25 (COS_5/B, COS_4/C), 25.8. 15:26–16:26 (COS_7/A, COS_3/B, COS_1/B).
- After 21:00/night/weekend: angio during a running operation 1 (26.9. 11:40–12:40 COS_7), during ≥2 operations 0.

### C. Angio after 20:30 (from 1.4.; 12 events, 0 with a running operation)
12.6. 20:39–21:39 CMP; 23.6. 22:13–23:13; 12.7. (Sun) 22:04–23:04; 16.7. 22:29–23:29; 19.7. (Sun) 21:11–22:11; 14.8. 22:10–23:10; 7.9. 21:11–22:11 (non-CMP, I744); 20.9. (Sun) 22:51–23:51; 4.10. (Sun) 20:45–21:45; 17.10. (Sat) 22:49–23:49 (non-CMP, C19); 22.10. 22:59–23:59; 2.11. 21:50–22:50. Operations running after 20:30 (from 1.4.): 21.4. COS_1 to 23:15 (hybrid PTA/bypass), 18.5. COS_4 to 23:23, 20.7. COS_4 to 22:56, 24.8. COS_2 to 20:37, 21.9. COS_7 to 21:02, 12.10. COS_4 to 23:29, 3.11. COS_1 to 22:15 — no same-day angio overlap.

### D. Days with ≥2 acute angio (window = hour before nález)
21.4. 11:27–12:27 non-CMP & 15:03–16:03 CMP; **27.4. 11:32–12:32 & 12:18–13:18 (overlap)**; 11.5. 11:06, 12:25, 14:48 (3× CMP); 10.6. 11:44 & 13:29; 20.7. 09:35 & 13:04; 29.7. 13:23 & 17:15; 3.8. 16:13 & 17:53; 25.8. 09:41 & 15:26; 27.8. 14:23 & 19:31; 4.10. 20:45 & 23:38; 13.10. 15:30 & 19:29; 22.10. 16:59 & 22:59.

### E. Volunteer (extra anesthesia team) procedures — maxillofacial + vascular, >60 min after 15:00
Vascular = specialty "akutní LP chirurgie - typ H" and regex on name (bypass, endarterekt, tepn, aneurysm, aort, femor, popliteo, karotid, PTA, stent, embolektom, trombektom, cévn, amputac, revaskul, EVAR, iliac). Window 21.1.–3.11. (`--bottleneck` with `from` = 1.4. gives Apr–Nov):
| Month | Ops | Maxilo/Vasc | h after 15:00 | h after 15:30 | end >18 | end >20 | >8 h total |
|---|---:|---|---:|---:|---:|---:|---:|
| Feb | 8 | 4/4 | 29.6 | 25.6 | 3 | 2 | 4 |
| Mar | 4 | 2/2 | 17.9 | 15.9 | 3 | 2 | 3 |
| Apr | 3 | 1/2 | 15.2 | 13.7 | 2 | 1 | 3 |
| May | 4 | 4/0 | 11.9 | 9.9 | 1 | 1 | 3 |
| Jun | 3 | 1/2 | 5.1 | 3.6 | 0 | 0 | 2 |
| Jul | 4 | 4/0 | 14.4 | 12.4 | 3 | 0 | 4 |
| Aug | 6 | 2/4 | 16.8 | 13.8 | 2 | 1 | 4 |
| Sep | 6 | 5/1 | 19.0 | 16.0 | 3 | 1 | 4 |
| Oct | 5 | 3/2 | 17.4 | 14.9 | 2 | 1 | 4 |
| Nov | 1 | 0/1 | 7.3 | 6.8 | 1 | 1 | 1 |
| **Total** | **44** | **26/18** | **154.5** | **132.5** | **20** | **10** | **32** |
| From 1.4. | 32 | 20/12 | 107.1 | 91.1 | 14 | 6 | 25 |

By time of day (whole period): 15:30–18:00 84 h, 18:00–21:00 40 h, after 21:00 9 h. Mean length after 15:00 3.5 h, median 2.7 h; ~4.9 ops and 17 h per month (Feb–Oct), 4.4 ops / 14 h per month (Apr–Oct). Longest: 12.10. 7:34–23:29 maxilo (15.9 h), 18.5. 8:34–23:23 mastectomy/reconstruction (chir), 3.11. 7:45–22:15 aortofemoral bypass (14.5 h), 20.7. 8:38–22:56 plastic double-barrel fibula flap, 16.2. 9:10–22:57 maxilo, 9.11. 7:38–20:27 maxilo, 8.2. (Sunday) 14:15–22:02 vascular, 21.4. 12:41–23:15 vascular. Operations >8 h in the whole export: 48 (508 h, 195 h after 15:00); >10 h: 26; >12 h: 9; by specialty >8 h: chirurgie 22, maxilo 21, NCH 3, plastics 2.

### F. Deficit model ("souběhy"), weekdays, 21.1.–3.11. (205 weekdays); months show days (hours)
| Model | Days | % | Hours | Days ≥2 | ≥3 | ≥4 |
|---|---:|---:|---:|---:|---:|---:|
| tail 20, with volunteers, with angio | 42 | 20.5 | 12.4 | 10 | 0 | 0 |
| tail 30, with volunteers, with angio | 61 | 29.8 | 20.8 | 14 | 0 | 0 |
| tail 20, **without** volunteers | 55 | 26.8 | 19.5 | 17 | 4 | 1 |
| tail 30, **without** volunteers | 73 | 35.6 | 30.0 | 21 | 4 | 1 |
| tail 20, with vol., **no angio** | 38 | 18.5 | 10.2 | 8 | – | – |
| tail 30, with vol., **no angio** | 57 | 27.8 | 18.4 | 12 | – | – |

By month (with volunteers / without volunteers, tail 20 – tail 30; 11 = 1–3.11.):
| Month | With vol. days (h) | Without vol. days (h) |
|---|---|---|
| Apr | 3–6 (1.0–1.9) | 3–7 (1.0–2.0) |
| May | 8–10 (1.5–3.0) | 11–12 (3.2–5.0) |
| Jun | 3 (1.2–1.7) | 4 (1.2–1.9) |
| Jul | 1–3 (0–0.5) | 2–3 (0.2–0.7) |
| Aug | 2–4 (0.8–1.4) | 3–6 (1.8–2.6) |
| Sep | 5–8 (1.5–2.4) | 5–9 (1.6–2.8) |
| Oct | 10–13 (3.8–5.5) | 11–14 (5.1–7.0) |
| Nov | 1–2 (0.2–0.4) | 2 (0.2–0.4) |

From April (~155 weekdays): with volunteers 33–49 days (21–32 %, 10–17 h); without 41–57 (26–37 %, 14–22 h). Apr–Aug ~109 weekdays: 17–26 days (16–24 %); Sep–Oct ~44 weekdays: 15–21 days (34–48 %), 5–8 h; October alone 10–13 of 22 (45–59 %). Deficits occur before 16:30 with volunteers, up to 17:30 without. Whole-period tail-20/30 angio impact: +4 days, +2.2–2.4 h. Earlier wrong statement "Jan–Sep 1–8 days per month": true only for tail 20 (May 10 for tail 30).

### G. Workload after 15:30 (services 1+2, `--nch`; tail 20 / 30)
| Service | Weekday after 15:30 h | Weekend h | Weekend share | Ops after 15:30 weekday / weekend |
|---|---:|---:|---:|---|
| 1 NCH | 159.4 / 184.8 | 54.3 / 57.0 | 25 / 24 % | 99 / 16 |
| 1 trauma | 34.9 / 49.0 | 21.1 / 22.4 | 38 / 31 % | 37 / 8 |
| 2 chir | 204.4 / 234.9 | 48.9 / 52.9 | 19 / 18 % | 113 / 24 |
| 2 uro | 36.9 / 52.8 | 3.0 / 3.5 | 8 / 6 % | 41 / 3 |
| other | 125.7 / 139.1 | 6.2 / 6.7 | 5 % | 49 / 3 |
| all | 561.4 / 660.7 | 133.5 / 142.5 | 19 / 18 % | |

Services 1+2 only: 563 / 657 h total. Hours per clock hour after 15:30 (services 1+2, tail 25): 15h 189, 16h 164, 17h 65, 18h 31, 19h 14, 20h 6, 21h 3, 22h 3, 23h 1 → 74 % of the 476 h between 15:30 and 17:00. Weekend profile 10–23h: 1,1,3,11,34,42,18,9,6,2,1,1,0,0.

### H. Weekend neurosurgery (export contains only ops that end after 15:00 → lower bound)
16 weekend NCH ops in 16 days (11 Sat, 5 Sun), max 1 per day, 49 h (29.6 h after 15:00); NCH total 173 (9.2 % weekend). Weekday NCH ending after 15:30 per month 9,11,15,11,6,8,4,6,9,16,4 and weekend 1,1,1,0,0,3,1,0,3,6,0 (Jan–Nov) → **October is the highest year-to-date**. Weekend days with two operations: 11.1., 7.3., 6.9., 19.9., 26.9. (9.4 h); also 27.9., 24.10. in the overlap model ("two things at once" in 6 of 80 weekend days, 77–220 min). Weekend ops by service: NCH 16 (49 h), trauma 8 (18 h), chir 24 (41 h), uro 3 (2 h), other 3 (5 h).

### I. Export versions
| File | Rows | Period | Columns | Notes |
|---|---:|---|---|---|
| `8b875876-komplet_p_esahy_15.xlsx` | 860 | 2.1.–30.10. | has `Pacient`, no `Hlavní Dg` | includes 25+ ophthalmology (OKO_1/2/4) and 6. pavilon plastics |
| `56f8efaa-saly_seznam.xlsx` | 868 | 2.1.–10.11. | no `Pacient`, has `Hlavní Dg`, 9 columns | only COS rooms + a few JPL/Sectio/6. pavilon; 14 rooms |
| `a5fc87a6-KCC_1.xlsx` | 115 | findings 21.1.–3.11. | Datum založení (+HH:MM), Datum události, Datum zápis nálezu (+HH:MM), Datum uzavření, Žádající útvar, Popisující lékař, Uzavřel, Délka trvání (min), Hlavní Dg | 69/86 acute angio have 30 min |

New export specialties (868): chirurgie 219, NCH 173, ortho 121 (excluded), uro 113, trauma 102, maxilo 52, gyn 45 (excluded), plastics 28, ORL 11, day-care ortho/gyn 4. Total operation minutes 135 837, mean 156.5 min, max 955.

### J. Home AI test results
Live harness (`qt-live`, 14 questions): before schema+prompt fixes 7/9 (counted rows instead of summing "Body"; "C50 nebo C18" with single startswith value) → 14/14 after. User's file: engine said 0 of 115 → correct 21 of 115 (18.3 %), hit times 14:30–17:00 or ≥21:00/≤07:00 with I64/I633/I636.

### K. Release history in this session (git log)
| Version | Code | Commit | What |
|---|---:|---|---|
| 0.1.18 | 124 | 0a61da3 | Redakce redesign, cleanup |
| 0.1.19 | 125 | a6939b6 | board edit/delete Admin/Modifier, calendar readability |
| 0.1.20 | 126 | 125d473 | Opicentrum board pinch zoom, dynamic name column, refresh button |
| 0.1.21 | 127 | ba42f61 | Windows relay connections TLS 1.2 |
| 0.1.22 | 128 | 193d253 | widget no free on weekends, fast library switching, pinch zoom Android, midnight room reset, Domácí AI |
| 0.1.23 | 129 | 9835fd3 | Domácí AI visible on S23+, Admin only |
| 0.1.24 | 130 | 2e73101 | widget after update, attachments, auto-unload |
| 0.1.25 | 131 | ff58f23 | Domácí AI via relay proxy |
| 0.1.26 | 132 | 6ae4dda | clean-build package, same code as 0.1.25 |
| 0.1.27 | 133 | bd4dc18 | Excel read more clearly |
| 0.1.28 | 134 | e6fdda1 | skills, collapsed settings |
| 0.1.29 | 135 | c1e95ba | exact Excel computation, KCC skill, number check |

### L. Operations longer than 8 h in the new export (48 total; `--long`; Δ = after 15:00)
By threshold: >4 h 146 ops (1042 h, 373 h after 15:00); >6 h 77 (712 h, 251 h); >8 h 48 (508 h, 195 h); >10 h 26 (310 h, 142 h); >12 h 9 (123 h, 61 h). Of the 48: end after 18:00 = 28, after 20:00 = 13, after 22:00 = 6, start before 8:00 = 14, weekend = 0. Per month (Jan–Nov): 1, 7, 5, 5, 6, 2, 5, 4, 6, 5, 2.
| Date | Time | Length | Δ15:00 | Specialty | Room | Procedure |
|---|---|---:|---:|---|---|---|
| Mon 12.10. | 07:34–23:29 | 15.9 h | 8.5 h | maxilo | COS_4 | adenoid cystic carcinoma, total parotidectomy |
| Mon 18.5. | 08:34–23:23 | 14.8 h | 8.4 h | chirurgie | COS_4 | bilateral skin-sparing prophylactic mastectomy + reconstruction |
| Tue 3.11. | 07:45–22:15 | 14.5 h | 7.3 h | chirurgie | COS_1 | aortofemoral bypass |
| Mon 20.7. | 08:38–22:56 | 14.3 h | 7.9 h | plastics | COS_4 | double-barrel free fibula flap revision |
| Mon 16.2. | 09:10–22:57 | 13.8 h | 8.0 h | maxilo | COS_4 | angiosarcoma of scalp resection + reconstruction |
| Mon 9.11. | 07:38–20:27 | 12.8 h | 5.5 h | maxilo | COS_4 | tracheostomy, cheek carcinoma resection, neck dissection |
| Thu 17.9. | 07:26–19:58 | 12.5 h | 5.0 h | maxilo | COS_4 | floor of mouth carcinoma + bilateral neck dissection |
| Mon 21.9. | 07:35–20:04 | 12.5 h | 5.1 h | maxilo | COS_4 | segmental mandibulectomy + neck dissection |
| Mon 24.8. | 08:26–20:37 | 12.2 h | 5.6 h | chirurgie | COS_2 | femoro-crural bypass (ATA) |
| Tue 24.3. | 08:40–20:35 | 11.9 h | 5.6 h | chirurgie | COS_1 | femoro-pedal bypass |
| Mon 30.3. | 08:51–20:38 | 11.8 h | 5.6 h | maxilo | COS_4 | lower lip/chin resection + neck dissection |
| Thu 7.5. | 08:30–20:08 | 11.6 h | 5.1 h | maxilo | COS_4 | tongue tumour, bilateral neck dissection, ALT flap |
| Mon 5.10. | 07:41–19:17 | 11.6 h | 4.3 h | plastics | COS_4 | chimeric reconstruction of finger III (toe transfer) |
| Tue 21.4. | 12:41–23:15 | 10.6 h | 8.3 h | chirurgie | COS_1 | hybrid PTA + bypass + endarterectomy (vascular) |
| Mon 21.9. | 10:27–21:02 | 10.6 h | 6.0 h | NCH | COS_7 | decompression + navigated transpedicular stabilisation L5–S1 |
| Fri 23.10. | 07:53–17:00 | 9.1 h | 2.0 h | maxilo | COS_7 | midface reconstruction |
Remaining 32 ops are 8–11.6 h, mostly maxilo (COS_4 head-and-neck with free flaps), chirurgie vascular bypasses (COS_1/COS_2) and hand-surgery free-flap reconstructions "FFMT gracilis" (classed chirurgie, **not** flagged vascular by the regex → may deserve a separate "mikrochirurgie" class, see Open Questions). 13.2. appears twice (same NCH+maxilo meningioma case entered under both specialties).

### M. Weekend neurosurgery operations in the export (all single NCH op per day; `--nch`)
| Date | Room | Time | Length | Procedure |
|---|---|---|---:|---|
| Sun 11.1. | COS_7 | 14:59–18:15 | 3.3 h | CHSDH recurrence evacuation, craniotomy widening |
| Sat 7.2. | COS_7 | 14:24–15:03 | 0.7 h | CHSDH re-evacuation |
| Sat 21.3. | COS_7 | 13:40–15:15 | 1.6 h | intracerebral haematoma re-evacuation |
| Sat 13.6. | COS_7 | 13:23–15:10 | 1.8 h | spine stabilisation revision Th12–L3 |
| Sat 27.6. | COS_7 | 14:46–15:09 | 0.4 h | subdural evacuation + drainage |
| Sun 28.6. | COS_7 | 14:55–15:39 | 0.7 h | CHSDH evacuation |
| Sat 18.7. | COS_7 | 14:55–17:02 | 2.1 h | EDH evacuation |
| Sat 5.9. | COS_7 | 14:40–17:04 | 2.4 h | transpedicular fixation L4–S1 |
| Sat 26.9. | COS_7 | 09:43–16:48 | 7.1 h | transpedicular fixation Th3–Th7 (navigated) |
| Sun 27.9. | COS_7 | 12:30–18:55 | 6.4 h | fixation C4–Th2 + decompression |
| Sun 4.10. | COS_7 | 14:18–16:06 | 1.8 h | revision, evacuation, drainage |
| Sat 10.10. | COS_7 | 14:16–17:29 | 3.2 h | decompressive craniectomy |
| Sat 17.10. | COS_7 | 13:03–18:14 | 5.2 h | decompression Th10 + fixation |
| Sat 24.10. | COS_7 | 13:05–15:22 | 2.3 h | transpedicular fixation L1–L3 |
| Sun 25.10. | COS_7 | 14:00–18:02 | 4.0 h | percutaneous fixation Th6–Th9 |
| Sat 31.10. | COS_7 | 14:01–20:04 | 6.1 h | fixation C1–C4 + PLF |
Weekend NCH total 49 h (29.6 h after 15:00); 6 of 16 are in October. Weekend days with ≥2 operations of any service: 11.1. (NCH + chir, 5.4 h), 7.3. (2 trauma, 5.4 h), 6.9. (trauma + other, 3.9 h), 19.9. (chir + other, 4.3 h), 26.9. (NCH + chir, 9.4 h).

### N. Wording already delivered to the user (reuse, do not contradict)
Five-bullet summary (Apr–Oct): (1) deterioration from September: Apr–Aug 16–24 % of weekdays, Sep–Oct 34–48 %, October 45–59 %; (2) 33–49 days of ~155 (21–32 %), 10–17 h total, deficits before 16:30 with volunteers and up to 17:30 without; (3) volunteer teams cover 107 h after 15:00 (32 ops, ~14 h/month; 14 end after 18:00, 6 after 20:00; longest 15.9 h on 12.10.); (4) without volunteers 41–57 days, days with two uncovered rooms rise from 10–14 to 17–21, days with 3–4 uncovered appear only without volunteers (4 days, one with 4); (5) handover angio: 8 angio at 15:00–16:30 all during running operations (later 11 at 14:30–16:30, 10 during operations), 4–6 cases with an uncovered room (2.4., 15.4., 21.4., 13.10.; +14.5., 25.8. upper bound); 86 acute angio since April, 63 CMP. Recommended qualification: "model estimate, export lacks operations starting after 15:00; tail 20–30 min is an estimate". Suggested director sentence: "Od spuštění KCC v dubnu jsme na přelomu služeb 8krát řešili akutní angio při běžících operacích a v 4 až 6 případech tím zůstal sál bez anesteziologa. Po dobu 7 měsíců pokrývají dlouhé výkony dobrovolné týmy (107 h po 15:00). V září a říjnu chyběl volný anesteziolog zhruba v každém druhém pracovním dni." — **must be softened** to "souběh vyšší než kapacita" after the user's "Jak jsi zjistil…" challenge.
Honest weak points communicated: only ~12–30 h of deficit in 10 months → full 24 h third service not supported by hours alone; 74 % of load lies 15:30–17:00 → better argument for a second shifted shift ~15–19 h plus formalised volunteer teams.

### O. How to reproduce each number (scratchpad `qt-kcc`, args: KCC first, operations second)
| Claim | Command / mode |
|---|---|
| Volunteer table, angio segments, handover angio, uncovered-room cases (from 1.4.) | `--bottleneck KCC saly` |
| Deficit days/hours per month, slots, with/without volunteers, tail 20/30 | `--report KCC saly` (JSON; weekdays 205 in whole window) |
| Weekend NCH list, service hours, hour profile | `--nch saly` |
| Operations >4/6/8/10/12 h and the >8 h list | `--long saly` |
| Angio after 20:30 vs running operations | `--late KCC saly` |
| Days with ≥2 acute angio | `--multi KCC` |
| KCC events per month, CMP counts | `--kccm KCC` |
| Old vs new export diff | `--diff old.xlsx new.xlsx` |
| Describe/ground truth of the original 21/115 question | `dotnet … KCC.xlsx` (no mode; prints DESCRIBE and "SPRÁVNĚ (nezávisle)") |
Note: `--bottleneck` patched to start 1.4.; to get the 21.1. variant change `new DateTime(2026, 4, 1).Ticks` in `Bottleneck.cs` back to remove the clamp. `Report.cs` still runs over the whole KCC window (21.1.–3.11.); April-only monthly figures in this document were summed from its per-month output.

### P. Staffing assumptions confirmed by the user (model inputs)
| Item | Value | Source |
|---|---|---|
| Service 1 | NCH + trauma | user 11.11 |
| Service 2 | uro + chir (vascular counts as chir) | user 11.11 |
| Shifted shift | 13–21, weekdays only, covers one thing | user 11.11 |
| Day team | until 15:30; anesthesia tail after operation end 20–30 min | user 11.11 |
| Weekend | no day team, no shifted shift; service 2 also helps on JIP | user 11.11 |
| Voluntary extra team | long maxillofacial + vascular procedures | user 11.11 |
| Excluded | orthopaedics, gynaecology | user 11.11 |
| Angio window | 14:30 (last operations start; cannot leave patient) | user 11.11, late |
| KCC start | April 2026 | user 11.11, late |
Not yet confirmed: number of anesthesiologists after 15:30; who covers group C; whether anesthesia day team stays for operations already running at 15:30.

## Code Analysis

- `HomeAi\TableQuery.cs`: `TableQueryRunner.PrepareAsync(endpoint, model, question, tables, note, ct)` → builds prompt (`FormatHelp` with between multi-range/list/start-column rules and examples), requests JSON with `ResponseFormat(tables)`; `s_schemaUnsupported` static flag after HTTP 400; `TableQueryEngine` executes filters (`Filter` record with `Ranges()`, `InRange()` midnight crossing, `Equal` prefix match, `Matches` uses `DocumentTextExtractor.Typed`), group_by, aggregations count/sum/avg/min/max/count_distinct, `list`, `Percent`.
- `HomeAi\DocumentTextExtractor.cs`: `ExtractAsync(FileResult)` → `Tables` (name, `Columns` array, `Rows` with `Values`), `Describe` gets from `TableQueryRunner.Describe(doc.Tables)`; `CellText(object?)`, `Typed(object?)`, `Priority` (columns picked for the text summary).
- `HomeAi\HomeAiClient.cs`: `StreamChatAsync`/`CompleteAsync` accept `responseFormat`; `HomeAiException(message, statusCode)`; `EnsureSuccessAsync` forwards status.
- `HomeAi\AnswerNumberCheck.cs`: `Check(answer, sources)` returns a grey note.
- `ViewModels\HomeAiViewModel.cs`: `RunPlainAsync(..., double temperature, ...)`, table-query flow, number-check note after the answer.
- Staffing model constants (scratchpad `Bottleneck.cs`/`Report.cs`): `Op.G` = A if spec contains "neurochir" or "úrazové", B if "urologie" or specialty == "akutní LP chirurgie - typ H", else C; `Op.Volunteer` = (maxilo or vascular regex) and `E − 15·60 > 60`; deficit minute: `Math.Max(0,a−1)+Math.Max(0,b−1)+c+ang >= 2`; `slots[11]` = half-hour slots from 15:30 to 20:30; tail minutes 0/20/30.

## Files Changed

### Source code (repo `H:\Visual Studio\C#\Aplikace`)
- `src/SecureApp.Presentation/HomeAi/DocumentTextExtractor.cs` — uncommitted (+43): partial class, `Typed`, time/number regexes, TimeSpan in `CellText`, `Priority` matches `\bdg\b`.
- `src/SecureApp.Presentation/HomeAi/TableQuery.cs` — uncommitted (+145/−…): multi-range, time handling, prefix match, `list`, percent, Describe types, prompt rules.
- Committed earlier: `HomeAiClient.cs`, `AnswerNumberCheck.cs` (new), `HomeAiViewModel.cs`, `SecureApp.Presentation.csproj` (0.1.29/135), `Resources/Raw/changelog.json`.
- Untracked leftovers in repo root: `SecureApp-win-x64-0.1.7…0.1.17.zip` (old release zips, not committed).

### Data & results (scratchpad `C:\Users\dvora\AppData\Local\Temp\claude\22b5d484d26ebaa4\4d855aea-f698-44c0-b3ec-8a4f187ba1af\scratchpad\`)
- `qt-kcc\` — analysis harness (.NET console, references the Presentation sources): `Program.cs` modes `--variants --overruns --kcctimes --voluntary --report --combined --offshift --weekend --focus --long --wkspec --nch --bottleneck --diff --kccm --late --multi`; files `Bottleneck.cs, Nch.cs, Long.cs, Report.cs, Late.cs, Multi.cs, KccMonths.cs, Diff.cs, Variants.cs, Overruns.cs, KccTimes.cs, Combined.cs, OffShift.cs, Weekend.cs, Focus.cs, Voluntary.cs, Stubs.cs, MauiStub.cs`. Run: `dotnet build -c Release` then `dotnet qt-kcc\bin\Release\net10.0\qt-kcc.dll --bottleneck <KCC.xlsx> <saly.xlsx>` (analysis modes take KCC first, then operations; `--nch/--long/--diff` take only operations; `--multi/--kccm` only KCC).
- `qt-live\` — LM Studio harness (14 questions).
- `bn-new.txt, nch-new.txt, long-new.txt, rep-new.txt` — outputs on the new export; `report-data.json`, `report-data2.json` — older Report JSON; `user-msgs.txt` — outline of user messages; `presahy-salu.html` — Artifact source (stale); `SecureApp-win-x64-0.1.29.zip`, `msg29.txt`.
- Uploads: `C:\Users\dvora\.claude\uploads\4d855aea-f698-44c0-b3ec-8a4f187ba1af\` — `a5fc87a6-KCC_1.xlsx`, `8b875876-komplet_p_esahy_15.xlsx`, `56f8efaa-saly_seznam.xlsx`, two images.

### Config
- None changed this stretch apart from csproj/changelog for 0.1.29.

## User Feedback & Preferences (REQUIRED — never omit)

- "Vydej" = release; the user releases only on that word; "Nasad" = deploy. "Uspi pc" = put the PC to sleep (done at end of day).
- Writes Czech without diacritics, terse; wants **bullet points** ("V bodech...", "do 5 odrážek") and concrete, defensible arguments, not long essays.
- "Nevim jak to mam uchopit zkus mi pomoci..." — felt lost among numbers: needs a clear structure (hard facts vs estimates vs verify-with-colleague).
- Corrections that changed the analysis: ortho + gyn out ("neplet"); shifted shift 13–21 weekdays only; weekend has no shifted shift and service 2 helps JIP; vascular = chirurgie; day team to 15:30 + 20–30 min tail; voluntary teams on long maxilo/vascular; focus on voluntary rooms, acute angio, plastics; **"KCC začalo od dubna"**; angio window starts 14:30 because last operations start then and cannot be left.
- Asked "Jak jsi zjistil že byly odkryté sály?" — wants to know what is observed vs modelled; be honest about assumptions.
- Reference-period challenge ("co kdyby sis vybral správné referenční období?") — be transparent, do not cherry-pick; show whole + recent period.
- Hypothesis he wanted tested: weekend NCH ops >2/day because weekday work spills over to weekends where "mladší služba nemá tolik zastání" — data did **not** support it (max 1/day, export is lower bound).
- Previously (memory): batch builds, don't build/release after each fix; answer the question first before building; never debug-deploy to real devices; use only signed Release builds.
- Google "škodlivá aplikace" warning: "toto určitě najdi proč a vyřeš" — wants root cause, not a workaround; also said the message did not appear before the last update.
- Wants Domácí AI usable: attach file, load/unload model, hide settings under a button, bigger text window, skills.

## Where We're Going

1. **Decide on the director deliverable**: rewrite the Artifact page (same `file_path` `presahy-salu.html` → same URL) with 1.4.–3.11. numbers, monthly table (with/without volunteers), 14:30 angio, honest "model estimate" labelling; plus a one-page text (3 arguments + weaknesses). User has not yet said yes to the final version.
2. **Ask the user for the missing data** (list in Open Questions) and confirm the single fact "how many anesthesiologists are available for rooms 14:30–15:30 and after 15:30 on a weekday?" — then recompute deficits as "simultaneous rooms vs staff" (assumption-free metric: days with ≥4/5/6 rooms running at 16:00/17:00).
3. **0.1.30 release only on "Vydej"**: csproj 0.1.30/136 + `changelog.json`, commit (end with the Co-Authored-By/Claude-Session trailers from the system reminder), fresh worktree `C:\wt\r30`, copy `keystore\`, clean `dotnet publish -f net10.0-android -c Release` (expect 1,726 APK entries, signer SHA-256 `6e75392845b5a2916b906cae647b5e053867a42fb6ff4f494329ebf20341b27c`), Windows publish without `-r` (213 files), zip with powershell.exe Compress-Archive, S9+ `adb -s 22dab4387e0b7ece install -r` + verdict ALLOW, push `pi` and `github`, upload to `http://192.168.50.8:8080` with admin secret read via ssh, verify versions/SHA-256/manifest, update memory `release-artifact-paths.md` (next code 137).
4. Optional: Google Play Protect warning follow-up; clean worktrees `C:\wt\r*`; delete old untracked `SecureApp-win-x64-*.zip` in repo root (ask first).

## Risks & Blockers

- Export limitations: operations starting after 15:00 and weekend operations ending before 15:00 are absent → night/weekend/late-evening overlaps are lower bounds or invisible.
- All "uncovered room"/"deficit" numbers are model output (assumptions A/B/C groups, one shifted-shift capacity, tail 20–30 min, vascular keyword classification); angio start = nález − 60 min (nález is a documentation time; duration planned, 69/86 exactly 30 min).
- PowerShell 5.1 gotcha hit this session: `Get-Content -Raw` + `Set-Content -Encoding utf8` on `.cs` files double-encodes Czech characters (mojibake → `IndexOutOfRangeException` on column lookups). Repaired with `[IO.File]::ReadAllText(UTF8)` → `Encoding.GetEncoding(1250).GetBytes` → `UTF8.GetString` → `WriteAllText` with BOM. Use Edit/Write tools or byte-level APIs for files with diacritics.
- `git` is not on PATH: use `C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`; `bd` not installed.
- Low-memory reaper: run release publishes in the foreground, one at a time (memory).

## Open Questions

- How many anesthesiologists are available for operating rooms 14:30–15:30 and after 15:30 on a weekday (services 1, 2 + shifted shift = 3?) and who really covers group C (plastics, maxilo, ORL) after 15:30?
- Do free-flap microsurgical reconstructions under chirurgie count as volunteer-covered?
- Real outcome of the 4–6 coincidence days (2.4., 15.4., 21.4., 13.10., 14.5., 25.8.): did a room actually wait? Ask a colleague to confirm.
- Data to request: all weekend operations (any time, with acute/planned flag and original planned date); acute operations starting after 15:00 (evening/night); anesthesia times; records of voluntary teams (who, how many hours); door-to-groin times for CMP; same period last year (Sep–Oct 2025) for a year-on-year comparison.
- Google Play Protect "harmful app" warning on S23+ — cause not identified; user asked twice.

## Quick Start for Next Session

```bash
# Restore context
# (no beads) read memory index: C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md
# git (not on PATH): & 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe' status

# Reference docs
# plans/handoffs/HANDOFF_standalone-ade033e2_home-ai-excel-anesthesia-argument_2026-11-11.md (this file)

# Key files to read first
# src/SecureApp.Presentation/HomeAi/TableQuery.cs
# src/SecureApp.Presentation/HomeAi/DocumentTextExtractor.cs
# scratchpad/qt-kcc/Bottleneck.cs, Report.cs, Late.cs

# Evidence / data files
# C:\Users\dvora\.claude\uploads\4d855aea-f698-44c0-b3ec-8a4f187ba1af\{a5fc87a6-KCC_1.xlsx, 56f8efaa-saly_seznam.xlsx}
# scratchpad\bn-new.txt, nch-new.txt, long-new.txt, rep-new.txt

# Verify current state
dotnet build -c Release   # in scratchpad\qt-kcc
dotnet bin\Release\net10.0\qt-kcc.dll --bottleneck <KCC_1.xlsx> <saly_seznam.xlsx>   # expect 86 acute angio (from 1.4.), 11 at 14:30-16:30

# Next action
# Ask the user: (1) staffing count after 15:30 and 14:30-15:30, (2) whether to rewrite the Artifact page + one-page director text with the 1.4.–3.11. numbers; release 0.1.30 only after "Vydej".
```

## Session Closed
**Closed at:** 2026-11-11 23:05
**Commit:** e900ac8 (0.1.30 source changes + this handoff; NOT released)
**Session status:** Handed off to next session
