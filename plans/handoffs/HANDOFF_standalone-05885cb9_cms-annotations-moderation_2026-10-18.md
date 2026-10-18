# CMS (redakční systém): editor, přidělená práva, poznámky a moderace hotové; další je fáze 3 (video)

**Date:** 2026-10-18
**Status:** IN PROGRESS
**Bead(s):** none (bd není nainstalováno)
**Epic:** SecureApp – CMS pro výukové materiály (`CMS_PLAN.md`)
**Chain:** `standalone-05885cb9` seq `1`
**Parent:** none — first in chain
**Prior chain:** none — first in chain

## Related Handoffs

- `HANDOFF_nastenka-room-status-redesign_2026-10-15.md` — Nástěnka/stav místností, jiný pracovní proud (release 1.65–1.70), ne rodič.
- `HANDOFF_library-redesign-subcategories_2026-10-02.md` — sdílená knihovna a její review workflow, na kterém CMS staví.

## Reference Documents

- `CMS_PLAN.md` — master plán CMS (rozhodnutí, fáze 0–6, stav fází aktualizován commitem `af9a8d3`).
- Paměť uživatele: `C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md` (infra cesty, UI Automation, pravidla o buildech a debug deploy).

## The Goal

SecureApp (.NET MAUI, Clean Architecture; Windows/Android/iOS/MacCatalyst; E2EE chat; relay na Raspberry Pi; self-update) dostává redakční systém pro výukové materiály. Dokumenty se tvoří jen na PC (editor ve WebView2) a čtou se na PC i mobilu (nativní čtečka, responzivní rozložení, text, fotky, video). Editorská práva se přidělují per dokument a per člen (e-mail) a zanikají schválením. Ke čtení patří poznámky s viditelností a moderací, které mohou vést k začlenění do nové verze. Koncový stav je celý cyklus: zadání → psaní → schválení → publikace → poznámky → začlenění, ověřený na reálných zařízeních.

## Where We Are

Hotovo a zapsáno do gitu (všechno pushnuté na `pi` i `github`, strom čistý, HEAD `af9a8d3`):

