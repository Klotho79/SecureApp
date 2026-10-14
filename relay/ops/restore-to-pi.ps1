# Restores a backup made by backup-from-pi.ps1 back onto the Pi (2026-10-14, user's own ask: "musi
# byt cesta k obnoveni jinak zaloha nema smysl" - a backup nobody has ever run the restore half of
# is not actually a disaster-recovery plan, just a folder of files. This automates the exact manual
# steps relay/ops/README.md's own "Restoring" paragraph already described, so recovering after
# losing the Pi (or just its data/containers) doesn't depend on remembering/retyping them correctly
# under stress.
#
# Preconditions this script does NOT set up (see relay/ops/README.md for all of these):
#   - A Pi reachable over SSH as $PiHost, with Docker + the SecureApp repo already cloned into
#     ~/SecureApp (steps 1-4 in README.md's "one-time Pi setup"). On a REPLACEMENT Pi (new SD card/
#     new hardware), do that first - this script only restores DATA on top of an already-set-up
#     relay, it does not provision Docker/git/wg-easy from nothing.
#   - The SSH private key this PC uses to reach the Pi ($SshConfig) - README.md's own "Still not
#     covered" section already flags this as the one link in the chain that lives ONLY on this PC;
#     if THIS PC is what's gone, you need that key from wherever it was separately kept (or physical/
#     console access to the Pi) before any of this script's remote steps can run at all.
#
# What it does, in order: stops both containers (so nothing is writing to the files underneath this
# copy), scp's the chosen backup's data back onto the Pi in each of the three places
# backup-from-pi.ps1 pulled them FROM, then starts both containers back up.

param(
    [string]$BackupRoot = "C:\Users\dvora\SecureApp-Backups",
    # Which timestamped backup folder to restore - defaults to the newest one found under
    # $BackupRoot. Pass an exact folder name (e.g. "2026-10-13_030000") to restore an older one.
    [string]$Timestamp,
    [string]$SshConfig = "C:\Users\dvora\.ssh\config",
    [string]$PiHost = "secureapp-pi",
    [string]$RelayRemoteDir = "~/SecureApp/relay/SecureApp.Relay",
    [string]$WireGuardRemoteDir = "~/wireguard",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BackupRoot)) {
    throw "Backup root '$BackupRoot' does not exist on this PC - nothing to restore from."
}

if ($Timestamp) {
    $source = Join-Path $BackupRoot $Timestamp
    if (-not (Test-Path $source)) {
        throw "No backup found at '$source'."
    }
} else {
    $newest = Get-ChildItem -Path $BackupRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $newest) {
        throw "No backups found under '$BackupRoot'."
    }
    $source = $newest.FullName
    $Timestamp = $newest.Name
}

Write-Output "Restoring from: $source"
Write-Output "Target Pi:      $PiHost"
Write-Output ""
Write-Output "This OVERWRITES the Pi's current relay database, shared-library files, hosted APK,"
Write-Output ".env secrets, and every WireGuard peer's config with the chosen backup's versions."
Write-Output "Anything created/changed on the Pi AFTER '$Timestamp' that isn't in this backup is lost."

if (-not $Force) {
    $confirmation = Read-Host "Type RESTORE to continue"
    if ($confirmation -ne "RESTORE") {
        Write-Output "Aborted - nothing was touched."
        exit 1
    }
}

$relayDataSrc = Join-Path $source "relay-data"
$envSrc = Join-Path $source "relay.env"
$wgDataSrc = Join-Path $source "wireguard-data"
foreach ($path in @($relayDataSrc, $envSrc, $wgDataSrc)) {
    if (-not (Test-Path $path)) {
        throw "Backup '$Timestamp' is missing expected piece '$path' - refusing a partial restore."
    }
}

function Invoke-PiSsh([string]$Command) {
    ssh -F $SshConfig $PiHost $Command
    if ($LASTEXITCODE -ne 0) {
        throw "SSH command failed (exit $LASTEXITCODE): $Command"
    }
}

Write-Output "Stopping containers on the Pi..."
Invoke-PiSsh "cd $RelayRemoteDir && docker compose down"
Invoke-PiSsh "cd $WireGuardRemoteDir && docker compose down"

Write-Output "Copying relay data back..."
scp -F $SshConfig -r "$relayDataSrc\*" "${PiHost}:$RelayRemoteDir/data/"
if ($LASTEXITCODE -ne 0) { throw "scp of relay-data failed." }

scp -F $SshConfig "$envSrc" "${PiHost}:$RelayRemoteDir/.env"
if ($LASTEXITCODE -ne 0) { throw "scp of relay.env failed." }

Write-Output "Copying WireGuard data back..."
scp -F $SshConfig -r "$wgDataSrc\*" "${PiHost}:$WireGuardRemoteDir/data/"
if ($LASTEXITCODE -ne 0) { throw "scp of wireguard-data failed." }

Write-Output "Starting containers back up..."
Invoke-PiSsh "cd $RelayRemoteDir && docker compose up -d"
Invoke-PiSsh "cd $WireGuardRemoteDir && docker compose up -d"

Write-Output ""
Write-Output "Restore complete. Verify with:"
Write-Output "  curl.exe -s -o NUL -w 'health=%{http_code}`n' http://192.168.50.8:8080/health"
