using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Vestavěný tunel na Windows (2026-11-01, VPN_EMBED_PLAN.md) — práce s oficiálním WireGuard klientem:
/// zjištění stavu (bez práv správce) a jednorázový zvýšený krok, který klienta případně doinstaluje
/// a zaregistruje tunel jako Windows službu <c>WireGuardTunnel$SecureApp</c>.
/// </summary>
/// <remarks>
/// Proč oficiální klient (MSI + <c>wireguard /installtunnelservice</c>) a ne vlastní <c>wireguard-nt</c>:
/// wireguard-nt sám neřeší IP adresu, trasy ani DNS a README projektu pro vestavění do aplikace
/// doporučuje právě službu WireGuard klienta. Služba se spouští při startu Windows (ještě před
/// přihlášením) a práva správce chce jen instalace/změna, ne běh — stejná cesta, kterou už používá
/// <c>relay/ops/new-pc-onboarding.ps1</c>.
///
/// Zdroj komponenty je výhradně <c>download.wireguard.com</c>, NIKDY relay (explicitní požadavek
/// uživatele). Soubor se ověřuje dvakrát: SHA-256 připnutý tady v kódu (verze 1.1.1, ověřeno
/// 2026-11-01) a Authenticode podpis vydavatele "WireGuard LLC". Přechod na novější verzi tedy
/// vyžaduje nové vydání appky s novým hashem — záměrně, aby kompromitace serveru nemohla podstrčit
/// jiný instalátor.
///
/// 2026-11-02 oprava prvního ostrého pokusu na vývojovém PC: <c>/installtunnelservice</c> službě jen
/// zapíše CESTU k configu (služba si ho čte při každém startu), takže config nesmí být dočasný soubor,
/// který skript po instalaci maže — služba by po restartu Windows neměla co načíst. Config tedy žije
/// trvale v <see cref="ConfigDirectory"/> (ProgramData, jen SYSTEM a správci, obsahuje privátní klíč)
/// a přechodná kopie v profilu uživatele se maže. Selhání zvýšeného kroku navíc zapíše příčinu
/// (kód + konec logu WireGuardu, který jde číst jen jako správce) do <see cref="DiagnosticsPath"/>
/// a nic napůl nainstalovaného nezanechá.
/// </remarks>
public static class WireGuardWindowsSetup
{
    public const string TunnelName = "SecureApp";
    public const string ServiceName = "WireGuardTunnel$" + TunnelName;

    private const string MsiVersion = "1.1.1";
    private const string DownloadBase = "https://download.wireguard.com/windows-client/";
    private const string Sha256Amd64 = "7BFED60AD61B785C914B38B61555A975488E1D3EC472DBFB2FCDF498FCA75242";
    private const string Sha256Arm64 = "8336335C738D4AFF040FA9D11B752DA65A80B6F41D3262D6BA5E82F97EBD1BD4";

    private const int ErrorCancelledByUser = 1223;

    public static string ClientExePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe");

    public static bool IsClientInstalled => File.Exists(ClientExePath);

    /// <summary>Složka s přechodným configem — profil přihlášeného uživatele, ne sdílená složka.</summary>
    public static string StagingDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecureApp", "tunnel");

    /// <summary>Trvalé místo configu, na který ukazuje služba (zvýšený skript ho vytváří jen pro SYSTEM a správce).</summary>
    public static string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SecureApp", "tunnel");

    /// <summary>Příčina neúspěchu zapsaná zvýšeným skriptem (bez klíčů) — čte ji appka a dává do logu.</summary>
    private static string DiagnosticsPath => Path.Combine(StagingDirectory, "setup-diagnostics.txt");

    /// <summary>Stav služby tunelu: <c>null</c> = služba neexistuje, jinak true = běží. Nepotřebuje práva správce.</summary>
    public static async Task<bool?> QueryServiceRunningAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("sc.exe", $"query {ServiceName}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(psi);
        if (process is null) return null;
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0) return null; // 1060 = služba neexistuje

