# Vestavěný tunel (WireGuard v appce) — plán

Stav: návrh, nic implementováno. Navazuje na `HANDOFF_standalone-24caf3f8_releases-0-1-5-tunnel_2026-10-30.md` (volba mezi "A: veřejné HTTPS bez tunelu" a "B: opravit tunel") — uživatel zvolil třetí cestu: **tunel zůstává, ale uživatel o něm nemá vědět a nic nastavovat.**

## Cíl

Uživatel (Android i Windows) už nikdy neinstaluje samostatnou WireGuard appku, neimportuje `.conf` ani neskenuje QR. SecureApp má tunel vestavěný: po instalaci se sám automaticky nakonfiguruje (parametry stažené z relay), připojuje a udržuje na pozadí. Admin může parametry připojení (AllowedIPs, keepalive, endpoint) měnit přes existující relay rozhraní — appky si změnu sama vyzvednou. Na Windows, pokud chybí potřebná komponenta (driver), update appky na to upozorní a nainstaluje ji **z oficiálních zdrojů WireGuard projektu, ne z vlastního relay**.

## Rozhodnuto v konverzaci (2026-10-31)

- Uživateli nevadí, že tunel existuje — vadí mu, že musí "nastavovat něco, čemu nerozumí". Řešení = schovat tunel do appky, ne ho zrušit (varianta A z handoffu je tím odložena, ne zavržená).
- Admin musí mít i nadále možnost parametry připojení upravovat.
- Windows cesta: instalace chybějících komponent musí být explicitní (upozornění uživateli), zdroj = oficiální WireGuard, ne relay.
- Otázka bezpečnosti byla položena a zodpovězena v konverzaci: embedding bezpečnost nesnižuje, pokud (a) privátní klíč jde do platformního bezpečného úložiště (Android Keystore / Windows DPAPI), ne do `Preferences`/registru v plaintextu, a (b) distribuční kanál konfigurace zůstává tak autentizovaný jako dnes. Bonus navržený v konverzaci (appka si generuje klíč sama, na relay jde jen veřejný klíč) je v plánu jako **fáze 1, doporučená, ne vynucená** — potvrdit s uživatelem před implementací.

## Architektura

**Android** — embedovat oficiální `com.wireguard.android` tunnel knihovnu (GoBackend, JNI na `wireguard-go`; MIT, stejný kód jako oficiální WireGuard app a jako to dělá Mullvad/Tailscale). Vlastní `VpnService` potomek v `Platforms/Android`, appka ho spouští/zastavuje sama, žádná druhá appka. Systémový dialog "SecureApp chce vytvořit VPN připojení" se zobrazí jednou (OS úroveň, nelze obejít).

**Windows** — embedovat `wireguard-nt` (wireguard.dll + driver wireguard.sys), oficiálně distribuovaný WireGuard projektem přímo pro tento účel ("embeddable WireGuard implementation for Windows"). Tenký P/Invoke wrapper v `Platforms/Windows`. Instalace driveru vyžaduje jednorázovou admin elevaci (UAC) — to nelze obejít, je to podepsaný kernelový driver.

**Relay** — zůstává zdroj pravdy pro parametry (endpoint, AllowedIPs, keepalive, veřejný klíč serveru); `TuneWireGuardClientConfig` (už napsaná, nenasazená) se rozšíří o strukturovanou (JSON) odpověď pro appky vedle stávající textové `.conf` (tu si ponechá admin/PC-v-práci onboarding pro zpětnou kompatibilitu).

## Fáze

Pořadí potvrzeno uživatelem (2026-10-31): Android před Windows. Původní fáze 1 (appka generuje klíč, relay registruje jen veřejný klíč) **zrušena 2026-10-31** — wg-easy API to neumí (`POST api/wireguard/client` přijímá jen `name`, klíče generuje vždy server; ověřeno na vlastním kódu i třech nezávislých API wrapperech). Appka dál dostává privátní klíč od serveru jako dnes, přes stávající autentizovaný kanál. Detaily v `CHANGELOG.md` (2026-10-31).

