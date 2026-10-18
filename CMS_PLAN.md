# Redakční systém (CMS) pro výukové materiály — plán

Stav: NÁVRH (2026-10-18), nic neimplementováno. Bez vodoznaků (viditelné rušivé, neviditelné zamítnuto) — CMS je nepoužívá a nepočítá s nimi. Stávající ochrana okna (FLAG_SECURE / WDA_MONITOR) zůstává beze změny.

## Cíl
Autor na PC vytvoří materiál z textu, fotek a videí. Ten se uloží do sdílené knihovny (stávající workflow Koncept → Ke schválení → Publikováno) a čte se na PC i mobilu. Čtenáři k němu přidávají poznámky, které lze sdílet, skrýt a schválením začlenit do dokumentu.

## Ochrana obrazovky (rozhodnutí uživatele 2026-10-18)
Blokování snímků a nahrávání obrazovky („tajný režim“) platí **jen v chatu**. Dokumenty a výukové materiály (včetně CMS dokumentů) chráněné nejsou. Implementace: `ScreenCaptureGuard` zapíná ochranu jen když je zobrazená chatová stránka; admin přepínač v Nastavení ji dál vypíná všude. Přitom se ruší i plán ochrany videa u CMS.

## Rozhodnutí (výchozí, k potvrzení)
1. Dokument = neměnná verze, JSON strom bloků (bez pixelů). Poznámky = oddělená vrstva, append-only události.
2. Editor jen PC: WebView2 + TipTap (text) + vlastní vrstva bloků/mřížky. Čtečka nativní MAUI (PC i mobil), sdílí se jen JSON schéma.
3. Responzivita podle šířky kontejneru: compact < 600, medium 600–840, expanded > 840. Řádek 12 sloupců, `span` per třída, v compact vše pod sebe. Média nesou `aspectRatio`, nikdy pevnou výšku.
4. Poznámky ukotvené přes `blockId` + citaci (exact/prefix/suffix) + offsety. Stavy `ok | shifted | orphaned`, nikdy se nemažou.
5. Synchronizace: stávající relay, REST (delta podle kurzoru) + WebSocket push. Bez Yjs/CRDT/SignalR.
6. Začlenění poznámky = nová verze dokumentu přes existující schvalovací tok; schvalovatel nikdy neschvaluje vlastní obsah.
7. Ignorování poznámek je soukromý záznam uživatele (`annotation_hides`), ne pole u poznámky.
8. Šifrování poznámek: `all` sdíleným klíčem knihovny; `private`/`selected` per-příjemce obálky (viz fáze 6).

## Potvrzená rozhodnutí uživatele (2026-10-18)
- Text ve čtečce je vykreslený jako výběrový text (ne PNG), poznámky lze ukotvit na slovo. Kopírování ven zůstává blokované, výběr funguje jen uvnitř aplikace.
- Po nové verzi dokumentu dostane autor poznámky dotaz: poznámku smazat, nebo znovu připojit (potvrdit/upravit ukotvení). Žádné tiché přenášení ani tiché mazání. Dokud autor nerozhodne, poznámka je ve stavu `needsReview` a čtenářům se ukazuje u staré verze / jako nepřipojená.
- Poznámka se připojuje k dokumentu s výběrem publika: všichni, nebo vybraní. Admin vidí všechny poznámky vždy, bez ohledu na publikum.
- Důsledek pro šifrování (fáze 6): admin musí být vždy příjemcem obálky každé `selected`/`private` poznámky.

## Editorská práva (rozhodnutí uživatele 2026-10-18)
Účel: školení nováčků — uživatel se látku naučí tím, že dokument vypracuje, a časem ho i revidovat.
- Právo editovat se uděluje **na jeden konkrétní dokument**, ne globálně a ne rolí. Je to samostatná schopnost vedle role (stejná myšlenka jako `IsDocumentReviewer`), nezávislá na Admin/Modifier/Viewer.
- Uděluje ho **Admin nebo Modifier** (Viewer ne). Typický tok: Admin/Modifier založí prázdný dokument (zadání: název, kategorie), přidělí editora; ten ho vypracuje a odešle ke schválení.
- Platnost: **od přidělení do schválení dokumentu** (publikace verze). Vrácení k přepracování (zamítnutí) právo nezruší — editor dál pracuje na konceptu. Admin i Modifier mohou právo kdykoli odebrat nebo předat jinému.
- Po schválení právo automaticky zaniká. Pozdější revize vyžaduje nové přidělení (Admin nebo Modifier znovu přidělí stejnému nebo jinému uživateli) — tím se každá revize stává vědomým rozhodnutím.
- Nová `RbacAction` (např. `AssignCmsEditor`) pro Admin + Modifier; schvalování zůstává jen pro Reviewery/Admina a nikdy nad vlastním obsahem. Modifier si smí přidělit i sám sebe (už dnes smí nahrávat koncepty), schválit se ale nemůže.
- Audit zaznamenává, kdo koho přidělil/odebral (`granted_by`, u odebrání i kdo).
- Editor nesmí schvalovat vlastní dokument (stávající pravidlo `LibraryReviewPolicy`).
- Vynucení na relay (ne jen v UI): uložení konceptní verze dokumentu vyžaduje platný grant nebo roli Admin. Změny grantů se zapisují do auditu.
- Editor vidí v UI seznam „Moje zadání“ (dokumenty s aktivním grantem a jejich stav).