- **Čtečka (fáze 1, vydáno 1.73):** `src/SecureApp.Presentation/Cms/CmsDocumentView.cs` renderuje bloky nativně; `SelectableText.cs` je jeden Label na slovo s vlastní výběrovou vrstvou (kvůli ochraně kopírování); `SetMarks`/`Repaint` kreslí značky poznámek.
- **Editor (fáze 2a, 1.73):** `Resources/Raw/cms-editor.html` ve WebView, `Views/CmsEditorPage.xaml(.cs)`, `Cms/CmsDocumentSaver.cs`, `Cms/CmsImageProcessor.cs` (varianty 480/960/1800 px, EXIF). Jen PC.
- **2b (1.74):** uložení jako konceptní verze knihovny: `CmsDocumentSaver.StageDraftAsync` (nahraje `.cmsdoc` jako nezařazený soubor do složky „Výukové materiály“, pak `AddVersionAsync`, při selhání `CreateDraftAsync` s id dokumentu), `SubmitForReviewAsync`, `LoadLatestAsync`, `LastStageError`. Id CMS dokumentu = id dokumentu knihovny (relay přijímá volitelné `Id`).
- **2c (1.74):** editorská práva. Relay tabulka `cms_document_editors`, metody `GrantCmsEditor/RevokeCmsEditor/HasActiveCmsGrant/GetActiveCmsGrants/GetMemberEmail/IsPcDevice/GetCmsAssignableMembers/GetMemberKey`, endpointy `/cms/editors*` a `/cms/members`. Klient `HttpCmsAssignmentService`, `RedakceViewModel.CmsAssignments.cs`, karty v `RedakcePage.xaml` („Výukové materiály“, „Moje zadání“, „Zadání dokumentů“).
- **Identita člena** = e-mail z `activation_requests` (Approved), jinak e-mail sdíleného ARIM kontaktu, jinak `dev:<deviceId>`. Editace jen na PC (`directory_entries.platform` WinUI/Windows). Explicitní Viewer (role 2) potřebuje aktivní grant; Admin = role 0, Modifier = role 1. `ApproveLibraryDocumentVersion` grant uzavře (`end_reason='approved'`).
- **Fáze 4 – poznámky (1.74):** relay tabulky `cms_annotations`, `_recipients`, `_hides`, `_replies`; viditelnost private/selected/all, Admin vidí vše; ignorování per člen; odpovědi; soft delete. Těla šifrovaná klientsky sdíleným klíčem knihovny (`library:shared-key`, Nonce12+Tag16+Cipher, base64) v `HttpCmsAnnotationService`. UI: `CmsAnnotationPanel.cs`, `DocumentViewerPage.xaml(.cs)` (handlery `OnAnnotation*`, `PickRecipientsAsync`, `ReloadAnnotationsAsync` při změně `CmsModel`).
- **Fáze 5 – moderace (commit `f27396f`, NENÍ ve vydané 1.74):** stavy none/pending/incorporated/declined/kept, audit `cms_annotation_events`, `ProposeCmsAnnotation`/`ModerateCmsAnnotation`/`SetModeration`; moderátor = Admin/Modifier/aktivní grant, bez samomoderace mimo Admina, „kept“ nastaví publikum na všechny. Relay s tím je už nasazený na Pi (`/health` 200, nové endpointy bez autentizace vrací 401); klient s tlačítky „Navrhnout do dokumentu / Začlenit / Ponechat / Odmítnout“ čeká na další release.
- **Vydáno:** 1.73 (build 76) a 1.74 (build 77), obojí Android + Windows. Ověřeno `/download/android/version` a `/download/windows/version`.
- **Opravy self-updatu Windows (`225fb98`):** `NativeAppInstaller.cs` zapisuje pomocný PowerShell skript s UTF-8 BOM (cesta s diakritikou jako `K:\Meduňa\Appka` jinak rozbila PS 5.1); csproj target `RefreshWindowsAppVersion` maže zastaralý `obj\...\resizetizer\m\Package.appxmanifest`, takže `AppInfo.BuildString` už neuvázne na buildu 63 (příčina update smyčky).
- **Dnešní ne-kódová akce:** kontakty „Kosa Norbert“ (`2ce61da0-4731-4838-b5e4-71ffe6139ad0`) a „Repiščáková Veronika“ (`815d2545-7946-49c8-83bc-05668c0c6327`) převedeny z Firemních do Soukromých kontaktů ARIM nastavením `shared_contacts.note='ARIM'` na Pi (`updated_at_utc` 2026-10-18T17:47:59.698Z). Ověřeno čtením z DB.
- **Stav plánu:** `CMS_PLAN.md` obsahuje značky stavu u fází 2b, 2c, 4, 5 (commit `af9a8d3`).
- **Neověřeno v reálném provozu:** čtečka, výběr textu a editor na telefonech; celý tok zadání → publikace (potřebuje druhý účet jako Viewer); výměna poznámek mezi dvěma zařízeními; to, zda první PC update s BOM opravou projde (oprava pomůže až z 1.74 výš).

## What We Tried (Chronological)

