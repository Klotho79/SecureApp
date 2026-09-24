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
