# Releases 0.1.0 (106) → 0.1.5 (111) shipped; relay-side WireGuard config fix written but NOT deployed; decision pending: drop the tunnel for users (public HTTPS relay)

**Date:** 2026-10-30 (the work ran 2026-10-24 → 2026-10-30; this handoff is written 2026-10-30 evening, local time = UTC+2)
**Status:** IN PROGRESS — all six releases are live on the relay; one relay change (`TuneWireGuardClientConfig`) is written, compiles, is NOT deployed and NOT pushed; the user is deciding between "fix the tunnel" and "get rid of the tunnel for users"
**Bead(s):** none (`bd` is not installed in this repo)
**Epic:** SecureApp alpha: Opicentrum screens, widget, staffing alerts, release hygiene, network access (WireGuard)
**Chain:** `standalone-24caf3f8` seq `4`
**Parent:** `HANDOFF_standalone-24caf3f8_chat-boards-size-review_2026-10-24.md`
**Prior chain:** `HANDOFF_standalone-24caf3f8_opicentrum-perf-code-review_2026-10-22.md` > `HANDOFF_standalone-24caf3f8_code-review-p7-p9_2026-10-22.md` > `HANDOFF_standalone-24caf3f8_chat-boards-size-review_2026-10-24.md` > this

---

## Since Last Handoff

- **Parent's #1 "Ask: release `c672352` as 0.1.0 (106)?" → answered yes and shipped** on 2026-10-24 (`d3e442d`). Then 0.1.1 (107, 10-25), 0.1.2 (108, 10-27), 0.1.3 (109, 10-28), 0.1.4 (110, 10-29), 0.1.5 (111, 10-30). Each release was authorised by a short message ("Ano" / "Release" / "...pak release" / "Releace"). The standing rule stays: **no push, deploy or release without the user's explicit OK.**
- **Parent's #2 "read the device logs after the release" → partly done.** The 0.1.4 audit measured request frequency (Opicentrum sync) and the Petr's-S25 log was read (WebSocket `ConnectFailure` flood). The transport metrics list of the parent (`ws.send.slow`, `msg.receive.slow`, …) was not re-checked as a whole.
- **Parent's "Faltus tunnel keepalive" (unresolved diagnostic) → ROOT CAUSE FOUND this session:** wg-easy generates every client config with `PersistentKeepalive = 0` AND a full tunnel (`AllowedIPs = 0.0.0.0/0, ::/0`, `DNS = 1.1.1.1`). Live check on the Pi: keepalive is off for all 8 peers (table in Evidence). Petr's S25 has 21 undelivered messages waiting on the relay because it cannot reach the relay (`WebSocketException: net_webstatus_ConnectFailure` every few minutes in its log). See "Where We're Going" #1–#3.
- **Parent's priority list "reliability next: relay-side at-least-once delivery"** was NOT started; the session went to user-visible Opicentrum/widget work, release hygiene and the network question.
- **Parent's open item "S9+ stuck on 1.69"** is no longer valid: the S9+ was last seen on 0.1.1 (107).
- **Trajectory shift:** from chat reliability to (a) Opicentrum screens (Pozice duty marks, staffing-gap alerts, closed-day parsing bug), (b) less load on the Opicentrum portal, (c) the question "does the WireGuard tunnel hurt users?", which ended in "should users need a tunnel at all?".
- **Position-suggestion plan (`POZICE_NAVRH_PLAN.md`)** phases 1–2 shipped (staffing profiles + 6-month history, 0.1.1); phases 3 (display of suggestions) and 4 (write week to Opicentrum + undo) are NOT started.

## Reference Documents

- `POZICE_NAVRH_PLAN.md` — the four-phase plan for position suggestions (phases 3–4 open).
- `relay/ops/README.md` — relay redeploy pipeline, wg-easy notes (**keepalive 0 problem and the corrupt `WG_DEFAULT_ADDRESS` line are described there**).
- `plans/handoffs/HANDOFF_standalone-be860f68_crash-fixes-wireguard-onboarding_2026-10-07.md` (lines ~321–323) — a sample client config (full tunnel, keepalive 0, endpoint `212.111.15.246:51820`).
- `plans/handoffs/HANDOFF_notifications-background-widget_2026-09-28.md` — Opicentrum page shape (`pracoviste.php`, the last row "Odpolední 13-21 h" = posunutá služba, `sluzby7.php`).
- Memory index `C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md` — HARD RULES; updated this session: `feedback-versioning-alpha.md` (next code **112**), `release-artifact-paths.md` (0.1.4 and 0.1.5 gotchas), index line.
- No `CLAUDE.md` in this repo.

## The Goal

SecureApp is a closed hospital-team app (.NET MAUI Android + Windows, E2EE chat, document library, Opicentrum-based schedule screens) backed by a self-hosted ASP.NET relay on a Raspberry Pi. The app is still alpha: releases are `0.1.x`, the first real release will be `1.0`. The standing goals for this stretch were: keep the Opicentrum screens (Rozpis, Pozice, Volno, widget calendar) correct and cheap on the portal, alert Admin/Moderator about unstaffed positions, and keep every device reachable. The user's latest line of thought is that **the WireGuard tunnel is "nepříjemnost navíc" for users** — so the end state under discussion is an app that reaches the relay over HTTPS/WSS without any tunnel for ordinary members, with WireGuard kept only for the admin. Until that is decided and built, new-member tunnel configs must at least be split-tunnel with a working keepalive.

## Where We Are

