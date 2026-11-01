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
    /// Zapíše config do přechodného souboru a spustí zvýšený krok (UAC). Vrací <see cref="SetupResult"/>;
    /// přechodný config je po skončení smazaný vždy (zvýšený skript ho maže ve <c>finally</c>, tohle je pojistka).
    /// </summary>
    public static async Task<SetupResult> InstallAsync(string configText, CancellationToken ct = default)
    {
        Directory.CreateDirectory(StagingDirectory);
        var configPath = Path.Combine(StagingDirectory, TunnelName + ".conf");
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

    private static string BuildElevatedScript(string configPath)
    {
        var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        var file = $"wireguard-{(arm ? "arm64" : "amd64")}-{MsiVersion}.msi";

        // Žádné interpolace: skript je konstantní text a dosazují se jen tyto čtyři hodnoty - cesty
        // pocházejí z profilu uživatele, hash a URL z konstant výše, nikdy ze vstupu zvenčí.
        // Kódy ukončení čte InstallAsync: 10 = hash nesedí, 11 = podpis nesedí, ostatní = chyba.
        return """
            $ErrorActionPreference = 'Stop'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $conf = '@@CONF@@'
            $wg = Join-Path $env:ProgramFiles 'WireGuard\wireguard.exe'
            $svc = '@@SERVICE@@'
            try {
                if (-not (Test-Path $wg)) {
                    $msi = Join-Path $env:TEMP '@@FILE@@'
                    Invoke-WebRequest -Uri '@@URL@@' -OutFile $msi -UseBasicParsing
                    if ((Get-FileHash $msi -Algorithm SHA256).Hash -ne '@@SHA@@') { Remove-Item $msi -Force; exit 10 }
                    $sig = Get-AuthenticodeSignature $msi
                    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notlike 'CN=WireGuard LLC,*') { Remove-Item $msi -Force; exit 11 }
                    $p = Start-Process -FilePath msiexec.exe -ArgumentList '/i', ('"' + $msi + '"'), '/qn', '/norestart', 'DO_NOT_LAUNCH=1' -Wait -PassThru
                    Remove-Item $msi -Force
                    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { exit 12 }
                }
                if (-not (Test-Path $wg)) { exit 13 }

                if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
                    & $wg /uninstalltunnelservice '@@TUNNEL@@'
                    for ($i = 0; $i -lt 30 -and (Get-Service -Name $svc -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 500 }
                }
                & $wg /installtunnelservice $conf
                if ($LASTEXITCODE -ne 0) { exit 14 }
                for ($i = 0; $i -lt 30; $i++) {
                    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
                    if ($s -and $s.Status -eq 'Running') { exit 0 }
                    Start-Sleep -Milliseconds 500
                }
                exit 15
            }
            catch { exit 20 }
            finally { Remove-Item $conf -Force -ErrorAction SilentlyContinue }
            """
            .Replace("@@CONF@@", configPath.Replace("'", "''"))
            .Replace("@@SERVICE@@", ServiceName)
            .Replace("@@TUNNEL@@", TunnelName)
            .Replace("@@FILE@@", file)
            .Replace("@@URL@@", DownloadBase + file)
            .Replace("@@SHA@@", arm ? Sha256Arm64 : Sha256Amd64);
    }
}
