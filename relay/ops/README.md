# Relay redeploy pipeline — one-time Pi setup

Lets a push from the dev PC + an admin-only button in the app (`POST /admin/deploy`) rebuild and
restart the relay container, without the app holding any SSH key or Docker credential. See
`Program.cs`'s comment above `/admin/deploy` and `DEVELOPMENT_PLAN.md` for the full design
rationale (why not a Docker-socket-in-container approach).

Run these once on the Pi (over SSH, as the `dvorakv1` user):

## 1. Create the bare repo the dev PC will push to

```bash
mkdir -p ~/secureapp-repo.git
cd ~/secureapp-repo.git
git init --bare
```

## 2. Install the post-receive hook

Copy `post-receive.sample` from this folder to `~/secureapp-repo.git/hooks/post-receive` on the
Pi (e.g. `scp relay/ops/post-receive.sample dvorakv1@192.168.50.8:~/secureapp-repo.git/hooks/post-receive`
from the dev PC), then:

```bash
chmod +x ~/secureapp-repo.git/hooks/post-receive
```

This checks each push out into `~/SecureApp` — the same directory the relay was already manually
deployed into (see `DEVELOPMENT_PLAN.md`'s Milestone 5 notes), so existing `data/` (the real
`relay.db3` + library files) is untouched — `.gitignore` excludes it, so checkout never touches it.

## 3. Install the deploy watcher (systemd)

Copy `deploy.sh`, `secureapp-deploy.path`, and `secureapp-deploy.service` from this folder onto
the Pi, then:

```bash
chmod +x ~/SecureApp/relay/ops/deploy.sh
sudo cp ~/SecureApp/relay/ops/secureapp-deploy.path /etc/systemd/system/
sudo cp ~/SecureApp/relay/ops/secureapp-deploy.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now secureapp-deploy.path
```

Adjust the hard-coded `/home/dvorakv1/SecureApp...` paths in `deploy.sh`/`secureapp-deploy.path`
first if the Pi's username or deploy directory differs.

## 4. First push (from the dev PC, in the repo root)

```powershell
git remote add pi ssh://dvorakv1@192.168.50.8/home/dvorakv1/secureapp-repo.git
git push pi main
```

This only updates the working copy — it does **not** rebuild the container. Rebuild by either:
- Tapping "Redeploy relay" in the app's Settings → Admin section (Admin role only), or
- On the Pi: `touch ~/SecureApp/relay/SecureApp.Relay/data/deploy-requested` (same effect, no HTTP call).

Progress/errors land in `~/SecureApp/relay/SecureApp.Relay/data/deploy.log`.

## If a redeploy silently does nothing (2026-09-24)

`POST /admin/deploy` answering **202 does not mean the relay was rebuilt** — it only means the
marker file was written. On 2026-09-24 the marker was being written correctly and nothing ever
consumed it: `systemctl is-active secureapp-deploy.path` reported **failed** (still `enabled`, so
it looked fine at a glance), no `data/deploy.log` was ever created, and the running container
turned out to be a build from the previous day. This is the same class of failure that has bitten
this project repeatedly — the relay and every client share `SecureApp.Domain`, so a stale relay
silently drops fields it does not know about.

Check, in this order:

```bash
systemctl is-active secureapp-deploy.path     # "failed" => nothing is watching the marker
ls -la ~/SecureApp/relay/SecureApp.Relay/data/deploy.log
docker inspect -f '{{.State.StartedAt}}' secureapp-relay
```

Rebuilding by hand needs no root — `dvorakv1` is in the `docker` group:

```bash
cd ~/SecureApp/relay/SecureApp.Relay && docker compose build && docker compose up -d
```

Repairing the watcher itself does need root (`sudo systemctl status secureapp-deploy.path` for the
reason, then `daemon-reload` / `restart`). **Always verify the running build afterwards** rather
than trusting the 202 — e.g. `curl -s -o /dev/null -w '%{http_code}' http://192.168.50.8:8080/`
should be `302` now that the root redirect exists.

### The specific failure found 2026-09-24, and its fix

`secureapp-deploy.service` shipped with `User=dvorakv1`, but the marker file it reacts to is written
by the relay **container** (root) into the root-owned `./data` volume. So `deploy.sh`'s `rm -f` of
the marker failed with "Permission denied", the service exited 1, and after a few fast retries the
`.path` unit hit its start limit and stayed `failed` from 2026-09-04 on — the in-app "Redeploy relay"
button silently did nothing the whole time. Fixed by dropping `User=dvorakv1` so the service runs as
root (root can delete the root-owned marker and run docker compose; docker is root-equivalent
anyway). To apply the fix on the Pi (needs root, one time):

