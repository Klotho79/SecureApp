# No-admin self-healing launcher for a PC with no administrator rights ("PC v praci", 2026-11-19 -
# user's own ask: appka chybi .net atd., chce bat/skript, co si dulezite knihovny obstara sam, bez
# narustu velikosti balicku na relay/Pi SD karte). Reworked same day per user's follow-up ("nebo to
# muze byt soucasti spousteciho souboru, ktery to zkontroluje a kdyz tak stahne potrebne") - this is
# now not a one-time install step but what EVERY launch runs: every check below is a fast, purely
# local Test-Path/Get-AppxPackage call, and network/download only happens on the (first, or any
# future broken-install) run where something is actually missing. So the everyday "already
# installed, just open the app" path stays cheap - no network call, no slow TLS/compile setup at
# all; see Ensure-RelayTrust below, called lazily and only by the two steps that actually need it.
#
# Unlike new-pc-onboarding.ps1 (which self-elevates via UAC for WireGuard + runtime installs), this
# script NEVER elevates - everything lands in the user's own profile, nothing touches Program Files
# or the machine registry hive. It also installs no WireGuard tunnel at all: it targets a PC that
# reaches the relay's PUBLIC endpoint directly (RelayDefaults.PublicEndpoint, wss://212.111.15.246,
# confirmed reachable from outside the LAN/VPN 2026-11-19), so no tunnel is needed for the app to
# register or run.
#
# Nothing here grows the app's own release package or what's stored on the Pi - the Windows zip at
# /download/windows stays exactly as it is today (framework-dependent, SelfContained=false in
# SecureApp.Presentation.csproj). This script fetches the two missing runtime pieces straight from
# their own vendors (Microsoft's dot.net CDN, and the existing relay-hosted WinAppSDK installer)
# into the user's profile instead - see each step below for exactly where each file lands.
#
# What it does, in order (every step checks first and no-ops if already satisfied):
#   0. Copies itself into $InstallDir if not already the current version there, and points the
#      Start Menu shortcut at a tiny launcher that runs THAT local copy on every future start - so
#      a normal day's launch never depends on the relay being reachable at all, only a genuinely
#      broken/first-time install does.
#   1. .NET Desktop Runtime 10 (windowsdesktop): checked under $DotnetDir. If missing, downloads
#      Microsoft's own dotnet-install.ps1 (https://dot.net/v1/dotnet-install.ps1) and runs it with
#      -InstallDir $DotnetDir. This is Microsoft's own documented no-admin install path
#      (learn.microsoft.com/dotnet/core/tools/dotnet-install-script, checked 2026-11-19 - "the
#      dotnet-install scripts perform a non-admin installation"); it just unzips into a folder, no
#      registry writes, no relay/custom-cert involved (dot.net uses an ordinary public CA cert).
#      DOTNET_ROOT is then set to $DotnetDir as a USER (not machine) environment variable - the
#      documented way an apphost finds a non-standard runtime location without admin rights.
#   2. Windows App SDK Runtime 1.7: checked via Get-AppxPackage WITHOUT -AllUsers (current-user
#      scope only - that flag itself needs admin and isn't what's wanted here). If missing, runs the
#      SAME installer new-pc-onboarding.ps1 uses (relay's /download/ops/windowsappruntimeinstall-x64.exe,
#      Microsoft-signed, --quiet) but deliberately WITHOUT elevating. Per Microsoft's deployment guide
#      (learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps, checked 2026-11-19),
#      the installer only attempts the machine-wide path (and only needs admin for that) when it's
#      already running elevated; non-elevated, Add-AppxPackage registers the MSIX packages for the
#      current user only. NOT guaranteed on every PC - some organizations block all non-Store MSIX
#      sideloading by policy. If that's the case here, this step fails cleanly and says exactly that;
#      there is deliberately no silent fallback, since masking it would make a real blocker look like
#      a random app crash later.
#   3. SecureApp itself: downloads the relay's /download/windows portable zip from the PUBLIC
#      endpoint and extracts it to $InstallDir (user profile, not Program Files - no admin needed to
#      write there).
#   4. A Start Menu shortcut (user scope, no admin) pointing at a tiny launcher next to the app,
#      which runs this same script (the LOCAL copy from step 0, not a re-download) then starts
#      SecureApp.Presentation.exe with DOTNET_ROOT set for that process.
#
# Steps 2 and 3 talk to the relay over its self-signed certificate - pinned to the exact SHA-1
# thumbprint captured from the live certificate on 2026-11-19 (Ensure-RelayTrust below; a different
# pin than RelayTls.PinnedPublicKeySha256Base64, since Windows PowerShell 5.1's X509Certificate2 has
# no ExportSubjectPublicKeyInfo - update $script:RelayCertThumbprint if the relay's certificate is
# ever regenerated, see relay/ops/README.md). The pin check itself is a compiled .NET delegate
# (Add-Type), not a plain PowerShell scriptblock - a scriptblock assigned directly to
# ServerCertificateValidationCallback throws "There is no Runspace available to run scripts in this
# thread" once invoked from a non-interactive background thread (confirmed while testing this
# script - every HTTPS call failed with a generic, misleading "connection closed" error).
#
# Re-run safely any time, including automatically on every app launch: every step checks before
# acting, and the whole thing is only as slow as whatever is actually missing.

