# One-shot onboarding for a brand-new/work Windows PC (2026-10-07, user's own ask after repeatedly
# fighting the WireGuard GUI's "Import tunnel from file" dialog on "PC v praci" - see
# KNOWN_ISSUES.md / SettingsViewModel.Updates.cs's DownloadWireGuardConfigAsync for the exact
# tunnel-name validation rule this script's sanitizer matches). Idempotent - re-running it on a PC
# that already has everything just confirms that and exits cleanly; safe to re-run after a failed
# step instead of needing to undo anything first.
#
# What it does, in order:
#   1. Self-elevates (both the WireGuard installer and /installtunnelservice need admin rights).
#   2. WireGuard client: checks for C:\Program Files\WireGuard\wireguard.exe; if missing, downloads
#      the official installer and runs it.
#   3. SecureApp itself: checks for <InstallDir>\SecureApp.Presentation.exe; if missing, downloads
#      the relay's /download/windows portable zip and extracts it there. No installer needed - it's
#      a self-contained portable build, no admin rights required for this part (see Program.cs's own
#      remarks above the /download/windows route).
#   4. Gets a WireGuard .conf one of two ways, then installs it as a Windows service tunnel directly
#      via the documented `wireguard /installtunnelservice <path>` command - this bypasses the GUI's
#      "Import tunnel" dialog entirely, so the interactive tunnel-name validation that kept rejecting
#      names with spaces never even runs:
#        - -Code (2026-10-07, the real point of this script for a non-admin colleague): redeems a
#          short-lived, one-time pickup code at the relay's PUBLIC /onboarding/pickup/{code} - no
#          admin secret involved at all, not here and not anywhere the colleague can see. An admin
#          mints this code from SecureApp's own Settings → WireGuard card (it shows up right next to
#          the existing QR/.conf download once a client is created) and just tells/sends it to the
#          colleague - no file to transfer, nothing to explain beyond "run this and type this code".
#        - -ConfigPath: the older path, for a .conf file already on disk (e.g. the admin's own use).
#      Either way the resulting filename is re-sanitized against WireGuard's own tunnel-name charset
#      (the same one SettingsViewModel.Updates.cs's DownloadWireGuardConfigAsync already uses) as
#      defense-in-depth, since a member name can contain spaces or other characters WireGuard itself
#      rejects for a tunnel name even though they're perfectly valid in a filename or a person's name.
#
# Verified against WireGuard's own documentation (git.zx2c4.com/wireguard-windows, checked 2026-10-07)
# before writing this, not guessed:
#   - Installer URL: https://download.wireguard.com/windows-client/wireguard-installer.exe
#   - `wireguard /installtunnelservice <path>` is real, documented in docs/enterprise.md, and creates
#     a Windows service named WireGuardTunnel$<name-without-extension>.
#   - The installer .exe itself has NO documented silent/unattended flag - only the raw MSI (via
#     msiexec directly) supports that, per the same doc. This script does NOT claim one; expect one
#     UAC elevation prompt plus the installer's own short, mostly-automatic progress UI.

param(
    [string]$RelayBaseUrl = "http://192.168.50.8:8080",
    [string]$InstallDir = "C:\SecureApp",
    [string]$ConfigPath = "",
    [string]$Code = ""
)

$ErrorActionPreference = "Stop"

