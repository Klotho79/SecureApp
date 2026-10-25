# Návrh obsazení pozic (Opicentrum) — plán

Stav: fáze 1 v kódu (2026-10-25), nevydáno, relay nenasazen. Experimentální funkce, jen pro Admina, za přepínačem.

Fáze 1 (hotovo v kódu): relay `/opicentrum-staffing-config` (singleton blob, čte Admin/Modifier, zapisuje Admin, push „board-changed“ `staffing`), `OpicentrumStaffingConfig` (Domain), `HttpOpicentrumStaffingConfigService`, stránka `OpicentrumStaffingPage` (Lékaři / Pracoviště / Služby) — vstup tlačítkem „⚙ Profily“ na boardu Pozice (Admin na PC). Lidé/pracoviště/sloty služeb se berou z lokálních dat (týdny Pozic, Volno, služby); „Načíst z Opicentra“ stáhne aktuální + 4 minulé týdny. Součet cílových % nesmí přesáhnout 100 (zbytek = historie/férový podíl); L4 + úsek předvyplní 50 %.

Fáze 2 (hotovo v kódu): `OpicentrumHistoryService` (singleton) stahuje na pozadí 6 měsíců — týdny Pozic, měsíce Volna a Služeb (nová `FetchDutiesForMonthAsync`, sluzby7.php pro všechny) — po jedné stránce s pauzou 2 s, nejnovější první, ukládá průběžně do `opicentrum_history.json` v datové složce profilu; hotové minulé období se už nestahuje, aktuální se obnoví po 12 h; nic se nemaže, data se jen doplňují (statistika čte okno 6 měsíců). Spouští se při otevření Profilů a tlačítkem „Aktualizovat historii“. `OpicentrumStaffingStatistics`: den na dvou pracovištích = půl/půl, NEZAŘAZENÍ jen bez jiného pracoviště, služby párované podle jména (`OpicentrumParsing.NamesMatch`). Záložka „Statistika“: dní, služeb, nezařazen, nejčastější pracoviště, odchylka (s cíli = největší rozdíl od cíle, bez cílů = rozdíl od průměru grade, červeně ≥ 15 b.), detail per pracoviště; v záložce Lékaři u každého cíle šedě skutečnost.

## Cíl
Appka z dat Opicentra navrhne obsazení pozic (board Pozice) na zvolený týden. Učí se z historie, kdo kam chodí, respektuje grade, volno, služby a nastavená procenta, do návrhu přidává řízenou náhodu a ukazuje statistiku, aby obsazování bylo fér. Návrh se jen zobrazí; zapsat do Opicentra se dá vědomě po týdnech.

## Pravidla od uživatele (2026-10-25)
- **L1N** — nováček, na sále nesmí být sám, zaškoluje se. V návrhu jde vždy jako druhý ke školiteli (L3+), nikdy neobsazuje pozici sám.
- **L1** — bez kmenové zkoušky z anestezie. Na sály sám může, na detašovaná pracoviště (např. oční) ne; na některá (např. DIOP) ano — rozhoduje nastavení pracoviště.
- **L2** — po kmeni, pravidla jako L1.
- **L3** — po atestaci, může všude.
- **L4** — většinou úsekový lékař, má být kolem 50 % na svém úseku (výchozí cíl, admin ho může změnit).
- **Procenta na pozicích** — každý uživatel má v nastavení „požadované obsazení pracovišť“ (cílová % per pracoviště). Specialisté na určitý typ anestezie se na své pozici střídají.
- **Náhoda** — např. úsekový vedoucí ARIM si má občas „odpočinout“ na sále; návrh nemá být pokaždé stejný.
- **Služby** — kdo slouží, má být ten den na pracovišti, kde slouží, bez ohledu na preference. Přepínač „Respektovat službu“ (výchozí zapnuto).
- **Fér** = fér: každý se drží svých cílových procent, nikdo není dlouhodobě přetížený ani odstrčený.
- **Výstup** — jen zobrazit, s možností zapsat na určitý týden.

## Zdroje dat (už existují)
- Pozice (roster editor): den × pracoviště × lidé s `PersonId` (`OpicentrumRosterBoardSnapshot`), libovolný týden se dá dostáhnout (`OpicentrumRosterBoardLocalCache`).
- Volno (`spravavolna.php`): kódy per člověk a den (ŘD, PN/PL, SC, VV/NV, PS = po službě…) → kdo je k dispozici (`OpicentrumVolnoSnapshot`).
- Služby (`sluzby7.php`): 7 slotů měsíčně se jménem (`DutyRosterStore`, dnes drží jen ±7 dní).
- Zápis: přesun / přiřazení člověka na pracoviště (`OpicentrumBoardViewModel.MovePersonAsync`).