1. **Android: vestavěný tunel.** Binding `com.wireguard.android` (Android binding project nebo přímý JNI na `wireguard-go`), `VpnService` potomek, ukládání privátního klíče/konfigurace v Android Keystore-backed úložišti (ne `Preferences`), auto-config z relay při prvním spuštění (appka zavolá stávající `/admin/wireguard/clients`-style endpoint, dostane config stejně jako dnes, jen appka ho rovnou spotřebuje místo uživatele), periodická re-synchronizace při změně admin parametrů, UI v Nastavení → Síť (stav připojení, bez nutnosti cizí appky).
   - **Hotovo 2026-10-31 (plumbing, nic ještě nevolá appka):** binding projekt `src/SecureApp.WireGuard.Android` v řešení, referencovaný z `SecureApp.Presentation` jen pro Android. `AndroidManifest.xml` má `<service>` pro `GoBackend$VpnService`. `Platforms/Android/WireGuardTunnelManager.cs` — `ITunnel` + `Up(configText)`/`Down()`/`PrepareIntent`. `SupportedOSPlatformVersion` zvednuto 21→24 (knihovna to vyžaduje; bezpečné pro všechna reálná zařízení). Celá appka se zkompilovala (`dotnet build -f net10.0-android -c Debug`, 0 chyb) — jen compile-check, nic nainstalováno na zařízení.
   - **Hotovo 2026-10-31 (relay + app-side fetch, pořád nic nevolá `Up()`):** relay `POST /device/wireguard/config` — appka si sama vyžádá config vlastním device tokenem (`TryGetDeviceAuth`), žádný admin secret/pickup kód; nová tabulka `device_wireguard_peers` páruje zařízení s jeho wg-easy peerem napořád (druhé zavolání = aktuální config téhož peera, ne nový peer pokaždé); zrušení peera adminem → appka dostane 404 a NEDOSTANE náhradní (revoke musí doopravdy zabrat). App-side `IWireGuardTunnelConfigService`/`HttpWireGuardTunnelConfigService` (stejný vzor jako `IDevicePolicyService`), zaregistrováno v DI. `dotnet build` relay i appky (Android + Windows): 0 chyb.
   - **Hotovo 2026-10-31 (fetch→Up/Down propojeno, end-to-end kód kompletní, nic ještě netestováno na zařízení):** `IWireGuardTunnelController`/`WireGuardTunnelController` (Android-only, registrováno jen tam v `MauiProgram.cs`) spojuje `IWireGuardTunnelConfigService.FetchConfigAsync()` s `WireGuardTunnelManager.Up`/`Down` — config `null` (appka nezaregistrovaná nebo tunel zrušen adminem) → `Down()`, jinak `Up(configText)`. `MainActivity.OnCreate` žádá o systémový VPN souhlas jednou (`PrepareIntent` → `StartActivityForResult`), po odsouhlasení (`OnActivityResult`) hned zkusí sync. `App.xaml.cs`: nový throttlovaný job `RunWireGuardTunnelSyncAsync` (15 min, stejný vzor jako ostatní background joby) — tak appka pravidelně vyzvedne změny parametrů od admina i zrušení peera, ne jen při startu. Windows nemá `IWireGuardTunnelController` registrovaný → job se na Windows prostě přeskočí (`GetService` vrátí null), žádný `#if` v `App.xaml.cs`. `dotnet build` pro Android i Windows: 0 chyb.
   - **Hotovo 2026-10-31 (UI, jen status, kódem kompletní):** nová karta "Tunel" v Nastavení → Systém → Připojení, hned pod kartou "Relay" — jen text stavu (`Tunel: aktivní` / `vypnutý` / `čeká na povolení VPN` / `chyba (...)`), žádné nastavování (to je celý smysl plánu). `Infrastructure/WireGuardTunnelStatus.cs` (statický holder, zapisuje jen Android), `SettingsViewModel.Tunnel.cs` (`IsTunnelSupported` stejným vzorem jako `IsBackgroundRunSupported`, čte se při zobrazení Nastavení a při `OnWindowResumed` — ne živě přes event). `dotnet build` Android i Windows: 0 chyb.
   - **Zbývá jen reálný test na zařízení** (signed Release, HARD RULE) — teprve ten potvrdí, že binding skutečně vytvoří funkční tunel (ne jen že se to zkompiluje), a jestli `GoBackend.SetState` jankuje UI vlákno (volá se bez `Task.Run`, řešit jen pokud se to na zařízení projeví).