Datově: relay tabulka `cms_document_editors(document_id, user_id, granted_by, granted_at_utc, ended_at_utc, end_reason)`; `end_reason` = `approved | revoked | reassigned`. Aktivní grant = `ended_at_utc IS NULL`. Při publikaci verze relay v téže transakci uzavře aktivní granty dokumentu (`approved`).

## Otevřené otázky (blokují fázi uvedenou v závorce)
- Smí mít jeden dokument víc editorů naráz (např. nováček + mentor)? Výchozí: ano, více aktivních grantů. (fáze 2)
- Sdílení míří na osoby (všechna zařízení kontaktu ARIM) nebo na zařízení? Výchozí: osoby. (fáze 4)
- Co když autor na dotaz nikdy neodpoví? Výchozí: poznámka zůstane `needsReview`, admin ji může rozhodnout za něj. (fáze 6)
- Limit videí (délka/rozlišení) vzhledem k úložišti na Pi a přenosu přes WireGuard. (fáze 3)

## Fáze

### Fáze 0 — Schémata a spec (malá)
- JSON Schema pro dokument (`blocks`, `row`, `image`, `video`, `list`, `heading`, `paragraph`) a pro poznámky/události.
- Doménové typy v `SecureApp.Domain` (`CmsDocument`, `CmsBlock`, `Annotation`, `AnnotationEvent`).
- Hotovo, když: schéma validuje ruční ukázkové dokumenty, build 0 chyb.

### Fáze 1 — Nativní čtečka (bez editoru) — IMPLEMENTOVÁNO (2026-10-18), NEOVĚŘENO NA ZAŘÍZENÍ
Hotovo: `DocumentType.Cms` (přípona `.cmsdoc`), `CmsDocumentView` (`Presentation/Cms/`), `ICmsMediaProvider` (obrázky/video ze sdílené knihovny, cache na disku), větev ve `DocumentViewerPage`, tlačítka zoomu jen na PC, `ISharedLibraryService.DownloadDecryptedAsync`. Build čistý (Windows i Android), JSON se deserializuje včetně polymorfních bloků.
1b (2026-10-18) — výběr textu IMPLEMENTOVÁN, NEOVĚŘEN NA ZAŘÍZENÍ: text se vykresluje po slovech (`SelectableText`, jeden `Label` na slovo ve `FlexLayout`), takže není systémový výběr ani menu „Kopírovat“. Klepnutí na slovo = výběr, druhé klepnutí na jiné slovo ve stejném textu = rozsah, další klepnutí = znovu, klepnutí na jediné vybrané slovo = zrušit. Spodní lišta ukáže citaci a tlačítko „💬 Poznámka“ (zatím jen ukázka ukotvení). Čistá logika (`CmsText`: tokenizace, rozsah, `CmsAnchor` s kontextem) je v Domain a prošla scratch testem. Omezení spiku: výběr jen v rámci jednoho odstavce/nadpisu/položky seznamu; velký dokument = tisíce `Label`ů (ověřit výkon na S9+; případná náhrada kreslením přes Skia).
Zbývá: ověření na reálných zařízeních (vzhled, výkon, zda klepnutí na slovo spolehlivě funguje na PC i Androidu), poster videa a pinch-zoom obrázku (fáze 3).
- Renderer bloků v MAUI (`CollectionView` + šablona na typ bloku), třídy šířky, přeskládání řádků.
- Obrázky s rezervovaným místem podle `aspectRatio`, výběr varianty podle šířky.
- Reflow zoom + fit-to-width na PC (jen preference zařízení).
- Napojení na stávající `DocumentViewerPage` jako nový typ obsahu v knihovně.
- Hotovo, když: 3–4 ruční dokumenty se správně zobrazí v šířkách 360/390/430/tablet/PC a na reálných telefonech (S9+, S23+, PC).