## Datový model (nový)
- **Profil lékaře** (klíč = `PersonId` z Opicentra): grade (L1N/L1/L2/L3/L4), domovský úsek (u L4), specializace (volný seznam štítků), cílová % per pracoviště (prázdné = odvodit z historie / rovný podíl ve skupině grade), „vyřadit z návrhu“.
- **Profil pracoviště** (klíč = `WorkplaceId`): typ (sál / detašované / úsek), minimální grade, výjimka „L1/L2 smí“ (DIOP), vyžadovaná specializace, kolik lidí (výchozí = typický počet z historie pro daný den v týdnu), zda se tam smí zaškolovat L1N.
- **Mapování služeb**: slot ze `sluzby7.php` („ARIM I“, „Odpolední 13–21 h“, …) → pracoviště.
- Uložení: relay, admin-only endpointy (osobní údaje personálu, stejně citlivé jako dnešní snapshot rosteru); čte Admin/Modifier, zapisuje Admin.

## Algoritmus návrhu
1. **Tvrdá omezení**: volno a PS → nikam; jeden člověk = jedna pozice denně; grade/specializace dle profilu pracoviště; L1N jen jako doplněk k L3+; při zapnutém přepínači služba → pracoviště služby (pevně předem).
2. **Skóre** pro zbývající (člověk, pracoviště, den):
   - deficit vůči cílovým % v klouzavém okně (hlavní složka — kdo je pod cílem, má přednost; tím vzniká střídání specialistů i „fér“),
   - zvyk z historie (menší váha, novější týdny váží víc),
   - náhoda (posuvník „přísně ↔ promíchat“; deterministické semínko, tlačítko „Navrhnout znovu“ = nové semínko).
3. **Přiřazení** den po dni: nejdřív nejvzácnější pozice (nejméně oprávněných lidí), pak greedy podle skóre; deficit se průběžně přepočítává, aby se týden vyrovnával. Bez sítě, běží lokálně na PC (milisekundy).
4. Neobsaditelná pozice → zvýrazněná s důvodem (např. „nikdo s L3 volný“).

## UI
- **Pozice → „⚙ Profily“ → Lékaři** (PC, Admin): lidé z Opicentra, grade, úsek, specializace, cílová % (součet max. 100 %); skutečná % z historie přibudou ve fázi 2.
- **Pozice → „⚙ Profily“ → Pracoviště / Služby** (PC, Admin): min. grade (detašovaná L3, výjimky L1), zaškolování L1N, specializace, počet lidí, vyřazení; mapování slotů služeb na pracoviště.
- **Board Pozice → „Navrhnout“** (Admin): návrhové (šedé) čipy vedle skutečných, přepínač „Respektovat službu“, posuvník náhody, „Navrhnout znovu“, přijmout čip / zahodit čip, **„Zapsat týden“** (potvrzení se souhrnem změn; uloží se stav „před“, aby šel zápis vrátit).
- **Statistika** (Admin): per člověk cílová vs. skutečná % za období, odchylka, zvýraznění; filtr podle grade.

## Fáze
1. **Profily lékařů a pracovišť + mapování služeb** (relay tabulky + endpointy, PC obrazovky). Předvyplnit z historie.
2. **Historie + statistika** — pomalé stažení minulých týdnů Pozic/Volna/Služeb na pozadí (šetrně k Opicentru), statistika. Ukáže, zda jsou data dost dobrá.
3. **Návrh** — algoritmus + zobrazení na boardu (jen čtení).
4. **Zápis týdne** do Opicentra + vrácení.
5. Ladění vah podle zpětné vazby.

## Otevřené otázky
- Klouzavé okno pro procenta: **rozhodnuto 6 měsíců** (uživatel 2026-10-25: „vem vše 6 měsíců“), stejně jako hloubka historie.
- Seznam specializací a které pracoviště kterou vyžaduje (vyplní se v nastavení, nemusí být předem).
- Hloubka historie: **rozhodnuto 6 měsíců** (uživatel 2026-10-25). Opicentrum má historii několik let — délka je nastavitelná, rozšíření jen dostáhne starší týdny (vhodné např. pro vzácné pozice, kde je 6 měsíců málo dat).
