@echo off
rem Self-fetching double-click installer (2026-10-07, user's own ask: wanted a non-admin colleague
rem to self-onboard a brand-new PC with ONLY this one file - no .conf to transfer, no admin secret,
rem nothing else to explain beyond "run this and type the code I give you"). Downloads the real
rem onboarding logic fresh from the relay every run, so this one already-distributed file stays
rem current forever - any future fix to new-pc-onboarding.ps1 just needs re-uploading to the relay
rem (see relay/ops/README.md), never a new copy of this launcher.
setlocal enabledelayedexpansion

set "RELAY=http://192.168.50.8:8080"
set "PS1_TEMP=%TEMP%\new-pc-onboarding.ps1"

echo Stahuji nejnovejsi instalacni skript z relay (%RELAY%)...
powershell -NoProfile -Command "try { Invoke-WebRequest -Uri '%RELAY%/download/onboarding.ps1' -OutFile '%PS1_TEMP%' -UseBasicParsing } catch { Write-Output ('STAHOVANI SE NEZDARILO: ' + $_.Exception.Message); exit 1 }"
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