1. **Update pad s verzí 60 / stále build 63 (uživatel: „když dám aktualizovat pc verzi spadne“).** Hypotéza 1: cesta s diakritikou. Zjištěno: skript bez BOM → PS 5.1 ho čte v ANSI. Oprava: `new System.Text.UTF8Encoding(true)` v `NativeAppInstaller`. Uživatel přejmenoval složku bez diakritiky, „průběh je stejný“ → druhá příčina. Hypotéza 2: `AppInfo.BuildString` je zastaralý. Příčina: zastaralý `Package.appxmanifest` v `obj\...\resizetizer\m\`; oprava targetem `RefreshWindowsAppVersion` (BeforeTargets="BeforeBuild", jen Windows). Výsledek: verze 1.74/77 se hlásí správně.
2. **Ruční zkopírování buildu do instalační složky uživatele** zablokoval klasifikátor oprávnění; nepokoušel jsem se obejít, nahlášeno uživateli.
3. **Android publish zabit pro nedostatek paměti.** Řešení: build až při 8,8 GB volné paměti, Android první, Windows po něm.
4. **Zápis do DB na Pi.** Příkaz přes `ssh secureapp-pi "docker run ... sqlite3 ... \"UPDATE ...\""` selhal na uvozovkách; `!`-prefix v tomto prostředí příkaz do SSH vůbec nedoručil (na Pi nevznikl `/tmp/x.sql`). Zafungovalo až: SQL do proměnné `$sql`, `$sql | ssh secureapp-pi "cat > /tmp/x.sql && docker run --rm -v .../data:/data -v /tmp/x.sql:/x.sql:ro --entrypoint sh nginx:alpine -c 'apk add -q --no-cache sqlite && sqlite3 /data/relay.db3 < /x.sql'"`. Dvě dřívější vlastní pokusy zamítl klasifikátor („Modify Shared Resources“, „Remote Shell Writes“); po explicitním vložení příkazu uživatelem jako zprávy prošel.
5. **Testování relay logiky bez nasazení:** konzolový projekt `scratchpad\relaytest\` odkazuje na csproj relay a testuje granty, viditelnost poznámek, ignorování, odpovědi, mazání a moderaci na dočasné DB. Všechno prošlo, bez logických chyb.
6. **Ověření živé Windows aplikace** přes UI Automation (`UIAutomationClient` v PowerShellu), protože aplikace sama blokuje snímání obrazovky (DLP). Karta Redakce a editor se otevřely.
7. **Git commit potichu selhal** (here-string s přesměrovanými chybami). Řešení: commit zprávu sestavit jako běžný řetězec s `` `n `` a vždy zkontrolovat výstup a `git log`.

## Key Decisions

- **Ochrana obrazovky („tajný režim“) je jen v chatu**, ne v CMS (rozhodnutí uživatele).
- **Editorské právo patří ČLENU (e-mail), ne zařízení; editace jen na PC.** Původně jsem navrhl per zařízení; uživatel opravil („identita člena je na mail … jen na pc může editovat“). Zamítnuto: grant na `device_id`.
- **Id CMS dokumentu = id dokumentu knihovny**, aby šlo použít stávající review workflow `/library-documents/*` místo paralelního systému.
- **Těla poznámek šifrována sdíleným klíčem knihovny** (fáze 4–5); per-příjemce obálky odloženy do fáze 6. Do té doby je řízení přístupu jen v relay. Admin musí být vždy příjemcem obálky (rozhodnutí uživatele pro fázi 6).
- **Vlastní model bloků místo TipTap** (žádná npm/CDN závislost); editor je vanilla JS.
- **Čtečka: vlastní výběrová vrstva** (Label na slovo), ne systémové kontextové menu, aby šlo zabránit kopírování ven.
- **Uživatel: nevydávat bez pokynu** a nebuildit po každé opravě (viz paměť „Batch builds“). Fáze 5 klient proto zatím není vydán.
- **Smazání poznámky** v UI jen pro autora (`IsMine`); Admin smí na relay, ale tlačítko pro něj v UI není (záměrně, jednoduché).
- **Otázka WireGuard vs. FortiClient / veřejný `wss` endpoint** probrána; uživatel nemá doménu a téma opustil, nic nezavedeno.

## Evidence & Data