param(
    [string]$RelayBaseUrl = "https://212.111.15.246",
    [string]$InstallDir = "$env:LOCALAPPDATA\SecureApp\app",
    [string]$DotnetDir = "$env:LOCALAPPDATA\SecureApp\dotnet",
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

function Ensure-RelayTrust {
    if ($script:RelayTrustReady) { return }

    Add-Type -TypeDefinition @"
using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public static class RelayCertPin {
    public static string PinnedThumbprint;
    public static bool Validate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) {
        if (sslPolicyErrors == SslPolicyErrors.None) return true;
        if (certificate == null) return false;
        var cert2 = new X509Certificate2(certificate);
        return string.Equals(cert2.Thumbprint, PinnedThumbprint, StringComparison.OrdinalIgnoreCase);
    }
}
"@
    [RelayCertPin]::PinnedThumbprint = "1859C067B270FDC40B9924E08757BC0380108F47"
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback =
        [Delegate]::CreateDelegate([System.Net.Security.RemoteCertificateValidationCallback], [RelayCertPin], "Validate")
    $script:RelayTrustReady = $true
}

# --- 0. Cache a local copy of this script so future launches need no network just to check -------
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$localCopyPath = Join-Path $InstallDir "install-no-admin.ps1"
if ($PSCommandPath -and (Resolve-Path $PSCommandPath -ErrorAction SilentlyContinue) -ne (Resolve-Path $localCopyPath -ErrorAction SilentlyContinue)) {
    Copy-Item -Path $PSCommandPath -Destination $localCopyPath -Force
}

# --- 1. .NET Desktop Runtime 10 --------------------------------------------------------------------
function Test-DesktopRuntime {
    $sharedDir = Join-Path $DotnetDir "shared\Microsoft.WindowsDesktop.App"
    return (Test-Path $sharedDir) -and (Get-ChildItem $sharedDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "10.*" })
}

if (Test-DesktopRuntime) {
    Write-Output ".NET Desktop Runtime 10: OK."
}
else {
    Write-Output ".NET Desktop Runtime 10 chybí - stahuji instalační skript Microsoftu (dot.net, bez nutnosti admin práv)..."
    $installScript = Join-Path $env:TEMP "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript -UseBasicParsing
    New-Item -ItemType Directory -Force -Path $DotnetDir | Out-Null
    & $installScript -Channel "10.0" -Runtime "windowsdesktop" -InstallDir $DotnetDir -NoPath
    Remove-Item $installScript -ErrorAction SilentlyContinue
    if (Test-DesktopRuntime) {
        Write-Output ".NET Desktop Runtime 10: nainstalován do $DotnetDir."
    }
    else {
        Write-Error ".NET Desktop Runtime se nepodařilo nainstalovat - zkontrolujte výstup výše."
    }
}

if ($env:DOTNET_ROOT -ne $DotnetDir) {
    [Environment]::SetEnvironmentVariable("DOTNET_ROOT", $DotnetDir, "User")
    $env:DOTNET_ROOT = $DotnetDir
}