```bash
sudo cp ~/SecureApp/relay/ops/secureapp-deploy.service /etc/systemd/system/
sudo cp ~/SecureApp/relay/ops/secureapp-deploy.path    /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl reset-failed secureapp-deploy.path secureapp-deploy.service
sudo systemctl enable --now secureapp-deploy.path
systemctl is-active secureapp-deploy.path        # expect: active
```

Then test end to end: tap "Redeploy relay" in the app (or `curl -X POST -H "X-Admin-Secret: …"
http://192.168.50.8:8080/admin/deploy`), and watch `~/SecureApp/relay/SecureApp.Relay/data/deploy.log`
fill in — the marker should be gone within a second or two and the log should show a build.

Note also that `data/` is root-owned, so the marker file cannot be created or removed over plain
SSH as `dvorakv1` — going through `/admin/deploy` (the relay container runs as root) is the only
way to set it without `sudo`.

## Future: fully automatic

Point a systemd timer (or the post-receive hook itself) at the same marker-file trick instead of
waiting for a button tap — nothing about `/admin/deploy` or `deploy.sh` needs to change.

# WireGuard (wg-easy) — two things that bite (2026-09-24)

## 1. An idle tunnel dies silently, and the app looks "connected" the whole time

Symptom: one phone can open `http://192.168.50.8:8080` and another cannot, both showing an
active tunnel. What actually distinguishes them is traffic volume, visible on the Pi:

```bash
docker exec wg-easy sh -c 'wg show wg0 dump'
```

WireGuard re-handshakes roughly every 2 minutes **while traffic flows**. A handshake column
reading tens of minutes old means nothing has gone through that tunnel since — it is dead, even
though the phone's WireGuard app still shows it as up. On 2026-09-24 the working phone had
846 MiB sent and a handshake seconds old; the failing one had 2 MiB and a handshake 73 minutes
old. The browser was posting requests into a tunnel whose carrier-NAT mapping had long expired,
so nothing came back and the page rendered blank.

Root cause: wg-easy generates every client config with **`PersistentKeepalive = 0`**. For a phone
behind carrier-grade NAT that is wrong — nothing refreshes the mapping, so any tunnel that goes
quiet stops working until it is toggled off and on by hand. A busy phone never notices, which is
exactly why this looks like a per-device mystery.