### Fáze 2 — stav (2026-10-18)
- **2a editor — IMPLEMENTOVÁNO, V APLIKACI NEOVĚŘENO:** `Resources/Raw/cms-editor.html` (vanilla JS, bez npm/CDN; TipTap se nepoužil — žádná závislost, bloky mají vlastní model), `CmsEditorPage` (WebView, výběr obrázku/videa, náhled šířek 360–768, uložení), `CmsDocumentSaver` (nahrání médií do sdílené knihovny jako nezařazené soubory, sestavení `CmsDocument`, import jako lokální `.cmsdoc`), `CmsImageProcessor` (varianty 480/960/1800 px, EXIF orientace). Vstupy: tlačítko „📝 Nový dokument“ v Dokumentech a „📝 Upravit“ ve čtečce (jen PC). JS prošel testem v headless Edge (načtení → úprava → export, stabilní `id` bloků, značky, sloupce, nested bloky, přepnutí třídy šířky, export→import→export identické). C# build čistý (Windows, Android).
- **Nevyzkoušeno v reálném WebView2 uvnitř aplikace:** návrat hodnoty z `EvaluateJavaScriptAsync` (použit base64, aby se obešlo escapování), velikost předávaných dat, file pickery, uložení end-to-end, drag & drop myší.
- **2b — ZBÝVÁ:** místo lokálního importu uložit jako konceptní verzi přes `/library-documents/*` (teď se ukládá jako obyčejný lokální dokument a při úpravě se starý lokální dokument nahradí novým).
- **2c — ZBÝVÁ:** editorská práva per dokument (relay `cms_document_editors`, vynucení, admin/Modifier UI, „Moje zadání“). Do té doby edituje každý, kdo smí nahrávat do knihovny.
- Známá omezení: video se nahrává bez překódování (fáze 3), nejde vložit obrázek mezi dva sloupce přetažením z plochy, žádné undo mimo vestavěné v prohlížeči, bez potvrzení při zavření s neuloženými změnami jen obecné varování.

### Fáze 2 — Editor na PC
- WebView2 + TipTap, vlastní bloky: odstavec, nadpis, seznam, obrázek, video, řádek se sloupci; drag & drop.
- Panel vlastností bloku, osnova, náhled „Mobil“ s přepínačem šířek.
- Uložení jako koncept přes existující endpointy `/library-documents/*` (staging verze), stabilní `blockId`.
- Editorská práva per dokument (sekce výše): tabulka `cms_document_editors`, vynucení na relay, admin UI „Zadat dokument / přidělit editora / odebrat“, editorská obrazovka „Moje zadání“, automatický zánik grantu při publikaci.
- Hotovo, když: autor vytvoří dokument od nuly, uloží koncept, otevře ho čtečkou z fáze 1.

### Fáze 3 — Média
- Import obrázků: generování variant (480/960/1800 px).
- Import videí: ffmpeg na PC → MP4 H.264+AAC, 720p (volitelně 1080p), faststart, poster, titulky.
- Přehrávač: `CommunityToolkit.Maui.MediaElement`, ovládání (play, osa, rychlost, fullscreen `contain` s respektem safe-area), range requests.
- Hotovo, když: video 16:9, 21:9 a svislé se přehraje bez deformace na PC i obou telefonech.

### Fáze 4 — Poznámky (základ)
- Relay: tabulky `document_annotations`, `annotation_events`, `annotation_hides`, `annotation_recipients`; endpointy zápis + `GET ?since=cursor` + WS `annotation-changed`.
- Klient: lokální cache v SQLCipher, kurzor na dokument, ukotvení, vlákna odpovědí.
- UI: PC boční panel 320 px + značky v textu; mobil bottom sheet (compact), boční panel (medium+).
- Viditelnost `private | selected | all`, skrýt poznámku / autora, filtry Moje / Sdílené se mnou / Všechny / Skryté.
- Hotovo, když: dvě zařízení si v reálném čase vymění poznámku, offline zařízení ji dostane po připojení.

### Fáze 5 — Moderace a začlenění
- Obrazovka „Poznámky ke schválení“ (fronta, kontext, diff, Začlenit / Odmítnout / Ponechat sdílené, hromadné akce).
- Začlenění vytvoří novou verzi (komentář, úprava textu nebo nový blok), poznámka → `incorporated`, autor dostane notifikaci.
- Audit přes `annotation_events` (obdoba `library_document_reviews`).
- Hotovo, když: celý cyklus poznámka → schválení → nová verze → publikace proběhne na reálných zařízeních.

### Fáze 6 — Šifrování `selected` a re-anchoring
- Per-příjemce obálky pro `private`/`selected` (jednorázový klíč poznámky zabalený pro příjemce přes existující páry relací).
- Po publikaci nové verze: pro každou poznámku se navrhne nové ukotvení (blok → citace → přibližná shoda). Autor dostane notifikaci a v dialogu zvolí „Znovu připojit“ (s náhledem navrženého místa, může ho upravit) nebo „Smazat“. Do rozhodnutí stav `needsReview`.
- Admin je vždy příjemcem obálky; vidí všechny poznámky a může `needsReview` rozhodnout za autora.
- Hotovo, když: relay nevidí text soukromé poznámky a po nové verzi proběhne dotaz autorovi end-to-end.

## Rizika
- Výběrový text (rozhodnuto) vyžaduje renderer, který text vykreslí jako text a zároveň zabrání kopírování ven; na Androidu/Windows to chce vlastní výběrovou vrstvu, ne systémový kontextový menu. Ověřit hned ve fázi 1.
- Zásah do vykreslování: nový renderer vedle stávajícího PNG vieweru, ne místo něj; ostatní typy dokumentů se nemění.
- Velikost videí na Pi/WireGuardu.
- Editor ve WebView2 je závislost jen pro PC; nesmí se dostat do Android/iOS buildu.

## Ověřování
Stejný standard jako dosud: build čistý na všech cílech + serverové ověření + podepsaný Release přes self-update (nikdy debug deploy na reálná zařízení). Každou fázi vydat zvlášť až na pokyn.