        // Název stavu ("RUNNING") se nelokalizuje, text před ním ano - proto hledám jen ten.
        var match = Regex.Match(output, @"\d+\s+(STOPPED|START_PENDING|STOP_PENDING|RUNNING|CONTINUE_PENDING|PAUSE_PENDING|PAUSED)");
        return match.Success && match.Groups[1].Value == "RUNNING";
    }

    /// <summary>
    /// Skutečný stav tunelu právě teď pro Nastavení (bez sítě a bez práv správce): existuje služba, běží a má
    /// aktivní síťové rozhraní. Neříká, že handshake s relay proběhl — ten vidí jen správce na relay.
    /// </summary>
    public static async Task<string> DescribeAsync(CancellationToken ct = default)
    {
        var running = await QueryServiceRunningAsync(ct);
        if (running is null)
            return IsClientInstalled
                ? "Tunel: zatím není nastaven (služba neexistuje) — nastaví se po potvrzení správce"
                : "Tunel: zatím není nastaven — chybí komponenta WireGuard, doinstaluje se po potvrzení správce";
        if (running == false)
            return "Tunel: služba existuje, ale neběží";

        var interfaceUp = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Any(n => n.Name == TunnelName && n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
        return interfaceUp
            ? "Tunel: běží (služba i síťové rozhraní jsou aktivní)"
            : "Tunel: služba běží, ale síťové rozhraní není aktivní";
    }

    /// <summary>
    /// Zapíše config do přechodného souboru a spustí zvýšený krok (UAC). Vrací <see cref="SetupResult"/>;
    /// přechodný config je po skončení smazaný vždy (zvýšený skript ho maže ve <c>finally</c>, tohle je pojistka).
    /// </summary>
    public static async Task<SetupResult> InstallAsync(string configText, CancellationToken ct = default)
    {
        Directory.CreateDirectory(StagingDirectory);
        var configPath = Path.Combine(StagingDirectory, TunnelName + ".conf");
        try { File.Delete(DiagnosticsPath); } catch { /* starý výpis není důležitý */ }
        await File.WriteAllTextAsync(configPath, configText, new UTF8Encoding(false), ct);
        try
        {
            var script = BuildElevatedScript(configPath);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(psi);
            if (process is null) return SetupResult.Failed;
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
                LogFailure(process.ExitCode);
            return process.ExitCode switch
            {
                0 => SetupResult.Ok,
                10 or 11 => SetupResult.UntrustedDownload,
                _ => SetupResult.Failed,
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledByUser)
        {
            return SetupResult.Declined;
        }
        finally
        {
            try { File.Delete(configPath); } catch { /* skript ho maže taky */ }
        }
    }

    public enum SetupResult { Ok, Declined, UntrustedDownload, Failed }

    private static void LogFailure(int exitCode)
    {
        string detail;
        try
        {
            detail = File.Exists(DiagnosticsPath) ? File.ReadAllText(DiagnosticsPath) : "(skript nezapsal příčinu)";
            File.Delete(DiagnosticsPath);
        }
        catch (Exception ex)
        {
            detail = "(výpis příčiny nejde přečíst: " + ex.Message + ")";
        }

        // Jeden řádek logu: žádné tabulátory ani zalomení, a rozumná délka.
        detail = Regex.Replace(detail, @"\s*[\r\n]+\s*", " | ").Replace('\t', ' ');
        if (detail.Length > 1800) detail = detail[^1800..];
        AppLog.Event("wireguard.windows.setup-failed", ("exit", exitCode), ("detail", detail));
    }

    private static string BuildElevatedScript(string stagePath)
    {
        var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        var file = $"wireguard-{(arm ? "arm64" : "amd64")}-{MsiVersion}.msi";

        // Žádné interpolace: skript je konstantní text a dosazují se jen tyto hodnoty - cesty
        // pocházejí z profilu uživatele / ProgramData, hash a URL z konstant výše, nikdy ze vstupu zvenčí.
        // Kódy ukončení čte InstallAsync: 10 = hash nesedí, 11 = podpis nesedí, ostatní = chyba
        // (12 msi, 13 chybí wireguard.exe, 14 /installtunnelservice selhal, 15 služba neběží, 20 výjimka).
        return """
            $ErrorActionPreference = 'Stop'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $stage = '@@STAGE@@'
            $dir = '@@DIR@@'
            $conf = Join-Path $dir '@@TUNNEL@@.conf'
            $diag = '@@DIAG@@'
            $wg = Join-Path $env:ProgramFiles 'WireGuard\wireguard.exe'
            $svc = '@@SERVICE@@'
            $ok = $false
            function Fail($code, $text) { Set-Content -Path $diag -Value $text -Encoding UTF8; exit $code }
            try {
                if (-not (Test-Path $wg)) {
                    $msi = Join-Path $env:TEMP '@@FILE@@'
                    Invoke-WebRequest -Uri '@@URL@@' -OutFile $msi -UseBasicParsing
                    if ((Get-FileHash $msi -Algorithm SHA256).Hash -ne '@@SHA@@') { Remove-Item $msi -Force; Fail 10 'hash instalatoru nesedi' }
                    $sig = Get-AuthenticodeSignature $msi
                    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike 'CN=WireGuard LLC,*') { Remove-Item $msi -Force; Fail 11 'podpis instalatoru nesedi' }
                    $p = Start-Process -FilePath msiexec.exe -ArgumentList '/i', ('"' + $msi + '"'), '/qn', '/norestart', 'DO_NOT_LAUNCH=1' -Wait -PassThru
                    Remove-Item $msi -Force
                    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { Fail 12 ('msiexec exit=' + $p.ExitCode) }
                }
                if (-not (Test-Path $wg)) { Fail 13 'wireguard.exe po instalaci chybi' }

                if ((Test-Path $dir) -and ((Get-Acl $dir).Owner -notmatch 'Administrators|SYSTEM|Správci')) { Remove-Item $dir -Recurse -Force }
                New-Item -ItemType Directory -Path $dir -Force | Out-Null
                & icacls.exe $dir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
                Copy-Item -Path $stage -Destination $conf -Force

                if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
                    & $wg /uninstalltunnelservice '@@TUNNEL@@'
                    for ($i = 0; $i -lt 30 -and (Get-Service -Name $svc -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
                }
                & $wg /installtunnelservice $conf
                $code = $LASTEXITCODE
                if ($code -ne 0) { Fail 14 ('installtunnelservice exit=' + $code) }
                for ($i = 0; $i -lt 30; $i++) {
                    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
                    if ($s -and $s.Status -eq 'Running') { $ok = $true; exit 0 }
                    Start-Sleep -Milliseconds 500
                }
                Fail 15 'sluzba po 15 s nebezi'
            }
            catch { Fail 20 $_.Exception.Message }
            finally {
                Remove-Item $stage -Force -ErrorAction SilentlyContinue
                if (-not $ok) {
                    # Nic napůl nainstalovaného: příčina do výpisu, rozbitá služba pryč a s ní i config s klíčem.
                    try {
                        $ErrorActionPreference = 'Continue'
                        if (Test-Path $wg) {
                            $tail = (& $wg /dumplog 2>&1 | ForEach-Object { "$_" } | Select-Object -Last 40) -join "`n"
                            Add-Content -Path $diag -Value ("`n--- dumplog ---`n" + $tail) -Encoding UTF8
                            if (Get-Service -Name $svc -ErrorAction SilentlyContinue) { & $wg /uninstalltunnelservice '@@TUNNEL@@' | Out-Null }
                        }
                        Remove-Item $conf -Force -ErrorAction SilentlyContinue
                    } catch { }
                }
            }
            """
            .Replace("@@STAGE@@", stagePath.Replace("'", "''"))
            .Replace("@@DIR@@", ConfigDirectory.Replace("'", "''"))
            .Replace("@@DIAG@@", DiagnosticsPath.Replace("'", "''"))
            .Replace("@@SERVICE@@", ServiceName)
            .Replace("@@TUNNEL@@", TunnelName)
            .Replace("@@FILE@@", file)
            .Replace("@@URL@@", DownloadBase + file)
            .Replace("@@SHA@@", arm ? Sha256Arm64 : Sha256Amd64);
    }
}