2. **Windows: vestavěný tunel.** P/Invoke wrapper na `wireguard.dll`, instalační krok v updateru: detekce chybějícího/zastaralého driveru → upozornění uživateli → stažení z oficiální WireGuard URL (ověřit aktuální adresu a hash/podpis při implementaci, nehádat teď) → instalace s UAC. Stejná auto-config logika a UI jako Android.
3. **Migrace stávajících 8 peerů** z externí WireGuard appky na vestavěný tunel — buď souběh obou (starý tunel běží dál, dokud appka nenaskočí na nový), nebo revoke + reissue přes admin. Najednou při první aktualizaci, která vestavěný tunel obsahuje.
4. **Úklid onboardingu** — QR/`.conf` cesta zůstává jen pro admin/debug (`new-pc-onboarding.ps1`), běžný uživatel už žádnou cizí appku ani konfigurační soubor nevidí; aktualizovat `relay/ops/README.md`.

## Rizika

- ~~Android binding cizí Kotlin/Java knihovny do .NET MAUI je netriviální~~ — **ověřeno 2026-10-31, vyřešeno.** `<AndroidMavenLibrary Include="com.wireguard.android:tunnel" Version="1.0.20260102" />` v binding projektu (šablona `android-bindinglib`) + `Xamarin.AndroidX.Annotation`/`Collection` jako transitivní závislosti → `dotnet build` 0 chyb, vygenerované typy `Com.Wireguard.Android.Backend.GoBackend` (+ `GoBackend.VpnService`), `Com.Wireguard.Config.Config`, `Statistics`. `.aar` obsahuje `libwg-go.so` pro `arm64-v8a` (~3,4 MB), `armeabi-v7a`, `x86`, `x86_64`. Test proběhl v izolovaném scratchpad projektu, nic v repu se nezměnilo. Další krok: binding projekt založit v repu (`src/SecureApp.WireGuard.Android` nebo podobně) a napsat skutečný `VpnService` potomek, co `GoBackend` používá.
- Windows driver instalace = jednorázová UAC výzva i ve "schovaném" scénáři — nelze slíbit uživateli nulovou interakci při prvním spuštění na novém PC.
- Testování jen na reálných zařízeních, jen signed Release (HARD RULE) — žádný Debug `dotnet build -t:Run` na produkční zařízení.
- Pokud vestavěný tunel selže (binding nejde / wireguard-nt se nepovede embedovat na Windows v rozumném čase), fallback je varianta B (opravit stávající externí-appku tunel, oprava `TuneWireGuardClientConfig` je už napsaná) nebo varianta A (veřejné HTTPS) z handoffu.

## Rozhodnuto (doplněno 2026-10-31)

- Varianta A (veřejné HTTPS bez tunelu) je odložena — řešíme jen vestavěný tunel; A se znovu otevře jen pokud vestavěný tunel nepůjde.
- Migrace stávajících 8 peerů proběhne najednou při první aktualizaci, která vestavěný tunel obsahuje (ne postupně po jednom zařízení).

## Pilot (od release 0.1.6, 2026-10-31)

Vestavěný tunel nikdy neběžel na reálném zařízení, tak jde ven jen pro **S23+** (`6b4bf8e9-131b-4304-8368-c831a32c5a2b`, "Zařízení S23+ uživatele Vilém"), ne pro všechny najednou:

- Relay `Program.cs`: `wireGuardTunnelPilotDeviceIds` — `/device/wireguard/config` vrací 404 pro každé jiné zařízení, přesně jako "appka nezaregistrovaná/tunel zrušen" (appka to tak už umí zpracovat).
- App `MainActivity.RequestWireGuardTunnelPermissionCoreAsync`: zavolá `HasPendingConfigAsync()` **před** zobrazením systémového VPN dialogu — zařízení bez pilotního configu ten dialog NIKDY neuvidí (ne jen "tunel se nezapne", ale appka se jich na VPN ani nezeptá).
- Petrův S25, S9+, PC v práci, DESKTOP-F8AS2U6: po aktualizaci na 0.1.6 se pro ně nic nezmění, žádný dialog, appka funguje jako dřív (plná funkčnost je neovlivněná — jen se appka tiše zeptá relay a dostane 404).
- **Až se S23+ potvrdí stabilní** (appka neuspí, nespadne, tunel funguje): smazat `wireGuardTunnelPilotDeviceIds`, dát endpoint všem autentizovaným zařízením. Žádná nová appka na klientech není potřeba — jen relay redeploy.
