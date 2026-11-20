@echo off
rem Self-fetching double-click installer (2026-10-07, user's own ask: wanted a non-admin colleague
rem to self-onboard a brand-new PC with ONLY this one file - no .conf to transfer, no admin secret,
rem nothing else to explain beyond "run this and type the code I give you"). Downloads the real
rem onboarding logic fresh from the relay every run, so this one already-distributed file stays
rem current forever - any future fix to new-pc-onboarding.ps1 just needs re-uploading to the relay
rem (see relay/ops/README.md), never a new copy of this launcher.
setlocal enabledelayedexpansion

rem Public endpoint (2026-11-20, was "http://192.168.50.8:8080" - LAN-only, unreachable from exactly
rem the brand-new/work PC this script targets, see new-pc-onboarding.ps1's own header). Relay uses a
rem self-signed cert (pinned thumbprint, see install-no-admin.ps1's Ensure-RelayTrust - keep this
rem value in sync with that file); plain Invoke-WebRequest fails this very first fetch with
rem "Nadrizene pripojeni bylo uzavreno..." without it.
set "RELAY=https://212.111.15.246"
set "PS1_TEMP=%TEMP%\new-pc-onboarding.ps1"

echo Stahuji nejnovejsi instalacni skript z relay (%RELAY%)...
powershell -NoProfile -Command "try { Add-Type -TypeDefinition 'using System;using System.Net.Security;using System.Security.Cryptography.X509Certificates;public static class RelayCertPin0{public static string Pin;public static bool Validate(object s,X509Certificate c,X509Chain ch,SslPolicyErrors e){if(e==SslPolicyErrors.None)return true;if(c==null)return false;var c2=new X509Certificate2(c);return string.Equals(c2.Thumbprint,Pin,StringComparison.OrdinalIgnoreCase);}}'; [RelayCertPin0]::Pin='1859C067B270FDC40B9924E08757BC0380108F47'; [System.Net.ServicePointManager]::SecurityProtocol=[System.Net.SecurityProtocolType]::Tls12; [System.Net.ServicePointManager]::ServerCertificateValidationCallback=[Delegate]::CreateDelegate([System.Net.Security.RemoteCertificateValidationCallback],[RelayCertPin0],'Validate'); Invoke-WebRequest -Uri '%RELAY%/download/onboarding.ps1' -OutFile '%PS1_TEMP%' -UseBasicParsing } catch { Write-Output ('STAHOVANI SE NEZDARILO: ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 (
    echo.
    echo Nepodarilo se stahnout instalacni skript z relay %RELAY%.
    echo Zkontrolujte pripojeni k siti/VPN a zkuste to znovu.
    pause
    exit /b 1
)

rem Primary path: a short pickup code the admin told/sent you (SecureApp Settings -> WireGuard
rem card shows it right after creating a new member's access). Nothing else needed - no file,
rem no password. Pass it as an argument to skip the prompt (useful if you're scripting this).
set "CODE=%~1"
if "%CODE%"=="" (
    set /p "CODE=Zadejte kod od administratora (Enter pro preskoceni, pokud zatim WireGuard tunel nepotrebujete): "
)

rem Fallback for an admin's own direct use: a .conf file already sitting next to this .bat (or
rem dragged onto it) still works, same as before, if no code was given at all.
set "CONF="
if "%CODE%"=="" (
    for /f "delims=" %%f in ('dir "%~dp0*.conf" /b /o-d 2^>nul') do (
        if "!CONF!"=="" set "CONF=%~dp0%%f"
    )
)

if not "%CODE%"=="" (
    echo Pouzivam kod: %CODE%
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1_TEMP%" -Code "%CODE%"
) else if not "%CONF%"=="" (
    echo Pouzivam konfiguraci: %CONF%
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1_TEMP%" -ConfigPath "%CONF%"
) else (
    echo Zadny kod ani .conf soubor - instaluji jen WireGuard klienta a SecureApp, bez tunelu.
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1_TEMP%"
)

echo.
echo Hotovo - toto okno muzete zavrit.
pause