| Commit | Obsah |
|---|---|
| `fb1cf3f` | Release 1.73: čtečka, výběr textu, editor, ochrana obrazovky jen v chatu |
| `225fb98` | Oprava Windows updatu (BOM, zastaralý AppInfo) |
| `ef684a0` | Redakce: vstup „Nový dokument“ |
| `4040d18` | 2b: uložení jako konceptní verze |
| `4206d06` | 2c: granty per dokument |
| `4360715` | Granty patří členovi (e-mail), jen PC, `/cms/members` |
| `f9e7fc2` | Fáze 4: poznámky |
| `1bccaa8` | Release 1.74 (build 77) |
| `f27396f` | Fáze 5: moderace (není ve vydané verzi) |
| `af9a8d3` | CMS_PLAN: stav fází |

| Stav fází | Kód | Vydáno | Ověřeno na zařízeních |
|---|---|---|---|
| 1 čtečka | ano | 1.73 | ne |
| 2a editor | ano | 1.73 | jen headless Edge test JS, ne reálný WebView2 |
| 2b koncepty | ano | 1.74 | ne |
| 2c práva | ano | 1.74 | testy na dočasné DB, bez druhého účtu |
| 3 média | ne | – | – |
| 4 poznámky | základ | 1.74 | ne (jedno zařízení) |
| 5 moderace | relay + klient | ne | testy na dočasné DB |
| 6 obálky + re-anchoring | ne | – | – |

