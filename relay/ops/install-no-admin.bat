@echo off
rem Self-fetching double-click installer for a PC with NO admin rights (2026-11-19, user's own ask
rem for "PC v praci"). Same self-fetch trick as new-pc-onboarding.bat: downloads the real logic
rem fresh from the relay's generic /download/ops/{name} route every run, so this one already-
rem distributed file stays current forever - any future fix to install-no-admin.ps1 just needs
rem re-uploading to the relay (see relay/ops/README.md), never a new copy of this launcher.
rem
rem Unlike new-pc-onboarding.bat, this NEVER asks for admin/UAC - that's the whole point of this
rem script (see install-no-admin.ps1's own header for why/how each piece avoids it).
setlocal enabledelayedexpansion

set "RELAY=https://212.111.15.246"
set "PS1_TEMP=%TEMP%\install-no-admin.ps1"

echo Stahuji nejnovejsi instalacni skript z relay (%RELAY%)...
powershell -NoProfile -Command "try { Invoke-WebRequest -Uri '%RELAY%/download/ops/install-no-admin.ps1' -OutFile '%PS1_TEMP%' -UseBasicParsing } catch { Write-Output ('STAHOVANI SE NEZDARILO: ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 (
    echo.
    echo Nepodarilo se stahnout instalacni skript z relay %RELAY%.
    echo Zkontrolujte pripojeni k internetu a zkuste to znovu.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1_TEMP%" -Launch

echo.
echo Hotovo - toto okno muzete zavrit.
pause
