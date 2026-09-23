# Pulls a full backup of the Pi's SecureApp infrastructure to this PC (2026-09-23, user's own ask:
# make sure admin access / community data survives losing a device, a disk, or the Pi itself).
# Everything here lives ONLY on the Pi otherwise - a disk failure there with no copy elsewhere would
# lose it permanently, unlike the app's own source code (already in two git remotes) or the release
# keystore (already backed up in three places, see keystore/README.md).
#
# What's pulled, and why each one matters:
#   - relay/SecureApp.Relay/data/  - relay.db3 (every registered device, chat session mapping, invite
#     history, directory entries), library-files/ (actual shared-library documents), downloads/ (the
#     hosted APK + version manifest - cheap to rebuild, included anyway since it's small).
#   - relay/SecureApp.Relay/.env   - SECUREAPP_RELAY_ADMIN_SECRET + SECUREAPP_WGEASY_URL/PASSWORD.
#     Without a copy of this, losing the Pi means these specific secret VALUES are gone even though
#     the app/relay code itself would still run fine with freshly-generated new ones.
#   - wireguard/data/               - wg-easy's own state (every WireGuard peer's keys/config). Losing
#     this breaks every already-paired device's VPN access, not just new ones.
#
# Retention: keeps the last RetentionCount timestamped backups, deletes older ones automatically so
# this doesn't grow unbounded. Run manually once, or register as a daily Windows Scheduled Task (see
# relay/ops/README.md for the one-time setup command).

param(
    [string]$BackupRoot = "C:\Users\dvora\SecureApp-Backups",
    [int]$RetentionCount = 14,
    [string]$SshConfig = "C:\Users\dvora\.ssh\config",
    [string]$PiHost = "secureapp-pi",
    [int]$MinHoursBetweenRuns = 20
)

$ErrorActionPreference = "Stop"

# This PC isn't reliably on at the scheduled 3 AM time (2026-09-23, user's own real observation) -
# the task is ALSO registered to fire at every logon as a second trigger, so a missed 3 AM run still
# gets covered the next time this PC is actually used. That alone would mean multiple redundant runs
# on any day with several logons, so skip if the newest existing backup is still fresh enough.
if (Test-Path $BackupRoot) {
    $newest = Get-ChildItem -Path $BackupRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($newest -and $newest.Name -match '^\d{4}-\d{2}-\d{2}_\d{6}$') {
        $newestTime = [DateTime]::ParseExact($newest.Name, "yyyy-MM-dd_HHmmss", $null)
        $hoursSince = (Get-Date) - $newestTime
        if ($hoursSince.TotalHours -lt $MinHoursBetweenRuns) {
            Write-Output "Skipping - last backup ($($newest.Name)) is only $([Math]::Round($hoursSince.TotalHours,1))h old (< $MinHoursBetweenRuns h)."
            exit 0
        }
    }
}

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$dest = Join-Path $BackupRoot $timestamp
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Write-Output "Backing up to $dest ..."

# scp -r pulls whole directories; each one lands under its own subfolder in $dest so a restore knows
# exactly where each piece came from.
scp -F $SshConfig -r "${PiHost}:~/SecureApp/relay/SecureApp.Relay/data" "$dest\relay-data"
scp -F $SshConfig "${PiHost}:~/SecureApp/relay/SecureApp.Relay/.env" "$dest\relay.env"
scp -F $SshConfig -r "${PiHost}:~/wireguard/data" "$dest\wireguard-data"

if ($LASTEXITCODE -ne 0) {
    Write-Error "One or more scp transfers failed - see output above. Leaving the partial backup in place for inspection rather than deleting it."
    exit 1
}

Write-Output "Backup complete: $dest"

# Retention - newest first, delete anything past RetentionCount.
$allBackups = Get-ChildItem -Path $BackupRoot -Directory | Sort-Object Name -Descending
if ($allBackups.Count -gt $RetentionCount) {
    $toDelete = $allBackups | Select-Object -Skip $RetentionCount
    foreach ($old in $toDelete) {
        Write-Output "Pruning old backup: $($old.Name)"
        Remove-Item -Recurse -Force $old.FullName
    }
}