# --- 2. Windows App SDK Runtime 1.7 ----------------------------------------------------------------
$hasWindowsAppRuntime = [bool](Get-AppxPackage -Name "Microsoft.WindowsAppRuntime.1.7*" -ErrorAction SilentlyContinue)
if ($hasWindowsAppRuntime) {
    Write-Output "Windows App SDK Runtime 1.7: OK."
}
else {
    Write-Output "Windows App SDK Runtime 1.7 chybí - stahuji z relay ($RelayBaseUrl/download/ops/windowsappruntimeinstall-x64.exe)..."
    Ensure-RelayTrust
    $waInstaller = Join-Path $env:TEMP "windowsappruntimeinstall-x64.exe"
    Invoke-WebRequest -Uri "$RelayBaseUrl/download/ops/windowsappruntimeinstall-x64.exe" -OutFile $waInstaller -UseBasicParsing
    Write-Output "Instaluji BEZ práv správce (--quiet) - mělo by se zaregistrovat jen pro tento účet..."
    try {
        Start-Process -FilePath $waInstaller -ArgumentList "--quiet" -Wait
    }
    catch {
        Write-Output "Instalátor nahlásil chybu: $($_.Exception.Message)"
    }
    Remove-Item $waInstaller -ErrorAction SilentlyContinue
    $hasWindowsAppRuntime = [bool](Get-AppxPackage -Name "Microsoft.WindowsAppRuntime.1.7*" -ErrorAction SilentlyContinue)
    if ($hasWindowsAppRuntime) {
        Write-Output "Windows App SDK Runtime 1.7: nainstalován (jen pro tento účet)."
    }
    else {
        Write-Output ""
        Write-Output "!!! Windows App SDK Runtime se bez práv správce nenainstaloval. !!!"
        Write-Output "Tento PC pravděpodobně blokuje firemní politikou i instalaci MSIX balíčku jen pro"
        Write-Output "jednoho uživatele (sideloading). Appka bez tohoto kroku na Windows neběží. Buď:"
        Write-Output "  - požádejte IT o jednorázové spuštění TOHOTO kroku s právy správce, nebo"
        Write-Output "  - domluvte se, ať se appka pro Windows přebuildí s vestavěným Windows App SDK"
        Write-Output "    (o něco větší balíček, ale bez téhle závislosti úplně)."
    }
}

# --- 3. SecureApp samo -------------------------------------------------------------------------
$secureAppExe = Join-Path $InstallDir "SecureApp.Presentation.exe"
if (Test-Path $secureAppExe) {
    Write-Output "SecureApp: OK ($secureAppExe)."
}
else {
    Write-Output "Stahuji portable verzi z $RelayBaseUrl/download/windows..."
    Ensure-RelayTrust
    $zipPath = Join-Path $env:TEMP "secureapp-windows-portable.zip"
    Invoke-WebRequest -Uri "$RelayBaseUrl/download/windows" -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
    Remove-Item $zipPath -ErrorAction SilentlyContinue
    if (Test-Path $secureAppExe) {
        Write-Output "SecureApp: nainstalována do $InstallDir."
    }
    else {
        Write-Error "Rozbalení proběhlo, ale $secureAppExe nebyl nalezen - zkontrolujte obsah $InstallDir (struktura ZIPu se možná změnila)."
    }
}

# --- 4. Start Menu zástupce (jednou - ukazuje na launcher, ten pak pouští TOTO skript při každém start) ---
$launcherPath = Join-Path $InstallDir "Spustit-SecureApp.cmd"
if (-not (Test-Path $launcherPath)) {
    @"
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "$localCopyPath" -InstallDir "$InstallDir" -DotnetDir "$DotnetDir" -RelayBaseUrl "$RelayBaseUrl" -Launch
"@ | Set-Content -Path $launcherPath -Encoding ascii
}

$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuDir "SecureApp.lnk"
if (-not (Test-Path $shortcutPath)) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $launcherPath
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.IconLocation = $secureAppExe
    $shortcut.Save()
    Write-Output "Zástupce vytvořen: $shortcutPath"
}

# --- 5. Spuštění appky (jen když o to launcher/Spustit-SecureApp.cmd požádal) ----------------------
if ($Launch -and (Test-Path $secureAppExe)) {
    Start-Process -FilePath $secureAppExe
}

Write-Output "=== Hotovo ==="
