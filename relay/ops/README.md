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

## Future: fully automatic

Point a systemd timer (or the post-receive hook itself) at the same marker-file trick instead of
waiting for a button tap — nothing about `/admin/deploy` or `deploy.sh` needs to change.

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

**Restoring**: stop the relay/wg-easy containers, copy the relevant `<timestamp>\relay-data\*` back
onto the Pi's `~/SecureApp/relay/SecureApp.Relay/data/`, `<timestamp>\relay.env` onto
`~/SecureApp/relay/SecureApp.Relay/.env`, and `<timestamp>\wireguard-data\*` onto
`~/wireguard/data/`, then `docker compose up -d` both.

**Still not covered by this backup** (accepted gaps, not yet addressed):
- The SSH private key this PC uses to reach the Pi at all (`C:\Users\dvora\.ssh\secureapp_pi_ed25519`)
  — only exists here; losing this PC means losing remote Pi access until physically at the Pi itself.
- Each individual device's own local encrypted vault (chat history, E2EE keys) — deliberately never
  backed up anywhere centrally; that's the point of E2EE, not a gap.