# --- Self-elevate ------------------------------------------------------------
$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal $currentUser).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Output "Restartuji se s právy správce (instalace WireGuard klienta / tunelu to vyžaduje)..."
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    foreach ($key in $PSBoundParameters.Keys) {
        $argList += "-$key"
        $argList += "`"$($PSBoundParameters[$key])`""
    }
    Start-Process -FilePath "powershell.exe" -ArgumentList $argList -Verb RunAs -Wait
    exit $LASTEXITCODE
}

# --- 1. WireGuard client -------------------------------------------------------
$wireguardExe = "C:\Program Files\WireGuard\wireguard.exe"
if (Test-Path $wireguardExe) {
    Write-Output "WireGuard klient: již nainstalován ($wireguardExe)."
}
else {
    Write-Output "WireGuard klient chybí - stahuji instalátor..."
    $installerPath = Join-Path $env:TEMP "wireguard-installer.exe"
    Invoke-WebRequest -Uri "https://download.wireguard.com/windows-client/wireguard-installer.exe" -OutFile $installerPath
    Write-Output "Spouštím instalátor - může se objevit krátký instalační dialog, WireGuard pro tenhle .exe nedokumentuje plně tichou instalaci..."
    Start-Process -FilePath $installerPath -Wait
    Remove-Item $installerPath -ErrorAction SilentlyContinue
    if (Test-Path $wireguardExe) {
        Write-Output "WireGuard klient: nainstalován."
    }
    else {
        Write-Error "WireGuard klient se nenainstaloval (očekávaný soubor $wireguardExe chybí) - zkontrolujte instalaci ručně."
    }
}

# --- 2. SecureApp portable ------------------------------------------------------
$secureAppExe = Join-Path $InstallDir "SecureApp.Presentation.exe"
if (Test-Path $secureAppExe) {
    Write-Output "SecureApp: již nainstalována ($secureAppExe)."
}
else {
    Write-Output "SecureApp chybí v $InstallDir - stahuji portable verzi z relay ($RelayBaseUrl/download/windows)..."
    $zipPath = Join-Path $env:TEMP "secureapp-windows-portable.zip"
    Invoke-WebRequest -Uri "$RelayBaseUrl/download/windows" -OutFile $zipPath
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
    Remove-Item $zipPath -ErrorAction SilentlyContinue
    if (Test-Path $secureAppExe) {
        Write-Output "SecureApp: nainstalována do $InstallDir."
    }
    else {
        Write-Error "Rozbalení proběhlo, ale $secureAppExe nebyl nalezen - zkontrolujte obsah $InstallDir ručně (struktura ZIPu se možná změnila)."
    }
}

# --- 3. WireGuard tunnel import (optional) ---------------------------------------
if ($Code) {
    Write-Output "Vyzvedávám WireGuard konfiguraci pomocí kódu..."
    try {
        $pickup = Invoke-RestMethod -Uri "$RelayBaseUrl/onboarding/pickup/$Code" -UseBasicParsing
    }
    catch {
        Write-Error "Kód se nepodařilo vyzvednout: $($_.Exception.Message) (je neplatný, již použitý, nebo vypršel - vyžádejte si nový)."
        $pickup = $null
    }
    if ($pickup) {
        $pickupConfigPath = Join-Path $env:TEMP "$($pickup.memberName -replace '[^a-zA-Z0-9_ -]','_').conf"
        Set-Content -Path $pickupConfigPath -Value $pickup.configText -Encoding ascii
        $ConfigPath = $pickupConfigPath
        Write-Output "Konfigurace vyzvednuta pro '$($pickup.memberName)'."
    }
}

if ($ConfigPath) {
    if (-not (Test-Path $ConfigPath)) {
        Write-Error "ConfigPath '$ConfigPath' neexistuje."
    }
    elseif (-not (Test-Path $wireguardExe)) {
        Write-Error "WireGuard klient není dostupný, tunel nelze nainstalovat."
    }
    else {
        # Same WireGuard-safe charset the app itself now sanitizes against (see
        # SettingsViewModel.Updates.cs's DownloadWireGuardConfigAsync) - defense-in-depth in case
        # ConfigPath came from an older build or was renamed by hand after downloading.
        $baseName = [System.IO.Path]::GetFileNameWithoutExtension($ConfigPath)
        $safeName = [System.Text.RegularExpressions.Regex]::Replace($baseName, '[^a-zA-Z0-9_=+.\-]', '_')
        if ($safeName.Length -gt 32) { $safeName = $safeName.Substring(0, 32) }
        if ([string]::IsNullOrWhiteSpace($safeName)) { $safeName = "wireguard" }

        $safeConfigPath = $ConfigPath
        if ($safeName -ne $baseName) {
            $safeConfigPath = Join-Path (Split-Path $ConfigPath -Parent) "$safeName.conf"
            Copy-Item -Path $ConfigPath -Destination $safeConfigPath -Force
            Write-Output "Název tunelu upraven na platný: '$baseName' -> '$safeName'."
        }

        Write-Output "Instaluji tunel jako Windows službu (bez GUI dialogu, takže se validace názvu v dialogu vůbec nespustí)..."
        & $wireguardExe /installtunnelservice $safeConfigPath
        Write-Output "Hotovo - tunel '$safeName' by měl běžet jako služba WireGuardTunnel`$$safeName (zkontrolujte ve WireGuard klientovi nebo `"Get-Service WireGuardTunnel`$$safeName`")."
    }
}
else {
    Write-Output "Žádný -Code ani -ConfigPath nezadán - krok s instalací tunelu vynechán."
}

Write-Output ""
Write-Output "=== Onboarding hotovo ==="
