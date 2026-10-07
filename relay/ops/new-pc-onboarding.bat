@echo off
rem Self-fetching double-click installer (2026-10-07, user's own ask: wanted ONE file that
rem downloads everything it needs itself, not a .bat+.ps1 pair that has to travel together).
rem Downloads the real onboarding logic fresh from the relay every run, so this one already-
rem distributed file stays current forever - any future fix to new-pc-onboarding.ps1 just needs
rem re-uploading to the relay (see relay/ops/README.md), never a new copy of this launcher.
rem Needs network/VPN access to the relay for this one step; everything after that (the actual
rem WireGuard/SecureApp installs) works exactly like the local-pair version did.
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

rem Same local .conf auto-detection as before - looks next to THIS .bat, not next to the
rem downloaded .ps1 (which lands in %TEMP%, unrelated to where the user's own config file is).
set "CONF=%~1"
if "%CONF%"=="" (
    rem %CONF% inside a for loop's own block is expanded once at PARSE time, not per iteration -
    rem it would always see the ORIGINAL empty value and let every file in the loop overwrite the
    rem last one's result. !CONF! (delayed expansion) re-reads it live each iteration, so the
    rem newest (first, since dir is sorted descending) match actually wins.
    for /f "delims=" %%f in ('dir "%~dp0*.conf" /b /o-d 2^>nul') do (
        if "!CONF!"=="" set "CONF=%~dp0%%f"
    )
)

if "%CONF%"=="" (
    echo Zadny .conf soubor nenalezen vedle tohoto .bat - instaluji jen WireGuard klienta a SecureApp, bez tunelu.
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1_TEMP%"
) else (
    echo Pouzivam konfiguraci: %CONF%
    powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1_TEMP%" -ConfigPath "%CONF%"
)

echo.
echo Hotovo - toto okno muzete zavrit.
pause