**Repository and live state**
- Branch `main`, HEAD `a9c3af1` = Release 0.1.5 (111), pushed to `pi` and `github`. Working tree has ONE uncommitted change: `relay/SecureApp.Relay/Program.cs` (the `TuneWireGuardClientConfig` change, see below). This handoff file is the other new file.
- Live on the relay (verified through `/download/{android,windows}/version`): versionCode **111**, versionName **0.1.5**, both platforms, uploaded 2026-10-30 18:38 UTC. SHA-256 round trip of the downloaded APK and zip matched the local files.
- 0.1.5 APK 40,134,222 B (arm64-only, signer SHA-256 `6e75392845b5a2916b906cae647b5e053867a42fb6ff4f494329ebf20341b27c`, `aapt2` badging: versionCode 111 / 0.1.5). Windows zip 67,154,652 B, 218 entries.
- Devices (last known): S23+ (Vilém) 0.1.3 at the time of the check; S9+ 0.1.1; home PC DESKTOP-F8AS2U6 0.1.1; PC v práci 1.99.0.102 (102) with 3 pending messages, last active 24.10.; Petr's S25 ("Zařízení S25 uživatele Petr", role 1, 0.1.0 (106)) with **21 pending messages**, last active 24.10. 20:18. Nobody has been verified on 0.1.5 yet — the in-app updater has to pick it up.

**0.1.5 (111), `a9c3af1` — closed-day parsing bug**
- Symptom (user screenshot of Pozice, column `st 28.10.`): every workplace row showed a red chip "– · Odpolední 13-21 h". 28.10. is a Czech holiday.
- Cause: on a holiday the portal writes `--` in every roster cell. `OpicentrumParsing.ExtractPeople` only filtered empty strings, so `--` became a person called "--" in every row; the 0.1.4 duty overlay (`DutyPositionOf` → `NamesMatch`) matched "--" with the "--" entry of the "Odpolední 13-21 h" row and painted it red. (The grid parser `ParsePracovisteGrid` already treated `--` as a closed day; the people parser did not.)
- Fix: `ExtractPeople` keeps only names containing a letter; `OpicentrumSyncService.AddDuty` returns when the name has no letter; `OpicentrumParsing.NamesMatch` returns false unless both sides contain a letter. After updating, Pozice needs "Obnovit" (the cached snapshot still holds the `--` chips).
- Changelog entry 0.1.5 added in `Resources/Raw/changelog.json`; csproj `0.1.5` / `111`.

**0.1.4 (110), `4f3ef6a`** (details in Files Changed)
- Widget calendar: even more muted fill palette; today keeps its own cell colour and gets only a muted red ring (`TodayRing 0xFFD05A5A`) and a muted red number (`TodayText 0xFFE87878`) when the cell is empty or belongs to the adjacent month; adjacent-month fills are dimmed to alpha `0x55`, today's adjacent-month fill only to `0x99` so it stays legible when the widget shows November.
- Pozice board: people on duty are drawn red with "Jméno · slot" from the already-synced `DutyRosterStore` (no extra download); `DutyRosterStore.GetMany` loads the store once.
- Opicentrum request load: `StaffingGapChecker` reuses week pages the sync already downloaded (`DoctorGridCache`, 25 min), nurses' weeks in one login (`FetchRosterGridsAsync`), check hourly and not at night (22–05); roster sync every 10 min for `today-7 … today+21`, the full range through the end of next month once an hour, night interval 60 min.

**0.1.3 (109), `1976d74`:** staffing-gap alerts (unstaffed afternoon shift 7 days ahead; operating room with only a doctor or only a nurse 2 days ahead), shared read-only nurses' portal login (AROS) entered per Admin/Modifier device, gap days ringed in the widget (`AlertRing 0xFFFF4081`).
**0.1.2 (108), `98e3535`:** widget month calendar, Redakce as a button menu (mobile: Admin only), PC registration at login + "Rozpracované dokumenty", one person = one e-mail (shared role/reviewer/PICC; Admin is never auto-inherited), redirect to Nastavení → Systém → Opicentrum when credentials are missing.
**0.1.1 (107), `d29344d`:** system Back button across tabs, Opicentrum header, Nástěnka "Rozpis pracovišť" card as a button, experimental staffing profiles and 6-month statistics (Admin on PC).
**0.1.0 (106), `d3e442d`:** the review pass `c672352` (arm64-only APK, transport hardening, fewer relay requests).

**Uncommitted relay change (the user asked for it: "Hlavně uprav soubor který se stahuje na vytvoření tunelu pro nové uživatele")**
- `relay/SecureApp.Relay/Program.cs`: new static local function `TuneWireGuardClientConfig(string config)` (just above `ReadVersionName`), applied in `/admin/wireguard/clients` where `configText` is read from wg-easy: `var configText = TuneWireGuardClientConfig(await wg.GetStringAsync(...))`.
- It rewrites, inside `[Peer]`, `AllowedIPs = 10.8.0.0/24, 192.168.50.8/32` (instead of `0.0.0.0/0, ::/0`), sets `PersistentKeepalive = 25` (replaces `0`, or inserts after `AllowedIPs` when absent), and removes every `DNS =` line.
- All onboarding paths read the config from this one endpoint (app QR, `.conf` download in `SettingsViewModel.Updates.cs`, the `new-pc-onboarding.ps1` pickup code via `db.CreateWireGuardPickupCode(configText, …)` stored at creation), so one edit covers them. Already-issued pickup codes keep the old text (24 h validity).
- `dotnet build -c Release` in `relay\SecureApp.Relay`: 0 warnings, 0 errors. **Not tested against a real wg-easy config** (tested only by reading); the first new client created after deployment is the real test.
- **Pushing to `pi` triggers the relay self-redeploy pipeline** — so do not push anything (even an unrelated client release) while this change sits in the tree unless a relay redeploy is wanted.

