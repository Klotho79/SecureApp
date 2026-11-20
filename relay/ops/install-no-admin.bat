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
rem Relay uses a self-signed cert (pinned thumbprint, see install-no-admin.ps1's own Ensure-RelayTrust
rem - keep this value in sync with that file). Plain Invoke-WebRequest fails this very first fetch
rem with "Nadrizene pripojeni bylo uzavreno..." (confirmed 2026-11-20) since nothing has told .NET to
rem trust it yet - this inline Add-Type sets up the same pinned validation before the download.

echo Stahuji nejnovejsi instalacni skript z relay (%RELAY%)...
powershell -NoProfile -Command "try { Add-Type -TypeDefinition 'using System;using System.Net.Security;using System.Security.Cryptography.X509Certificates;public static class RelayCertPin0{public static string Pin;public static bool Validate(object s,X509Certificate c,X509Chain ch,SslPolicyErrors e){if(e==SslPolicyErrors.None)return true;if(c==null)return false;var c2=new X509Certificate2(c);return string.Equals(c2.Thumbprint,Pin,StringComparison.OrdinalIgnoreCase);}}'; [RelayCertPin0]::Pin='1859C067B270FDC40B9924E08757BC0380108F47'; [System.Net.ServicePointManager]::SecurityProtocol=[System.Net.SecurityProtocolType]::Tls12; [System.Net.ServicePointManager]::ServerCertificateValidationCallback=[Delegate]::CreateDelegate([System.Net.Security.RemoteCertificateValidationCallback],[RelayCertPin0],'Validate'); Invoke-WebRequest -Uri '%RELAY%/download/ops/install-no-admin.ps1' -OutFile '%PS1_TEMP%' -UseBasicParsing } catch { Write-Output ('STAHOVANI SE NEZDARILO: ' + $_.Exception.Message); exit 1 }"
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