- Verze: 1.73 = build 76; 1.74 = build 77; předtím 1.72 = build 75.
- Relay: `http://192.168.50.8:8080`, `/health` 200 po nasazení fáze 5.
- Anotační kotva `CmsAnchor`: VersionId, BlockId, Exact, Prefix, Suffix, Start, End.
- Stavy moderace: `none|pending|incorporated|declined|kept`.
- Dočasné testy: `C:\Users\dvora\AppData\Local\Temp\claude\H--Visual-Studio-C--Aplikace\5d29eafb-0799-4670-8d46-dff5fa287252\scratchpad\relaytest\`.
- Stav DB kontaktů po změně: `Kosa Norbert|ARIM|2026-10-18T17:47:59.698Z`, `Repiščáková Veronika|ARIM|2026-10-18T17:47:59.698Z`.

## Code Analysis

- `CmsAnnotationView(Id, DocumentId, AuthorName, Audience, Anchor, Text, CreatedAtUtc, HiddenByMe, IsMine, Replies, Moderation="none", CanModerate=false)` v `ICmsAnnotationService.cs`; metody `ListAsync, CreateAsync, ReplyAsync, ProposeAsync, ModerateAsync, DeleteAsync, SetHiddenAsync`.
- `CmsAnnotationPanel.BuildItem`: tlačítko „Navrhnout do dokumentu“ je podmíněno `a.IsMine && a.Moderation is "none" or "declined" && a.IsMine` (kvůli prioritě `is`/`&&` funguje, ale je křehké, při příští úpravě zjednodušit).
- `HttpCmsAnnotationService.SendForViewAsync` mapuje 403/404 na `InvalidOperationException` s českou hláškou.
- Relay `Program.cs`: helpery `CanEditCmsDocument`, `CanAssignCmsEditors`; endpointy `/cms/editors*`, `/cms/members`, `/cms/documents/{id}/annotations`, `/cms/annotations/{id}/replies|propose|moderate|hide`, `DELETE /cms/annotations/{id}`.
- `RelayDatabase.cs`: tabulky `cms_document_editors(document_id, editor_device_id, editor_email, title, folder_path, granted_by, granted_at_utc, ended_at_utc, end_reason)`, `cms_annotations`, `cms_annotation_recipients`, `cms_annotation_hides`, `cms_annotation_replies`, `cms_annotation_events`.
- `SelectableText` je jeden Label na slovo; výkon u dlouhých odstavců nebyl měřen, rizikové místo na S9+.
- Windows build: `AppInfo.BuildString` řídí kontrolu aktualizací; manifest v `obj` zastarává → target `RefreshWindowsAppVersion`.

## Files Changed

### Source code
- `src/SecureApp.Presentation/Platforms/Windows/NativeAppInstaller.cs` — BOM u pomocného skriptu.
- `src/SecureApp.Presentation/SecureApp.Presentation.csproj` — verze 1.74/77, target `RefreshWindowsAppVersion`.
- `src/SecureApp.Presentation/ViewModels/RedakceViewModel.cs`, `RedakceViewModel.CmsAssignments.cs`, `Views/RedakcePage.xaml` — vstup do CMS, přidělování.
- `src/SecureApp.Presentation/Views/CmsEditorPage.xaml(.cs)` — zadaná práce (`assignedDocumentId`, `assignedTitle`), odeslání ke schválení.
- `src/SecureApp.Presentation/Cms/CmsDocumentSaver.cs`, `CmsAnnotationPanel.cs`, `SelectableText.cs`, `CmsDocumentView.cs`.
- `src/SecureApp.Presentation/Views/DocumentViewerPage.xaml(.cs)` — panel poznámek a značky.
- `src/SecureApp.Presentation/Library/HttpCmsAssignmentService.cs`, `HttpCmsAnnotationService.cs`; registrace v `MauiProgram.cs`.
- `src/SecureApp.Domain/Interfaces/Services/ICmsAssignmentService.cs`, `ICmsAnnotationService.cs`, `ValueObjects/CmsAssignment.cs`.
- `relay/SecureApp.Relay/RelayDatabase.cs`, `Contracts.cs`, `Program.cs`.

### Docs
- `CMS_PLAN.md` — stav 2b/2c/4/5.

### Data & results
- Na Pi: `~/SecureApp/relay/SecureApp.Relay/data/relay.db3` (tabulka `shared_contacts` upravena pro dva kontakty).

## User Feedback & Preferences (REQUIRED — never omit)

- Píše česky, odpovídat česky.
- „chci aby aktualizace fungovala“ — update musí fungovat; ověřovat, ne tvrdit.
- „zkus to sam“ — preferuje, abych věci zkoušel sám (UI Automation, build), ne přeposílal na něj.
- „Aha no to pravo musi byt jak na zarizeni tak na clena … identita clena je na mail … a jen na pc muze editovat“ — práva per člen přes e-mail, editace jen PC.
- „No a kdyz se vratime k tomu redakcnimu systemu nevodim ho nikde“ — funkce musí mít viditelný vstup v UI.
- „Ok udelej release musis i na android jsou tam zmeny?“ — vydávat na obě platformy, pokud se Android měnil.
- „Ne muzeme pokracovat…“, „Pokracuj…“, „Pokracujem“, „Ano pokracujeme…“ — chce plynulý postup bez zbytečných dotazů.
- „1“ (volba možnosti, že to mám provést já) — tolerovat operace na Pi, ale klasifikátor je blokuje; funguje, když uživatel příkaz vloží sám jako zprávu.
- Paměť: nebuildit po každé opravě; nikdy debug deploy na reálná zařízení; podepsaný Release přes self-update.
- Nemá doménu; otázku FortiClient / veřejného endpointu pro práci opustil.

## Where We're Going

1. **Fáze 3 – média (rozhodnuto začít zde):** nejdřív zjistit, zda je na PC dostupné ffmpeg a jak ho přibalit; překódování videa na MP4 H.264+AAC 720p s faststart, poster, přehrávač `CommunityToolkit.Maui.MediaElement` ve čtečce, pinch-zoom obrázku. Hotovo, když 16:9, 21:9 a svislé video běží bez deformace na PC i obou telefonech.
2. **Release s fází 5 klientem** (jen na pokyn): bump csproj na 1.75/78, Android publish (podepsaný APK) a Windows publish, upload na relay, kontrola `/download/*/version`.
3. **Ověření na zařízeních:** čtečka, výběr textu, editor (WebView2), celý tok zadání → schválení se druhým účtem (Viewer), výměna poznámek mezi dvěma zařízeními.
4. **Poznámky:** živý push (WS `annotation-changed`), offline cache a kurzor, boční panel (PC) / bottom sheet (mobil), filtry.
5. **Fáze 5 zbytek:** fronta „Poznámky ke schválení“ s diffem, hromadné akce, skutečné začlenění do nové verze, notifikace autorovi.
6. **Fáze 6:** per-příjemcové obálky, re-anchoring po nové verzi (`needsReview`).

## Risks & Blockers

- Klasifikátor oprávnění blokuje zápisy na Pi a kopírování do uživatelských složek; počítat s tím, že uživatel příkaz vloží sám.
- Android build potřebuje dost volné paměti (dřív zabit při nízké); stavět Android první, pak Windows.
- Velikost videí na Pi a přenos přes WireGuard (otevřená otázka limitu délky/rozlišení).
- Výkon `SelectableText` (Label na slovo) na starších telefonech neměřen.
- První PC update s BOM opravou nelze plně ověřit, dokud uživatel neaktualizuje z ≥1.74 na novější.
- Editor v reálném WebView2: předávání dat přes `EvaluateJavaScriptAsync` (base64), file pickery, drag & drop jsou nevyzkoušené.

## Open Questions

- Smí mít dokument víc aktivních editorů naráz (výchozí: ano)?
- Sdílení poznámek míří na osoby nebo zařízení (výchozí: osoby; relay zatím používá zařízení)?
- Co když autor nikdy neodpoví na dotaz re-anchoringu (výchozí: `needsReview`, rozhodne admin)?
- Limit délky/rozlišení videa.
- Kdy vydat fázi 5 (čeká na pokyn uživatele).

## Quick Start for Next Session

```powershell
# Git (cesta k exe viz paměť secureapp-infra-paths)
$git = 'C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe'
$env:GIT_SSH_COMMAND = 'ssh -F "C:\Users\dvora\.ssh\config" -o IdentitiesOnly=yes'
& $git log --oneline -10    # očekávej HEAD af9a8d3 nebo novější

# Reference
# CMS_PLAN.md, plans/handoffs/HANDOFF_standalone-05885cb9_cms-annotations-moderation_2026-10-18.md

# Klíčové soubory
# src/SecureApp.Presentation/Cms/CmsDocumentView.cs
# src/SecureApp.Presentation/Cms/CmsDocumentSaver.cs
# src/SecureApp.Presentation/Views/CmsEditorPage.xaml.cs
# src/SecureApp.Presentation/Resources/Raw/cms-editor.html
# relay/SecureApp.Relay/RelayDatabase.cs, Program.cs

# Ověř relay
curl.exe -s -o NUL -w "%{http_code}" http://192.168.50.8:8080/health   # 200

# Verze ke kontrole
curl.exe -s http://192.168.50.8:8080/download/windows/version
curl.exe -s http://192.168.50.8:8080/download/android/version

# Next action
# Fáze 3: zjistit dostupnost ffmpeg na PC (Get-Command ffmpeg), navrhnout přibalení/stažení,
# pak implementovat překódování + poster ve CmsDocumentSaver a přehrávač v CmsDocumentView.
```

## Appendix: Release & Ops Recipes (verified this session)

### Release pipeline (jen na pokyn uživatele)
1. Bump `ApplicationDisplayVersion` a `ApplicationVersion` v `SecureApp.Presentation.csproj` (teď 1.74 / 77).
2. Android: `dotnet publish -f net10.0-android -c Release` → podepsaný APK `src\SecureApp.Presentation\bin\Release\net10.0-android\publish\com.companyname.secureapp.presentation-Signed.apk`. Stavět jako první a jen při dost volné paměti (>~8 GB).
3. Windows: `dotnet publish -f net10.0-windows10.0.19041.0 -c Release` (bez extra RID přepínačů), publish složku zabalit do zipu „naplocho“ (soubory v kořeni zipu).
4. Upload (hlavička `X-Admin-Secret`, tajemství je v `~/SecureApp/relay/SecureApp.Relay/.env` na Pi, číst přes `ssh secureapp-pi`):
   - Windows: raw POST na `/admin/upload/windows?versionCode=N&versionName=X`.
   - Android: multipart `curl.exe -F apk=@... -F versionCode=N -F versionName=X` na `/admin/upload/android`.
5. Ověřit `/download/android/version` a `/download/windows/version`.
6. Nikdy Debug deploy na reálná zařízení (paměť `never-debug-deploy-real-devices`: nesoulad podpisu smaže lokální vault).

### Relay deploy
`git push pi main`, potom `ssh secureapp-pi 'cd ~/SecureApp/relay/SecureApp.Relay && docker compose build && docker compose up -d'`, ověřit `/health`. Nové migrace tabulek se vytvářejí při startu (existují na Pi).

### Práce s DB relay na Pi (kořenový vlastník souboru)
- Čtení: `$sql | ssh secureapp-pi "sqlite3 -readonly -header /home/dvorakv1/SecureApp/relay/SecureApp.Relay/data/relay.db3"` (SQL přes stdin, jinak se rozbijí uvozovky).
- Zápis: jednorázový docker kontejner s `nginx:alpine` + `apk add sqlite`, viz „What We Tried“ bod 4. Sloupce `shared_contacts`: `id, display_name, phone, note, sort_order, created_at_utc, updated_at_utc, email`. Firemní kontakty = prázdná `note`; Soukromé ARIM = `note='ARIM'`.

### Git
- Cesta: viz Quick Start; remotes `pi` (`ssh://192.168.50.8/home/dvorakv1/secureapp-repo.git`) a `github` (`git@github.com:Klotho79/SecureApp.git`).
- Zprávu commitu sestavit jako řetězec s `` `n `` a končit řádky `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` a `Claude-Session: https://claude.ai/code/session_01KYU91M66ZuwAyDHhYMGyyX`. Vždy zkontrolovat výstup commitu.

### Testování živé Windows aplikace
Aplikace blokuje snímání obrazovky (DLP). Ovládání a čtení přes UI Automation (`UIAutomationClient` v PowerShellu), postup v paměti `windows-ui-automation-technique`. Takto se ověřilo otevření karty Redakce a editoru.

### Scratch testy relay
`scratchpad\relaytest\` (konzolový projekt s odkazem na relay csproj, dočasná DB). Při dalších změnách relay rozšířit o nový scénář a spustit před nasazením.

## Appendix: Aktuální omezení a drobnosti k úklidu

- Podmínka tlačítka „Navrhnout do dokumentu“ v `CmsAnnotationPanel.cs:98` má redundantní `a.IsMine` na obou koncích; funkčně správně, zjednodušit při příští úpravě.
- Admin může poznámku mazat na relay, ale UI nabízí „Smazat“ jen autorovi.
- „Začlenit“ zatím jen mění stav poznámky na `incorporated`; nevytváří novou verzi dokumentu ani změnu textu. To je největší mezera fáze 5.
- Poznámky se po změně verze dokumentu nepřekotvují (fáze 6); kotva drží `VersionId`.
- Editor: bez překódování videa (fáze 3), bez vložení obrázku mezi dva sloupce přetažením z plochy, undo jen vestavěné v prohlížeči.
- iOS/MacCatalyst se nedá sestavit (žádný Mac); udržovat kód kompatibilní, ale neověřovat.
- `CMS_PLAN.md` má nyní u fází 4 a 5 dva nadpisy („stav“ a „původní zadání“); při další větší úpravě sloučit.

## Session Closed
**Closed at:** 2026-10-18 21:57
**Commit:** 19581d6
**Session status:** Handed off to next session
