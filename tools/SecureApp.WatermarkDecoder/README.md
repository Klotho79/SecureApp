# SecureApp.WatermarkDecoder

Čte neviditelný (pixelový) vodoznak, který appka SecureApp vkládá do každé vykreslené stránky
dokumentu nebo fotky (viz `src/SecureApp.Presentation/Rendering/PixelWatermark.cs`). Vodoznak
obsahuje jméno osoby, která dokument prohlížela, a čas (UTC) — uložené v nejméně významném bitu
(LSB) barevných kanálů R/G/B prvních ~60 000 pixelů, opakovaně za sebou.

## Použití

```
dotnet run --project tools/SecureApp.WatermarkDecoder -- cesta/k/podezrelemu/obrazku.png
```

nebo po sestavení samostatného .exe:

```
secureapp-watermark-decoder.exe cesta/k/podezrelemu/obrazku.png
```

Vypíše nalezené jméno + čas, nebo "Vodoznak nenalezen".

## Důležité omezení — přečtěte než z výsledku uděláte závěr

- **"Vodoznak nenalezen" NEZNAMENÁ, že se dokument neunikl bez souhlasu** — jen že tahle konkrétní
  technika ho na tomhle konkrétním souboru nedokázala obnovit.
- **Funguje spolehlivě jen na nezměněnou digitální kopii** (PNG export, soubor stažený/zkopírovaný
  z appky beze změny).
- **Nefunguje na vyfocený displej telefonem/fotoaparátem** — šum snímače, automatická expozice a
  JPEG komprese při fotografování prakticky vždy zničí bity, na kterých je vodoznak založený. Na
  tenhle případ appka spoléhá na VIDITELNÝ pohyblivý vodoznak (jméno+čas+IP) přímo na obrazovce
  (viz `DocumentViewerViewModel`), ne na tenhle neviditelný.
- Nefunguje ani po zmenšení obrázku nebo převodu do JPEG.

Jinými slovy: tenhle nástroj je doplňková vrstva pro případ digitálního úniku (např. export
souboru z appky), ne náhrada viditelného vodoznaku, který je určený právě pro případ vyfocení.