Fix on one device, no server change, takes 30 seconds: WireGuard app → the tunnel → edit →
**Persistent keepalive = 25** → save → toggle off/on. Confirmed live on 2026-09-24: the phone
immediately loaded the page and pulled the 61 MB APK (the peer's sent counter jumped 2 MiB → 60 MiB).

Fix for everyone: add `WG_PERSISTENT_KEEPALIVE=25` to `~/wireguard/docker-compose.yml` and restart
wg-easy. **This briefly drops every tunnel**, so pick the moment. Note it only changes the config
text wg-easy *generates* — phones that already imported a config still carry
`PersistentKeepalive = 0` locally and must either re-import or set the value by hand.

## 2. `WG_DEFAULT_ADDRESS` in the compose file is currently corrupt

`~/wireguard/docker-compose.yml` contains:

```yaml
- WG_DEFAULT_ADDRESS=192.168.50.8:8080:8080
```

That is the relay's port-mapping string pasted into the wrong variable; it should be `10.8.0.x`.
The **running** container still has the correct value (it predates the edit), so nothing is broken
right now — but any restart, planned or not, would apply the corrupt one and break address
assignment for new clients. Fix the line before restarting wg-easy for any reason.

# New PC onboarding — WireGuard client + SecureApp in one script

`new-pc-onboarding.ps1` (2026-10-07, user's own ask, after repeatedly fighting the WireGuard GUI's
"Import tunnel from file" dialog setting up a new work PC — "PC v praci" was the real case that
prompted this). One idempotent script instead of the previous fully-manual flow:

```powershell
# run from the repo root, or copy the script anywhere on the target PC
.\relay\ops\new-pc-onboarding.ps1 -ConfigPath "C:\Users\you\Downloads\PC_v_praci.conf"
```

**`new-pc-onboarding.bat`** is a double-click launcher for people who don't want a PowerShell
command line (a `.ps1` doesn't run on double-click by default — it opens in a text editor, real
friction on a brand-new PC). Copy both files into the same folder (e.g. a USB stick) alongside the
downloaded `.conf`, then just double-click the `.bat` — it auto-detects a `.conf` sitting next to
it (newest wins if there's more than one) with no typing needed, or accepts one dragged onto its
icon. Verified both paths (auto-detect and drag-and-drop) against a dummy script before trusting it.

It self-elevates, then: (1) installs the official WireGuard client if missing, (2) downloads +
unpacks the SecureApp Windows portable build into `C:\SecureApp` (override with `-InstallDir`) if
missing, and (3) if `-ConfigPath` is given, installs that `.conf` directly as a Windows service
tunnel via `wireguard /installtunnelservice <path>` — a real, documented WireGuard CLI command
(confirmed against `git.zx2c4.com/wireguard-windows/tree/docs/enterprise.md`) that **bypasses the
GUI import dialog entirely**, so its interactive tunnel-name validation (which rejects spaces —
see the WireGuard fix in `SettingsViewModel.Updates.cs`'s `DownloadWireGuardConfigAsync`) never
runs at all. The script re-sanitizes the filename to the same WireGuard-safe charset first anyway,
as defense-in-depth for a config from an older build or one renamed by hand.

Not fully silent: WireGuard's own docs only document an unattended install via the raw MSI +
`msiexec`, not the bootstrapper `.exe` this script downloads — expect one UAC prompt and the
installer's own short progress UI, not a single headless command. `-RelayBaseUrl` defaults to
`http://192.168.50.8:8080`; override it if the target PC reaches the relay by a different address
(e.g. over the WireGuard tunnel itself, once that part is already up).

# No-admin install — work PC with no administrator rights

`install-no-admin.ps1` (2026-11-19, user's own ask: "PC v praci" has no admin rights at all, and
the user didn't want the Windows build to grow (self-contained would add ~150MB of .NET runtime to
every single release, which also means ~150MB more written to the Pi's SD card on every release
upload — a real wear concern on a card the user already worries about). Unlike
`new-pc-onboarding.ps1`, this script **never elevates** — everything lands in the user's own
profile (`%LOCALAPPDATA%\SecureApp\...` by default), nothing touches Program Files or the machine
registry hive, and no WireGuard tunnel is installed — it targets a PC reaching the relay's PUBLIC
endpoint directly (confirmed reachable from outside the LAN/VPN 2026-11-19).

```powershell
.\relay\ops\install-no-admin.ps1
```

**`install-no-admin.bat`** is the double-click launcher (same self-fetch trick as
`new-pc-onboarding.bat` — downloads the current `.ps1` from the relay's generic
`/download/ops/{name}` route every run, so re-uploading just the `.ps1` here keeps every already-
distributed `.bat` copy current).

**Not just a one-time install** — reworked 2026-11-19 per the user's own follow-up ("ať je to
součástí spouštěcího souboru, co to zkontroluje a případně stáhne potřebné"): the Start Menu
shortcut this script creates points at `Spustit-SecureApp.cmd`, which re-runs this same script
(a cached local copy, not a re-download) on **every** launch before starting the app, not just the
first one. That stays cheap day to day — every check is a local `Test-Path`/`Get-AppxPackage` call;
network/download only happens on the (first, or any later broken-install) run where something is
actually missing. This is a separate concern from the 20-30s cold-start time reported the same
day — see [[project-windows-startup-latency]] — which is about the app's own startup work once it's
already running, not this script.

What it does, step by step — see the `.ps1`'s own header for the full reasoning and sources:
0. Caches a copy of itself at `%LOCALAPPDATA%\SecureApp\app\install-no-admin.ps1` so every later
   launch runs checks from disk — a normal day never needs the relay reachable just to open the app.
1. **.NET Desktop Runtime 10**: installed user-locally via Microsoft's own `dotnet-install.ps1`
   (officially documented as a non-admin install path) into `%LOCALAPPDATA%\SecureApp\dotnet`;
   `DOTNET_ROOT` is then set as a **user** environment variable so the app's apphost finds it
   without any registry entry (which only an elevated installer could write).
2. **Windows App SDK Runtime 1.7**: runs the same installer `new-pc-onboarding.ps1` uses
   (`/download/ops/windowsappruntimeinstall-x64.exe`), but without elevating first — per
   Microsoft's own deployment guide, a non-elevated run registers the MSIX packages for the
   current user only (the machine-wide path, and the admin requirement, only kicks in when already
   elevated). **Not guaranteed on every PC** — a locked-down org policy can still block per-user
   MSIX sideloading entirely; the script detects this and says so plainly instead of silently
   failing later.
3. **SecureApp itself**: downloads the ordinary `/download/windows` portable zip (same file the
   relay already serves, no new upload step) into `%LOCALAPPDATA%\SecureApp\app`.
4. The Start Menu shortcut (user scope, no admin) points at `Spustit-SecureApp.cmd`, which re-runs
   step 0-3's checks from the local cached copy, then starts the app with `DOTNET_ROOT` set for
   that process (a shortcut alone can't set an environment variable, which is why this indirection
   exists at all).

Steps 2 and 3 talk to the relay over its self-signed certificate, pinned by SHA-1 thumbprint
(captured live 2026-11-19 — different from `RelayTls.PinnedPublicKeySha256Base64`, since Windows
PowerShell 5.1's `X509Certificate2` has no `ExportSubjectPublicKeyInfo`; update the thumbprint in
the script if the relay's certificate is ever regenerated) via a **compiled .NET delegate**
(`Add-Type`), not a plain PowerShell scriptblock — a scriptblock assigned directly to
`ServerCertificateValidationCallback` throws "There is no Runspace available to run scripts in
this thread" once .NET invokes it from a non-interactive background thread (hit this for real
while testing — every HTTPS call failed with a generic, misleading "connection closed" error with
no hint of the actual cause).

Upload both files to the relay's downloads dir once (same manual-scp hand-off as everything else
under `/download/ops/`):

```powershell
scp -F "C:\Users\dvora\.ssh\config" relay\ops\install-no-admin.ps1 relay\ops\install-no-admin.bat secureapp-pi:~/SecureApp/relay/SecureApp.Relay/data/downloads/
```

# Backup — Pi data to the dev PC

`backup-from-pi.ps1` (2026-09-23, user's own ask: don't lose community data or admin access if a
device/disk/the Pi itself is lost) pulls `relay/SecureApp.Relay/data/` (relay.db3, shared-library
files, hosted APK), `relay/SecureApp.Relay/.env` (the admin secret + wg-easy credentials), and
`wireguard/data/` (every paired device's WireGuard config) from the Pi to
`C:\Users\dvora\SecureApp-Backups\<timestamp>\` on this PC, keeping the last 14 daily runs.

**One-time Pi setup** — the WireGuard data is root-owned by default; the backup user needs read access:

```bash
sudo chmod -R a+rX ~/wireguard/data
```

**One-time PC setup** (updated 2026-09-23 — this PC isn't reliably on at 3 AM, so the task now has a
second trigger firing at every logon too; a script-level `$MinHoursBetweenRuns` guard in
`backup-from-pi.ps1` skips the run if the newest backup is under 20h old, so a logon shortly after the
3 AM run — or several logons in one day — doesn't produce redundant runs). Registered via an XML task
definition (`schtasks /Create /XML ...`) rather than `Register-ScheduledTask`, because the
`ScheduledTasks` PowerShell module's CIM provider was refusing all registrations with "Přístup byl
odepřen" on this machine that day (`schtasks.exe` itself worked fine — root cause not tracked down,
suspected related to the pending-reboot state from a concurrent .NET SDK auto-update):

```powershell
schtasks /Create /TN "SecureApp Pi Backup" /XML "relay\ops\backup-task.xml.sample" /F
```

`relay/ops/backup-task.xml.sample` is the exact definition in use on this PC (hardcodes this machine's
username and repo path — adjust both if setting this up elsewhere). Rebuild it with
`New-ScheduledTaskTrigger`/`Register-ScheduledTask` directly instead if that cmdlet is working normally
on your machine — the XML/`schtasks.exe` route was only needed as a workaround here.

Run it manually any time with `.\relay\ops\backup-from-pi.ps1` from the repo root — the 20h freshness
check applies to manual runs too (it just compares against the newest existing backup, regardless of
what triggered the run), so pass `-MinHoursBetweenRuns 0` to force one on demand right after another.

**Restoring** (2026-10-14, automated — a backup nobody can actually restore from isn't a disaster
recovery plan): `.\relay\ops\restore-to-pi.ps1` stops both containers, copies the newest (or a
chosen `-Timestamp`) backup's `relay-data`/`relay.env`/`wireguard-data` back onto the Pi exactly
where `backup-from-pi.ps1` pulled them from, and starts both containers back up. Asks for a typed
`RESTORE` confirmation first (it's destructive — overwrites whatever's currently on the Pi); pass
`-Force` to skip that for a scripted/unattended run. On a Pi that's been fully replaced (new SD
card/hardware, not just stopped containers), do the "one-time Pi setup" steps above FIRST (clone
the repo, install the deploy watcher) — this script only restores data onto an already-set-up relay,
it doesn't provision Docker/git/wg-easy from nothing.

**Still not covered by this backup** (accepted gaps, not yet addressed):
- The SSH private key this PC uses to reach the Pi at all (`C:\Users\dvora\.ssh\secureapp_pi_ed25519`)
  — only exists here; losing THIS PC means losing remote Pi access (so also the ability to run
  `restore-to-pi.ps1` itself) until physically at the Pi itself, or until that key is recovered from
  wherever else it was kept. If the real goal is "relay survives losing the Pi", this key — not just
  the Pi-side data — is the piece actually worth keeping a copy of somewhere separate (e.g. a
  password manager), since every other piece already has its own backup here.
- Each individual device's own local encrypted vault (chat history, E2EE keys) — deliberately never
  backed up anywhere centrally; that's the point of E2EE, not a gap.