**Network findings (read-only, nothing changed on the Pi)**
- WireGuard: wg-easy container, clients `10.8.0.x`, endpoint `212.111.15.246:51820`. The user confirmed (2026-10-30): **the public IP is static.**
- Defaults generated by wg-easy: full tunnel, DNS 1.1.1.1, keepalive 0 → all phone traffic goes via the Pi's home line, and an idle tunnel dies silently behind carrier NAT.
- `~/wireguard/docker-compose.yml` on the Pi has a corrupt line `WG_DEFAULT_ADDRESS=192.168.50.8:8080:8080` (should be a `10.8.0.x` address); `relay/ops/README.md` point 2 documents it. It must be fixed BEFORE any wg-easy restart. Server-wide `WG_PERSISTENT_KEEPALIVE=25` / `WG_ALLOWED_IPS` only affect newly generated configs and a restart drops every tunnel briefly.
- The app's relay address: `RelayDefaults.DefaultEndpoint = "ws://192.168.50.8:8080"`; `RelayHttp` rewrites `ws→http` / `wss→https`; `Platforms/Android/Resources/xml/network_security_config.xml` allows cleartext only for scoped hosts; Settings validation text says "např. ws://10.8.0.1:8080" (whether the validator accepts `wss://` was NOT checked).

## What We Tried (Chronological)

1. **Release 0.1.0 → 0.1.3 (10-24 … 10-28).** Standard recipe (edit csproj with the Edit tool, `dotnet build-server shutdown`, publish Android then Windows in the foreground, aapt2 + apksigner checks, zip with PS 5.1 `Compress-Archive`, commit, push `pi` + `github`, upload with `X-Admin-Secret`, verify). Result: all four live. Lessons are in memory `release-artifact-paths.md`.
2. **"Uspi pc" (10-29).** `SetSuspendState` via `System.Windows.Forms.Application` → the command blocks until the PC wakes (timed out at 120 s once, 20 s moved to background the second time); this is normal, the PC slept and woke again.
3. **Widget calendar colour (10-29), three iterations.**
   - (a) "Ještě méně výrazné barvy a aktuální den neměnit barvu jen orámování" → muted palette; today without its own fill colour.
   - (b) "Když na vigetu v kalendáři přepnu na listopad je dnešní den zcela nečitelný" → cause: today (29.10.) spills into the November grid as an adjacent-month day and was dimmed to alpha `0x55` (illegible).
   - (c) "Jen barvu ztlum ale ne tak moc a je možné aby rámeček byl červený a číslo také jen aktuální den a to ztlum" → today only: alpha `0x99` fill, muted red ring and number. Result shipped in 0.1.4.
   - Build error on the way: CS0266 — `0x99000000` is a `long`; fixed with `unchecked((int)0x99000000)`.
4. **Pozice duty marks (10-29).** User: "V rozpisu pracovišť opicentra vyznač sloužící a kde slouží, nemusí se shodovat s pozicemi přes den, využij již načtená data." Implemented with the synced `DutyRosterStore` (no new download): `OpicentrumRosterPersonChip(..., string? DutyPosition = null)`, `DutyPositionOf` (name match via `OpicentrumParsing.NamesMatch`, slots joined with " + "), red chip `#A65F5F` with white text, full cell width so it takes its own line. Rejected: fetching `sluzby7.php` again from the board (extra requests).
5. **Download-frequency audit + release 0.1.4 (10-29).** User: "Zkontroluj zda se data nestahují moc krát zda se zbytečně servery nezatěžují... a pak release".
   - Found: the staffing check re-downloaded week pages the sync had just downloaded; nurses' weeks used one login per week; sync ran the full range every 10 min day and night.
   - Fix: see "Where We Are" (cache, one login, near window every 10 min, full range hourly, night slowdown, hourly gap check).
   - Windows publish error NU1102 (Mono runtime pack for `win-x64` not found) because I added `-r win-x64` → re-run without `-r`. Windows upload got HTTP 405 with `curl -T` (PUT) → `-X POST -H "Content-Type: application/octet-stream" -T`.
6. **"Faltus má hodně nedoručených zpráv" (10-29).** Read-only: `/admin/devices` showed `pendingOutboxCount` 21 for Petr's S25, 3 for "PC v práci", 0 for the rest; the S25 log (last upload 10-24 07:58) shows `WebSocketException: net_webstatus_ConnectFailure` from `App.TryConnect` (209 on 22.10, 30 on 23.10, 11 on 24.10) plus HTTP timeouts. Conclusion: not a delivery bug — the phone cannot reach the relay; messages wait in the relay queue and are delivered at the next connect. Told the user; Petr needs the tunnel up or a network that sees the relay.
7. **"Je možné že tunel neustále zpomaluje provoz na mobilu?" (10-29/30).**
   - Read `relay/ops/README.md` (keepalive 0, corrupt `WG_DEFAULT_ADDRESS`), the 2026-10-07 sample config (full tunnel), and queried per-peer traffic on the Pi without printing keys (`wg show wg0 dump | cut -f4-8` columns only).
   - Answer given: yes, very plausible — full tunnel by default + no keepalive. Caveat stated: the phones' local configs were not seen; the full-tunnel evidence is the sample config.
   - Proposed split tunnel `10.8.0.0/24, 192.168.50.0/24` (or `192.168.50.8/32`) + `PersistentKeepalive = 25`, manual per phone (~30 s each) or server-wide through compose.
