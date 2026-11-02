using System.Security.Cryptography;
using System.Text;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Infrastructure;

/// <inheritdoc cref="IWireGuardTunnelController"/>
/// <remarks>
/// Windows (2026-11-01): tunel je služba oficiálního WireGuard klienta (<see cref="WireGuardWindowsSetup"/>),
/// takže běží i bez spuštěné appky a po restartu naběhne sám — na rozdíl od Androidu proto není
/// potřeba "start z cache". Appka jen hlídá, aby služba existovala a měla aktuální config z relay.
/// Práva správce (UAC) se žádají jen když je co dělat: chybí klient, služba nebyla vytvořena, nebo
/// admin změnil parametry. Odmítnutí se nezkouší znovu 24 h, neúspěch 6 h, ať appka neotravuje.
/// Zrušení peera adminem (config <c>null</c>) službu NEodstraňuje — server klíč stejně odmítne a
/// odinstalace by chtěla další UAC.
/// </remarks>
public sealed class WireGuardWindowsTunnelController : IWireGuardTunnelController
{
    private const string AppliedHashKey = "wireguard.windows.applied-config-sha256";
    private const string RetryNotBeforeKey = "wireguard.windows.retry-not-before-utc";
    private const string RetryBuildKey = "wireguard.windows.retry-build";

    private static readonly TimeSpan DeclinedRetry = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailedRetry = TimeSpan.FromHours(6);

    private readonly IWireGuardTunnelConfigService _configService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WireGuardWindowsTunnelController(IWireGuardTunnelConfigService configService)
    {
        _configService = configService;
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return; // dialog / instalace už běží
        try
        {
            var configText = await _configService.FetchConfigAsync(ct);
            if (configText is null)
            {
                WireGuardTunnelStatus.Set("Tunel: pro toto zařízení není nastaven");
                return;
            }

            var running = await WireGuardWindowsSetup.QueryServiceRunningAsync(ct);
            var configHash = Hash(configText);
            var upToDate = running == true && Preferences.Default.Get(AppliedHashKey, "") == configHash;
            if (upToDate)
            {
                WireGuardTunnelStatus.Set("Tunel: aktivní");
                return;
            }

            // Odklad platí jen pro verzi appky, která ho nastavila: po aktualizaci (typicky oprava právě té instalace) se zkusí hned.
            if (Preferences.Default.Get(RetryBuildKey, "") != AppInfo.Current.BuildString)
                Preferences.Default.Remove(RetryNotBeforeKey);

            if (DateTimeOffset.UtcNow.UtcTicks < Preferences.Default.Get(RetryNotBeforeKey, 0L))
            {
                WireGuardTunnelStatus.Set("Tunel: čeká na potvrzení instalace");
                return;
            }

            if (!await AskUserAsync(clientMissing: !WireGuardWindowsSetup.IsClientInstalled))
            {
                ScheduleRetry(DeclinedRetry);
                WireGuardTunnelStatus.Set("Tunel: čeká na potvrzení instalace");
                return;
            }

            AppLog.Event("wireguard.windows.setup-start");
            var result = await WireGuardWindowsSetup.InstallAsync(configText, ct);
            AppLog.Event($"wireguard.windows.setup-result={result}");
            switch (result)
            {
                case WireGuardWindowsSetup.SetupResult.Ok:
                    Preferences.Default.Set(AppliedHashKey, configHash);
                    Preferences.Default.Remove(RetryNotBeforeKey);
                    WireGuardTunnelStatus.Set("Tunel: aktivní");
                    break;
                case WireGuardWindowsSetup.SetupResult.Declined:
                    ScheduleRetry(DeclinedRetry);
                    WireGuardTunnelStatus.Set("Tunel: čeká na potvrzení instalace");
                    break;
                case WireGuardWindowsSetup.SetupResult.UntrustedDownload:
                    ScheduleRetry(FailedRetry);
                    WireGuardTunnelStatus.Set("Tunel: stažený instalátor WireGuard neprošel ověřením, nic se nenainstalovalo");
                    break;
                default:
                    ScheduleRetry(FailedRetry);
                    WireGuardTunnelStatus.Set("Tunel: instalace se nezdařila");
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(WireGuardWindowsTunnelController), "tunnel sync failed", ex);
            WireGuardTunnelStatus.Set($"Tunel: chyba ({ex.Message})");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasPendingConfigAsync(CancellationToken ct = default)
    {
        try
        {
            return await _configService.FetchConfigAsync(ct) is not null;
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(WireGuardWindowsTunnelController), "checking for a pending tunnel config failed", ex);
            return false;
        }
    }

    /// <summary>Vrací false i když okno zrovna není k dispozici (další tick to zkusí znovu) - bez okna se UAC nikdy nespouští "naslepo".</summary>
    private static Task<bool> AskUserAsync(bool clientMissing) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (Shell.Current is not { } shell) return false;
        var message = clientMissing
            ? "Pro spojení s relay serverem SecureApp potřebuje komponentu WireGuard. Stáhne se z oficiálního zdroje (download.wireguard.com), ověří se její podpis a nainstaluje se. Windows se zeptá na povolení správce."
            : "Spojení s relay serverem je potřeba nastavit nebo obnovit (změnilo se nastavení nebo tunel neběží). Windows se zeptá na povolení správce.";
        return await shell.DisplayAlertAsync("Připojení k relay", message, "Pokračovat", "Později");
    });

    private static void ScheduleRetry(TimeSpan delay)
    {
        Preferences.Default.Set(RetryNotBeforeKey, DateTimeOffset.UtcNow.Add(delay).UtcTicks);
        Preferences.Default.Set(RetryBuildKey, AppInfo.Current.BuildString);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
