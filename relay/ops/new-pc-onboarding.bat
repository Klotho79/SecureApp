@echo off
rem Double-click launcher for new-pc-onboarding.ps1 (2026-10-07, user's own ask - a .ps1 doesn't
rem run on double-click by default, it opens in a text editor, which is real friction on a brand-new
rem PC with no PowerShell habits). Keep this .bat next to new-pc-onboarding.ps1 (same folder, e.g. on
rem a USB stick) - %~dp0 resolves to wherever THIS file actually is, so it works copied anywhere.
rem
rem No arguments needed for the common case: drop the downloaded .conf file into this same folder
rem (or drag-and-drop it onto this .bat's icon) and just double-click. If more than one .conf sits
rem here, the newest one wins - picks it by file time, not by name, since names vary per member.
setlocal enabledelayedexpansion

set "CONF=%~1"
if "%CONF%"=="" (
    rem %CONF% inside a for loop's own block is expanded once at PARSE time, not per
    rem iteration - it would always see the ORIGINAL empty value and let every file in the
    rem loop overwrite the last one's result. !CONF! (delayed expansion) re-reads it live on
    rem each iteration, so the newest (first, since dir is sorted descending) match actually wins.
    for /f "delims=" %%f in ('dir "%~dp0*.conf" /b /o-d 2^>nul') do (
        if "!CONF!"=="" set "CONF=%~dp0%%f"
    )
)

if "%CONF%"=="" (
    echo Zadny .conf soubor nenalezen vedle tohoto .bat - instaluji jen WireGuard klienta a SecureApp, bez tunelu.
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0new-pc-onboarding.ps1"
) else (
    echo Pouzivam konfiguraci: %CONF%
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0new-pc-onboarding.ps1" -ConfigPath "%CONF%"
)

echo.
echo Hotovo - toto okno muzete zavrit.
pause