8. **"Uspi pc" again (10-30)** — same method, same behaviour.
9. **"V rozpisu opicentra 28.10. Je ve všech řádcích 13-21h odpolední ale 28.10. je svátek" (10-30).**
   - First pass (no data): checked `CzechCalendar` (28.10. is present), found that "Odpolední 13-21 h" is a row label, noted that the board blob on the relay is encrypted and that I never use Opicentrum credentials in dev tooling. Asked which screen and what text — and told the user a hypothesis (`--` parsed as a person) without claiming it.
   - The user sent a screenshot: Pozice, week 26.10.–1.11., red chips "– · Odpolední 13-21 h" in every row of column `st 28.10.` → hypothesis confirmed exactly. Fixed, released as 0.1.5.
   - Wrong turn: `git commit -m @'…'@` with double quotes in the message made PS 5.1 split the text into pathspecs → nothing was committed (and the `push` said "Everything up-to-date"). Redone with `git commit -F <file>`; noted in memory. The commit subject carries a UTF-8 BOM at its start (cosmetic).
10. **"Hlavně uprav soubor který se stahuje na vytvoření tunelu pro nové uživatele..." (10-30).** Found the config is generated in the relay (`/admin/wireguard/clients` → wg-easy API) and distributed through three paths that all read that response → one fix point; wrote `TuneWireGuardClientConfig`, built the relay. Did NOT commit/push/deploy (deploy needs the user's OK).
11. **Domain from a friend who runs Opicentrum (10-30).** The user asked whether a domain from that friend would be a security problem / a dependency. Answered: a DNS record alone is low-risk for message content (E2EE) and APK installs (signature pinned), but whoever controls DNS can redirect the name and obtain certificates, and the Windows zip is not verified the same way (NOT checked in code); dependency = his willingness to edit records; a domain on his hosting would also make the user dependent on his operations. Recommended own domain or free DDNS instead.
12. **DDNS (10-30).** User: "Jak to myslis zdarma ddns?" → explained; then "Je statická veřejná ..." → DDNS is not needed (static IP).
13. **"Jde mi o to že tunel je pro uživatele nepříjemnost navíc..." (10-30).** I proposed dropping the tunnel for ordinary users: relay reachable over HTTPS/WSS on a hostname (free DuckDNS name is enough with a static IP; reverse proxy Caddy with Let's Encrypt on the Pi; router forward 443 → Pi), `/admin/*` kept LAN/VPN-only at the proxy, rate limiting, audit of public routes that were written assuming "never reachable outside the LAN/VPN", tunnel running in parallel during the switch, WireGuard kept for the admin. Asked whether a name like `something.duckdns.org` is acceptable. **No answer yet** (the user ran `/handoff` instead).

## Errors, Wrong Turns and Corrections

- **0.1.5 commit lost to quoting** (above): always check `git log` after a commit in PS 5.1; use `-F file` when the message contains `"`.
- **Misjudged scope of a hypothesis:** I told the user "the board on the relay is encrypted and I cannot see the page" and asked for a screenshot. That cost one round trip but avoided guessing; the screenshot matched the hypothesis. Keep: never use the user's own Opicentrum login or the nurses' viewer login through dev tooling.
- **Parser asymmetry that caused 0.1.5:** two parsers of the same page (`ParsePracovisteWeek` vs `ParsePracovisteGrid`) treated `--` differently. A later review should check every other `ExtractPeople` consumer (leave page `spravavolna.php`, `sluzby7.php`) for non-name cells — only the people parser, `AddDuty` and `NamesMatch` were fixed.
- **Earlier session mistakes carried in memory** (do not repeat): answering a question by building ("Zrychli to zobrazení?"), assuming "rozpis" is one screen, measuring a stale artifact.
- **Tooling errors this stretch:** CS0266 (`0x99000000`), NU1102 (`-r win-x64`), HTTP 405 (PUT upload), multi-line `Edit` anchors with tabs in `App.xaml.cs` (use single-line anchors), `awk` quoting through ssh (use `cut`), the S25 log result file starts with my `len:` line (strip to the first `["` before `ConvertFrom-Json`).

## Key Decisions

- **Split tunnel with `10.8.0.0/24, 192.168.50.8/32` (not the whole `192.168.50.0/24`) in the generated config.** Rejected: leaving the full tunnel (slows all phone traffic through the home upload, drains battery); the whole LAN subnet (gives a phone network reach it never needs). Needs `10.8.0.0/24` so adb over `10.8.0.3:5555` keeps working.
- **`DNS =` removed from generated configs** because with a split tunnel the phone should keep its own resolver (1.1.1.1 is not routed through the tunnel anyway).
- **Fix the config in the relay (one point), not in the three distribution paths and not in wg-easy's compose.** Rejected: `WG_ALLOWED_IPS` / `WG_PERSISTENT_KEEPALIVE` in `~/wireguard/docker-compose.yml` (restart drops all tunnels, corrupt `WG_DEFAULT_ADDRESS` must be repaired first, and it still would not touch existing clients).
- **Do not deploy the relay change without the user's OK** (push to `pi` redeploys the relay).
- **Closed days are not people** — `--` filtered in the parser (not only in the board) so every consumer of `ParsePracovisteWeek` benefits. Rejected: a special case in the duty overlay only (would leave `--` chips on the board).
- **Duty marks use only synced data** — weeks outside the device's synced range show no marks (not confirmed with the user; mention if it comes up).
- **Opicentrum load reduction** (cache + hourly full sync + night slowdown + hourly gap check) was chosen without asking the user for exact numbers; it was part of "zkontroluj zda se data nestahují moc krát". Adjust if the user finds data stale.
- **Static public IP → no DDNS needed**; a hostname is only needed if the relay goes public over TLS (certificate), not for WireGuard.
- **Secret handling unchanged:** `SECUREAPP_RELAY_ADMIN_SECRET` read from the Pi `.env` into a PowerShell variable only, never printed or written, nulled after use; uploads and read-only diagnostics only.

## Evidence & Data

### Releases since the parent (all verified through `/download/*/version`)

| Version | Build | Commit | Date | Headline | APK (B) | Windows zip (B) |
|---|---:|---|---|---|---:|---:|
| 0.1.0 | 106 | `d3e442d` | 10-24 | review pass: arm64-only, fewer requests, transport hardening | 40,182,640 | 66,863,618 |
| 0.1.1 | 107 | `d29344d` | 10-25 | Back button, Opicentrum UI, staffing profiles | 40,031,746 | 67,071,485 |
| 0.1.2 | 108 | `98e3535` | 10-27 | widget month calendar, one e-mail = one person, Redakce menu | 40,436,745 | 67,106,769 |
| 0.1.3 | 109 | `1976d74` | 10-28 | staffing-gap alerts, nurses' portal card | n/r | n/r |
| 0.1.4 | 110 | `4f3ef6a` | 10-29 | duty marks, calmer widget, less portal load | 40,477,705 | 67,154,315 |
| 0.1.5 | 111 | `a9c3af1` | 10-30 | closed-day (`--`) parsing fix | 40,134,222 | 67,154,652 |

(n/r = not recorded in this session's context. 0.1.4 manifest `Version="0.1.4.110"`; zips have 218 entries from 0.1.1 on.)

### WireGuard peers on the Pi (read-only: allowed-ips, handshake, rx, tx, keepalive; 2026-10-30)

| Peer | Last handshake | rx (server received) | tx (server sent) | Keepalive |
|---|---|---:|---:|---|
| 10.8.0.2 | 50 589 min ago | 135 MiB | 878 MiB | off |
| 10.8.0.3 (S23+) | 1 min ago | 5 913 MiB | 60 863 MiB | off |
| 10.8.0.4 | 1 min ago | 8 773 MiB | 539 MiB | off |
| 10.8.0.5 | 6 822 min ago (≈ 4.7 days) | 887 MiB | 7 025 MiB | off |
| 10.8.0.6 | never | 0 | 0 | off |
| 10.8.0.7 | 136 min ago | 73 MiB | 1 828 MiB | off |
| 10.8.0.8 | 31 251 min ago | 810 MiB | 4 944 MiB | off |
| 10.8.0.9 | 45 742 min ago | 26 MiB | 252 MiB | off |

- Reading: the S23+ received 60.9 GiB through the tunnel — far more than SecureApp itself moves (one update is 40 MB) → consistent with a full tunnel carrying ordinary phone traffic (web, video, updates). The assumption that every phone is full-tunnel is NOT verified per phone (phones' local `AllowedIPs` cannot be seen from the server).
- The mapping "10.8.0.5 = Petr's S25" is my inference from timing (last exchange 4.7 days ago ≈ his last connection on 10-24), not read from wg-easy.

### Relay `/admin/devices` snapshot (2026-10-29)

| Device | Version | Pending outbox | Last active |
|---|---|---:|---|
| Zařízení S25 uživatele Petr | 0.1.0 (106), role 1 | **21** | 10-24 20:18 |
| PC v práci | 1.99.0.102 (102) | 3 | 10-24 |
| DESKTOP-F8AS2U6 | 0.1.1 | 0 | n/r |
| S23+ (Vilém) | 0.1.3 | 0 | now |
| S9+ | 0.1.1 | 0 | n/r |

### Petr's S25 log (last upload 2026-10-24 07:58)

| Day | `WebSocketException net_webstatus_ConnectFailure` from `App.TryConnect` |
|---|---:|
| 22.10 | 209 |
| 23.10 | 30 |
| 24.10 | 11 |

### The 28.10. screenshot (ground truth for 0.1.5)

- Screen: Opicentrum → Pozice, header "26. října – 1. listopadu 2026", subtitle "Aktualizováno: před 4 min · červeně = služba", column `st 28.10.` with a red chip "– · Odpolední 13-21 h" in every row (Sál č. 1 … Stacionář ODS); other columns normal (names, red duty chips for real duty). The "-" in the last row of the next column ("- / -") shows the same `--` pattern on neighbouring cells.

### Relay config example (what wg-easy emits, per the 2026-10-07 handoff, and what the new function produces)

```
# before                                   # after TuneWireGuardClientConfig
[Interface]                                [Interface]
Address = 10.8.0.x/24                      Address = 10.8.0.x/24
DNS = 1.1.1.1                              (DNS line removed)
[Peer]                                     [Peer]
AllowedIPs = 0.0.0.0/0, ::/0               AllowedIPs = 10.8.0.0/24, 192.168.50.8/32
PersistentKeepalive = 0                    PersistentKeepalive = 25
Endpoint = 212.111.15.246:51820            Endpoint = 212.111.15.246:51820
```

### Widget colours after 0.1.4 (ARGB)

| Constant | Value | Note |
|---|---|---|
| `DutyFill` | `0xFFA65F5F` | red |
| `VacationFill` | `0xFF4F8466` | green |
| `TripFill` | `0xFFB5A163` | yellow (dark number text) |
| `WeekendFill` | `0xFF484D52` | grey |
| `DayOffFill` | `0xFF58798F` | blue-grey |
| `SickFill` | `0xFFA87A55` | brown |
| `HolidayFill` | `0xFF7A6090` | purple |
| `TodayRing` / `TodayText` | `0xFFD05A5A` / `0xFFE87878` | today only |
| `AlertRing` | `0xFFFF4081` | staffing-gap day; wins over the today ring |
| adjacent-month alpha | `0x55` (today `0x99`) | |

### Opicentrum load after 0.1.4

| Job | Before | After |
|---|---|---|
| roster sync | full range every 10 min, day and night | `today-7 … today+21` every 10 min; full range through end of next month hourly; hourly at night (22–05) |
| staffing-gap check | re-downloaded doctor weeks, one login per nurse week | cached doctor weeks (25 min), one nurse login, hourly, skipped at night |

### Data files and scripts

- Scratchpad: `C:\Users\dvora\AppData\Local\Temp\claude\H--Visual-Studio-C--Aplikace\a53cdf21-e436-4afd-a2f0-a8d32f8bf044\scratchpad\` (includes `commitmsg.txt`). The latest screenshot is saved at `C:\Users\dvora\.claude\uploads\a53cdf21-e436-4afd-a2f0-a8d32f8bf044\fc7fcad3-image.jpg`.
- Release artifacts (not committed): `src\SecureApp.Presentation\bin\Release\net10.0-android\android-arm64\publish\com.companyname.secureapp.presentation-Signed.apk` and `src\SecureApp.Presentation\bin\Release\SecureApp-win-x64-0.1.5.zip`.

## Code Analysis

- `OpicentrumParsing.ExtractPeople(string cellHtml)` — splits a day cell into `(Name, PersonId?, Date?, WorkplaceId?)`; the final `.Where(n => n.Any(char.IsLetter))` is the 0.1.5 fix. `ParsePracovisteWeek(html)` is used by sync, history, board, name lists, move confirmation; `ParsePracovisteGrid(html)` keeps empty cells and `Closed` (`--`) cells for the staffing check.
- `OpicentrumParsing.NamesMatch(a, b)` — title/order/diacritics-tolerant; now requires a letter on both sides.
- `OpicentrumSyncService`: `MergePracovisteAsync` fills `DoctorGridCache` (static `ConcurrentDictionary<DateOnly, (grid, fetchedAtUtc)>`), `TryGetCachedDoctorGrid(weekOf, maxAge)`, `BuildGridWeek`, `FetchRosterGridsAsync(weeks, nurses, ct)` (one login); `FetchFullRosterBoardAsync` is what Pozice uses (week page → `ParsePracovisteWeek`).
- `DutyRosterStore` (Preferences JSON per profile): `GetMany(IEnumerable<DateOnly>)`, `DutyEntry(Position, Name)`; written by the sync (`sluzby7.php` slots plus the "Odpolední" row from `pracoviste.php`).
- `StaffingGapAnalysis.Analyze(doctors, nurses, today)` — `AfternoonLookAheadDays = 7`, `PositionLookAheadDays = 2`; a day is "planned" when an operating room (`Sál č. N`) has a doctor; closed cells (`--`) are skipped, empty cells are gaps.
- `App.RunBackgroundJobsAsync` — per-job throttles through Preferences (`opicentrum_last_bg_full_sync_utc`, `_backgroundOpicentrumSyncInterval` 10 min, `_backgroundOpicentrumFullSyncInterval` 60 min, `_backgroundOpicentrumNightSyncInterval` 60 min, `IsNightHour()`, `_staffingGapCheckInterval` 60 min).
- `NotificationsWidgetProvider`: fixed 6×7 grid of 42 cells (outer FrameLayout = ring, inner TextView = fill + number), always set every property; the in-app Rozpis calendar does not show the gap ring (widget only).
- Relay `Program.cs`: `/admin/wireguard/clients` (wg-easy API with the wg-easy password as a plain `Authorization` header; stores a pending peer and a 24 h pickup code), `/onboarding/pickup/{code}`, `/download/onboarding[.ps1]`, `/download/onboarding/{code}`, board endpoints `MapOpicentrumBoard`. Comments say the relay is "never reachable outside the LAN/VPN" — several public, unauthenticated routes depend on that.

## Files Changed

### Source code (since the parent)
- `src/SecureApp.Presentation/Workplace/OpicentrumParsing.cs` — `ExtractPeople` and `NamesMatch` ignore text without a letter (0.1.5).
- `src/SecureApp.Presentation/Workplace/OpicentrumSyncService.cs` — `AddDuty` letter check (0.1.5); `DoctorGridCache`, `FetchRosterGridsAsync`, `BuildGridWeek` (0.1.4).
- `src/SecureApp.Presentation/Workplace/StaffingGaps.cs`, `Workplace/DutyRosterStore.cs`, `Domain/Interfaces/Services/IOpicentrumSyncService.cs` (0.1.4).
- `src/SecureApp.Presentation/ViewModels/OpicentrumBoardViewModel.Positions.cs`, `Controls/PositionsBoardView.cs`, `Views/OpicentrumBoardPage.xaml` — duty marks (0.1.4).
- `src/SecureApp.Presentation/Platforms/Android/NotificationsWidgetProvider.cs` — palette and today styling (0.1.4).
- `src/SecureApp.Presentation/App.xaml.cs` — background job intervals (0.1.4).
- `relay/SecureApp.Relay/Program.cs` — `TuneWireGuardClientConfig` (UNCOMMITTED, not deployed).

### Config / release metadata
- `src/SecureApp.Presentation/SecureApp.Presentation.csproj` — `0.1.5` / `111`.
- `src/SecureApp.Presentation/Resources/Raw/changelog.json` — entries 0.1.4 and 0.1.5.
- Memory: `feedback-versioning-alpha.md` (next code 112), `release-artifact-paths.md` (0.1.5 gotchas), `MEMORY.md` index.

### Docs
- `plans/handoffs/HANDOFF_standalone-24caf3f8_releases-0-1-5-tunnel_2026-10-30.md` — this file.

## User Feedback & Preferences (REQUIRED)

- **Language / tone:** terse Czech with diacritics in my answers; the user writes without diacritics and with typos; they/them for people whose pronouns are unknown.
- **Release authorisation, verbatim:** 0.1.4 "...a pak release" (in the audit request), 0.1.5 "Releace". Standing rule from the paste prompt: "Do NOT push, deploy or release without my explicit OK."
- **Widget colours:** "Ještě méně výrazné barvy a aktuální den neměnit barvu jen orámování...", "Když na vigetu v kalendáři přepnu na listopad je dnešní den zcela nečitelný....", "Jen barvu ztlum ale ne tak moc a je možné aby rámeček byl červený a číslo také jen aktuální den a to ztlum".
- **Duty marking:** "V rozpisu pracovišť opicentra vyznač sloužící a kde slouží, nemusí se shodovat s pozicemi přes den, využij již načtená data."
- **Load on servers:** "Zkontroluj zda se data nestahují moc krát zda se zbytečně servery nezatěžují... a pak release".
- **Messages:** "Faltus má hodně nedoručených zpráv..." — expects an explanation, and the self-healing HARD RULE still applies (the user must never fix chats manually).
- **Network:** "Je možné že tunel neustále zpomaluje provoz na mobilu?"; "Hlavně uprav soubor který se stahuje na vytvoření tunelu pro nové uživatele..."; "...byl by velký bezpečnostní problém kdyby mi poskytl doménu kamarád co provozuje opicentrum, byl bych na něm moc závislý co se týče úprav nebo provozu?"; "Jak to myslis zdarma ddns?"; "Je statická veřejná ..."; **"Jde mi o to že tunel je pro uživatele nepříjemnost navíc..."** → the real requirement: ordinary users should not need a tunnel.
- **Holiday bug report:** "V rozpisu opicentra 28.10. Je ve všech řádcích 13-21h odpolední ale 28.10. je svátek." (screenshot followed).
- **Practical asks:** "Uspi pc" (twice; the command blocks until wake-up, that is fine).
- **Standing HARD RULES (memory):**
  - Only signed Release builds on real devices (S23+, Petr's S25, PC, S9+); never Debug or `dotnet build -t:Run` — a signature mismatch silently wipes the local vault. Never uninstall or clear app data; never `adb uninstall` after a failed `install -r`.
  - Chat/group recovery must be self-healing.
  - Batch builds; build/install only when the user says so; release/push/deploy only with explicit OK ("Release"/"Ano" counts).
  - **Opicentrum personal credentials never go through chat or dev tooling.** The shared read-only nurses' viewer login the user once typed in chat is NOT stored anywhere; each Admin/Modifier device enters it in Nastavení → Systém → Opicentrum → "Opicentrum – sestry".
  - csproj/source edits through the Edit tool, never PowerShell text replacement.
  - Do not adb-tap the S23+ (the Remote Control phone; `adb connect 10.8.0.3:5555` works); always pass `-s <serial>` with two devices.
  - Do not read or print `keystore\secureapp-release.keystore.password.txt` or `keystore\PRISTUPY.md`; never print WireGuard keys / preshared keys (`wg show wg0 dump` contains them — print only `cut -f4-8`).
  - Answer a question first, with measured numbers, before building.
  - Commit trailers: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_018dNGqDVhdndMxEK6VtGe6j`.
  - Version names are alpha `0.x`; next build code **112**; first real release 1.0; `ApplicationDisplayVersion` stays numeric.

## Where We're Going

1. **Get the user's decision on the network direction** (their last statement is "the tunnel is an extra annoyance for users"):
   - **A (recommended in my last message): public HTTPS/WSS relay without tunnel for members.** Needs: a hostname (a free `*.duckdns.org` name suffices with the static IP `212.111.15.246`), Caddy (or similar) on the Pi with a Let's Encrypt certificate, router forward 443 → Pi (the user must do this), proxy that exposes only app routes and keeps `/admin/*` LAN/VPN-only, rate limiting, an audit of public unauthenticated routes in `relay/SecureApp.Relay/Program.cs` (`/download/*`, `/download/onboarding/{code}`, `/onboarding/pickup/{code}`, anything else assuming "LAN/VPN only"), then change the default endpoint in `RelayDefaults.DefaultEndpoint` to `wss://…` (check that the Settings validator accepts `wss://`; `RelayHttp` already maps `wss→https`; `network_security_config.xml` needs no cleartext exception for a TLS host), release a client, keep the tunnel working in parallel until everyone has moved.
   - **B: keep the tunnel but make it painless** (split tunnel + keepalive 25 via the pending relay change; users' existing tunnels edited by hand).
   - Ask: "Stačí vám jméno typu `něco.duckdns.org`?" and wait. Do not touch the router/Pi without OK.
2. **Deploy the pending relay change only on OK:** commit `relay/SecureApp.Relay/Program.cs`, `git push pi main` (triggers the self-redeploy; verify per `relay/ops/README.md`), then create ONE test client through the app/relay (Admin device: Nastavení → onboarding, or `/admin/wireguard/clients`) and check the returned config text; revoke the test peer afterwards (the sweep service deletes unused pending peers after one hour). Fix the corrupt `WG_DEFAULT_ADDRESS` in `~/wireguard/docker-compose.yml` before any wg-easy restart (README point 2).
3. **Existing tunnels** (8 peers): per phone in the WireGuard app — `AllowedIPs = 10.8.0.0/24, 192.168.50.8/32`, `PersistentKeepalive = 25`, remove `DNS`, save, toggle off/on (~30 s). Petr's S25 first (21 pending messages). Offer to write the exact steps; also offer to check the S23+ itself without tapping it.
4. **Verify 0.1.5 on devices:** in Pozice, column 28.10. must show no `--` chips after "Obnovit" (also check that no other "--" appears in Rozpis/Volno or in the leave data); S23+/S25/PC update through the in-app updater; offer the S9+ cable update (`adb -s 22dab4387e0b7ece install -r` with the signed APK) only if asked.
5. **Review other parsers for non-name cells** (`spravavolna.php` leave names, `sluzby7.php` slots, `FetchStaffNamesAsync`) — only the people parser, `AddDuty` and `NamesMatch` were hardened.
6. **Position-suggestion plan phases 3–4** (`POZICE_NAVRH_PLAN.md`): suggestion display, then write the week to Opicentrum with undo — NOT started, needs the user's go.
7. **Optional follow-ups (not requested):** reword `NotConfiguredMessage` ("níže") in `OpicentrumSyncService.cs`; gap ring in the in-app Rozpis calendar; relay-side at-least-once delivery (parent #4); CMS follow-ups; unused `IMessageRepository.GetByGroupAsync` check (it still exists in `MessageRepository.cs:68`).

## Risks & Blockers

- **A client push redeploys the relay.** The uncommitted `Program.cs` change would go live with the next `git push pi` (e.g. a client-only release), possibly unannounced. Before pushing, decide: deploy it deliberately, or `git stash`/revert it.
- **Public exposure of the relay** (option A) changes the threat model: the relay was designed as LAN/VPN-only; some routes are unauthenticated by design. Needs the route audit and an admin-route block before the router port is opened.
- **Windows zip trust:** I checked only that the APK signature is pinned by Android; whether the Windows updater verifies the downloaded zip (hash/signature) was NOT checked — relevant if DNS/TLS moves to a name the user does not fully control.
- **Per-phone tunnel config unknown:** the full-tunnel claim is from a sample config and traffic volumes, not from reading each phone.
- **Auto-mode classifier** may refuse the first admin-secret read in a session; report, ask, wait — never circumvent.
- **Environment:** low memory → `dotnet build-server shutdown` then each publish in the foreground, one at a time; PS 5.1 quirks (`git commit -m` with quotes, BOM from `Set-Content`, `NativeCommandError` noise from `git push` stderr — check the `old..new  main -> main` line); `bd` not installed; no automated tests (verification = builds + device logs).

## Open Questions

- Does the user want option A (public HTTPS relay, no tunnel for members) or B (keep tunnel, make it split + keepalive)? Is a `*.duckdns.org` name acceptable?
- OK to deploy the pending relay change (`TuneWireGuardClientConfig`) now, independent of A/B?
- Does the Windows updater verify the downloaded zip? (Not checked.)
- Does the Settings relay-address validator accept `wss://`? (Not checked.)
- Did Petr's S25 ever reconnect (21 pending)? Is "10.8.0.5 = Petr" correct?
- Are the 0.1.4 sync intervals (near window every 10 min, full range hourly, nights hourly) acceptable to the user for freshness?
- Which phones actually run a full tunnel? (Confirm in the WireGuard app on the S23+.)

## Quick Start for Next Session

```bash
# git.exe is NOT on PATH:
#   C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe
git status -s ; git log --oneline -6   # expect HEAD a9c3af1 (0.1.5) plus this session's 'session:' commits; Program.cs relay change committed locally but NOT pushed

# Memory index (HARD RULES) and parent handoffs
#   C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md
#   plans/handoffs/HANDOFF_standalone-24caf3f8_chat-boards-size-review_2026-10-24.md   (parent; release recipe)
#   relay/ops/README.md   (wg-easy: keepalive 0, corrupt WG_DEFAULT_ADDRESS, redeploy pipeline)

# Key files to read first
#   relay/SecureApp.Relay/Program.cs   (/admin/wireguard/clients, TuneWireGuardClientConfig, onboarding routes, public routes to audit)
#   src/SecureApp.Presentation/Transport/RelayDefaults.cs and RelayHttp.cs   (ws:// default, ws->http / wss->https)
#   src/SecureApp.Presentation/Platforms/Android/Resources/xml/network_security_config.xml
#   src/SecureApp.Presentation/Workplace/OpicentrumParsing.cs   (ExtractPeople, NamesMatch)

# Evidence
#   Peer table and devices in this file. The Pi check was read-only (`wg show wg0 dump`, only columns 4-8 printed:
#   allowed-ips, handshake, rx, tx, keepalive; NEVER the key columns). The exact container name/command was not recorded
#   in this handoff — look it up (`docker ps` on the Pi) before re-running.

# Verify current state
dotnet build relay\SecureApp.Relay\SecureApp.Relay.csproj -c Release   # look for 'Počet chyb' = 0
curl.exe -s http://192.168.50.8:8080/download/android/version          # expect VersionCode 111, 0.1.5

# Next action
# Ask the user: "Chcete veřejné HTTPS (bez tunelu pro členy) – stačí jméno typu něco.duckdns.org? Nebo nasadit jen opravenou konfiguraci tunelu?"
# Do NOT push, deploy, release or touch the router/Pi without the explicit OK.
```

## Session Closed
**Closed at:** 2026-10-30 22:01 (local)
**Commits:** `2a165d2` (relay: split-tunnel config, local only, NOT pushed), `a1c4c84` (this handoff); release HEAD before them: `a9c3af1` (0.1.5, pushed)
**Session status:** Handed off to next session

