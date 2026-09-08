# 2026-09-08 — B5 follow-up (`deriveDisplayLabel`) — design-reviewer, form binding

> ⚠ **Transkriberad av sessionen ur agentens svar.** Hon har inget `Write`-verktyg i den här
> konfigurationen och returnerade rapporten för transkribering. Innehållet är hennes, ordagrant.
>
> Klas-direktiv 2026-09-08: *"Formen är `design-reviewer`s att binda. Ge henne valet; ge inget eget
> förslag."* Sessionen lade därför inget förslag och rekommenderade ingenting.

## Design-review: bindning av formen för B5 — `deriveDisplayLabel` (följd-PR till #1706/#1707)

**Status:** ✓ Form bunden. Ingen kod finns än, så inget rendered-verdikt är utfärdat och detta är
ingen `agents-done`-grund.
**Auktoritet:** DESIGN.md §4 (läsbarhetsdoktrin, informationsbärande siffror), §5 (4px, radie), §6
(hårlinjeliggare, "visa siffran direkt i rad"), §8 · AGENTS.md §5 `Frontend:`, §10 · ADR 0047 ·
ADR 0139 · CLAUDE.md §9.6.

### Ommätt material (mina siffror, inte sessionens)

Referensträdet: 22 sektioner, **87 huvudgrupper, 835 löv**; **9 huvudgrupper har exakt ett löv**
(12, 36, 37, 39, 41, 75, 92, 97, 99), **78/87 = 89,7 %** kan kollidera; störst är 47 med **74 löv**.
Huvudgrupp 62 = "Dataprogrammering, datakonsultverksamhet o.d.", löven 62100/62201/62202/62900.
Kommunaxeln: 21 län, 290 kommuner, Stockholms län 26. Allt stämmer med sessionens mätning.

**En terminologirättelse i min egen B5:** repots copy kallar SNI-sektionen *avdelning* och
2-siffernivån *huvudgrupp* (`pages.foretag.criteria.sniHelp`). B5 skrev "avdelning" om huvudgruppen.
Fyndet står, ordet var fel — fixen gäller **huvudgrupp**.

---

## BUNDET — detta är beslutet

**B-1. Fixen bor hos ANROPARNA, i en delad komponent. `deriveDisplayLabel`s logik ändras inte.**
Etiketten förblir ett **namn**; bredden bärs av **tal bredvid namnet**, aldrig inuti det. Fyra skäl,
i fallande tyngd: (1) samma sträng interpoleras in i `row.openBrowseAria`, `row.editAria`,
`row.deleteAria` och `row.deleteConfirmBody` — en kvantifierare där ger "Bevakningen
Dataprogrammering (1 av 4) tas bort"; (2) strängen är `<h1 class="jp-pagehero__title">` på 44px/800
på två ytor, och kommunaxelns "m.fl." kan inte lösas med namn alls (26 kommuner kan inte namnges i
en rubrik) — den ärliga primitiven för magnitud är ett tal; (3) katalogen (yta 2) bär redan talparet
och skulle annars säga samma sak två gånger; (4) ADR 0139: en etikettauktoritet.
Formen: **en delad presentationskomponent** som *alla fyra* ytor läser — samma val och samma skäl
som `CriterionAdLines`-extraktionen (senior-cto-advisor, #1681 del 3 in-block 1). Fyra inline-kopior
av samma uttryck är driftformen den extraktionen finns för att stänga. Förslag på namn
(icke-bindande): `components/company-criteria/criterion-breadth.tsx`, props `sniCodes` +
`municipalityCodes` som **arrays** (räkneregeln bor i komponenten, inte hos anroparna).

**B-2. Kvantiteten är RÅA LÖVANTALET — och det är den bindande halvan av B-1.**
`sniCodes.length` / `municipalityCodes.length`, exakt som `criterion-row.tsx:58` redan gör. Detta är
inte en detalj: repot bär en **andra** räkning, `decomposeSelection(...).length`, som dialogen visar
("N valda branscher"), och den **kollapsar en hel huvudgrupp till 1** (`criterion-options.ts:174-185`,
mätt). Med den räkningen renderar `{62100}` och hela huvudgrupp 62 **båda** "1 bransch" — B5 vore
oåtgärdad. En framtida harmonisering mot pickerns semantik får därför **inte** dra med sig dessa fyra
ytor utan att B5 mäts om. Noll-armen lämnas obehandlad: skrivvägen kräver minst en bransch och en
kommun, så "0 branscher" produceras av ingen väg.

**B-3. Ytor som ändras: 1, 3 och 4 får raden. Yta 2 ändras endast mekaniskt.**
**Yta 1 `/oversikt`:** raden ligger inuti `<li class="jp-appsummary__watch">`, **direkt efter**
`.jp-appsummary__watchname` och **före** `CriterionAdLines`. Renderas **ovillkorligt** — även när
`userLabel` finns (rådet "färre branscher" behöver talet oavsett rubrik) och även när
`reference === null` (då är den radens enda sakuppgift). Blockets `gap: 4px` äger avståndet.
**Ytorna 3 + 4 IN, och skälet är ADR 0047, inte symmetri:** båda renderar för-bred-refusalen med
"Färre branscher eller kommuner ger färre företag" och CTA:n "Ändra bevakningen" — en anvisad
handling vars storhet inte syns någonstans på sidan. Samma h1-tvetydighet därtill. Placering: **i
innehållskolumnen**, första raden efter `.jp-backlink`, direkt ovanför `<h2>`-magnituden — inte på
gradient-plattan. Precedensen är `.jp-cv-meta` (`cv/granska/[parsedId]`, PR #1684): plattan bär
sidans identitet, en post-deskriptor bor i containern. Det håller pagehero-elementsetet slutet
(kicker/title/lede/aside), slipper ett andra ink-register på plattan, och ger läsordningen
definition → utfall.
**Yta 2:** behåller sin `.jp-job__meta`-wrapper och byter bara det inline-byggda `summary` mot
komponenten. **Renderat resultat ska vara oförändrat** — det är omkontrollens hårda kontroll (R11).

**B-4. `formatNames`/`moreSuffix` ändras INTE, och de 8 testerna falsifieras därmed inte.**
Kvantifiering inuti etiketten avvisas av B-1:s fyra skäl. "m.fl." förblir okvantifierat på **båda**
axlarna — och besvaras på båda av talparet ("2 kommuner" mot "26 kommuner"), vilket är den halva B5
inte namngav. `display-label.test.ts:45,56,63` står **oförändrade**; :45 och :56 utgör tillsammans
redan pinnen för kollisionen (fixturens huvudgrupp 62 har exakt två löv, så :56:s indata *är* hela
huvudgruppen). Bind ett **nionde** test som gör avsikten oläsbar som slarv:
`expect(deriveDisplayLabel(["62010"],…)).toBe(deriveDisplayLabel(["62010","62020"],…))` under namnet
"ett enda löv och hela huvudgruppen ger SAMMA etikett (medvetet — bredden bärs av breddraden)".
Ingen befintlig assertion rörs.

**B-5. Docblocket rad 9-12: den falska satsen RADERAS, ersättningen namnger egenskapen och sin
komplement-yta.**
Ta bort "and a whole-division pick reads as exactly its name". Ersätt med (engelska, §1):

> The label states COVERAGE, never EXTENT, and that is deliberate: one leaf and its whole huvudgrupp
> render the same string, and "m.fl." does not say how many. Extent is answered beside the label by
> the breadth line ("1 bransch · 1 kommun") and never inside it — this string is also interpolated
> into aria-labels and the delete confirmation, where a quantifier would read as part of the watch's
> name.

Inga siffror i kommentaren (de decayar mot en ny referensversion); egenskapen, inte censusen.

**B-6. Copy: INGEN ny nyckel, ingen ändrad nyckel. `messages/sv/` och `messages/en/` är orörda.**
Återanvänds ordagrant, båda från `pages.foretag.criteria`:
`row.branschCount` = `"{count, plural, one {# bransch} other {# branscher}}"`
(en: `"{count, plural, one {# industry} other {# industries}}"`)
`row.kommunCount` = `"{count, plural, one {# kommun} other {# kommuner}}"`
(en: `"…{# municipality}…{# municipalities}"`)
Avdelaren är `" · "`, deklarerad i komponenten som **layoutglyf, inte copy** — samma formulering som
`criterion-row.tsx:28-30`. Ingen aria-avvikelse: separatorn stannar i strängen, som i den levererade
katalograden. Ordvalet "bransch" om ett löv är pickerns eget (`sniSelectedCount`), så talet
användaren ser är det hon valde.

**B-7. CSS: en klass, delad av alla fyra ytor. Inga nya tokens.**

```css
.jp-criterion-breadth {
  margin: 0;
  font-size: var(--text-body-sm);   /* 14px — paritet med .jp-job__meta, samma uppgift */
  color: var(--jp-ink-1);           /* aldrig ink-2/ink-3; ingen grå metadata */
  font-variant-numeric: tabular-nums; /* DESIGN.md §4: informationsbärande siffror */
}
```

`tabular-nums` sätts **i regeln, inte som utility** — `.jp-*` är olagrat och slår varje
Tailwind-utility för en egenskap regeln själv sätter, så en utility i JSX vore en tyst no-op och en
falsk signal till nästa läsare. Av samma skäl: `margin: 0` gör `mb-*`/`mt-*` på elementet
verkningslöst — avståndet ägs av grannarna. På ytorna 3/4 betyder det `mt-2` (8px) på den befintliga
`<h2>`:n; `.jp-backlink` sätter ingen marginal, så dess `mb-4` (16px) står kvar ovanför. 16 över /
8 under: raden binder till h2:n, inte till bakåtlänken. 4px-rutnätet håller på alla tre ytor.

---

### Vad som ska renderas efteråt (mekaniskt kontrollerbart)

Viewports **1280 / 1920 / 3440** på varje avläsning, plus en 390-passering på `/oversikt` för
radbrytning. Ljust tema, se dark-mode-noten.
**Nollkontroll före allt annat (annars mäter en grön avläsning ingenting):** kör R1 mot HEAD **utan**
ändringen och spara avläsningen — 0 `.jp-criterion-breadth`-noder och två oskiljbara rader. En
"efter"-avläsning som inte är en *skillnad* mot den är ingen mätning.

| # | Yta / tillstånd | Assertion |
|---|---|---|
| R1 | `/oversikt`, bevakning A=`{62100}`, B=de fyra löven i 62, samma kommun | `.jp-appsummary__watchname` **lika** för A och B; `.jp-criterion-breadth` **olika**: "1 bransch · 1 kommun" mot "4 branscher · 1 kommun". Nodantal === 2 innan text läses |
| R2 | `/oversikt`, `reference === null` | Rubrik = "Branschbevakning" för båda; breddraden finns och skiljer dem ändå |
| R3 | `/oversikt`, degraderad annonsläsning | `ads.countUnavailable` + breddraden; DOM-ordning namn → bredd → status |
| R4 | `/oversikt`, N≥2 med för bred bevakning | Breddrad 14px mot `.jp-matchline` 16px (computed), ordning namn → bredd → refusal; ingen fusion |
| R5 | `/oversikt`, N=20 | 20 noder, hårlinjeliggaren läsbar vid 1280 |
| R6 | `/oversikt`, N=0 och `kind !== "ok"` | **0** noder (kontroll att deltat inte läcker) |
| R7 | Yta 3, ok-läge | Raden mellan bakåtlänk och h2; computed 16px över / 8px under; färg `rgb(12,26,46)`; `font-variant-numeric: tabular-nums` |
| R8 | Ytorna 3 + 4, för bred | Refusal + "Ändra bevakningen" och breddraden på **samma skärm** (ADR 0047-punkten) |
| R9 | Yta 4, ok + `?visa=matching` | Exakt en breddrad, ovanför h2, ingen dubblering |
| R10 | Ytorna 3/4, ErrorShell | 0 noder |
| R11 | Yta 2, katalogen | Metaradens text, computed font-size, färg och marginal **identiska** före/efter |

**A11y (golv, inte önskelista):** DOM-ordning = visuell ordning på alla tre ytorna · inget nytt
fokuserbart element (antal fokuserbara på `/oversikt` identiskt före/efter) · ingen `role`/`aria-live`
— raden är en egenskap, inte en status · på yta 1 ligger den i samma `<li>` som namnet, så den
annonseras med det (WCAG 1.3.1) · kontrast mätt vid noden, inte antagen (ink-1 mot vitt 17,46:1, mot
canvas 16,14:1) · forced-colors: ren text, ingen färgburen signal, inget nytt att armera mot #1638.

**Dark mode:** `theme-provider.tsx:37` `DARK_MODE_ENABLED = false` (ommätt), så `[data-theme="dark"]`
går inte att nå från produkten och ingen mörk avläsning är skyldig. Vald token är ändå tema-säker:
`--jp-ink-1` är omdeklarerad under `[data-theme="dark"]` (`#F4F7FC`, 17,03:1 mot mörk canvas), så
raden ärver ett giltigt värde den dag flaggan vänds.

### Major (eget fynd, blockerar INTE denna PR, egen ändringsorsak)

1. **Samma bevakning räknas till två olika tal under samma ord** — Fil:
   `criterion-dialog.tsx:94-100,176` mot `criterion-row.tsx:58`
   Nuvarande: dialogen visar `decomposeSelection(...).length` → hela huvudgrupp 62 = "1 vald bransch";
   posten visar `sniCodes.length` → samma bevakning = "4 branscher". · Krävs: ett ord, en betydelse —
   antingen skilda substantiv eller en gemensam räkneregel; **inte** genom att posten byter till
   decomposed-talet, vilket återöppnar B5 (B-2). · Motivering: DESIGN.md §8, ADR 0047. Befintligt
   repo-tillstånd som deltat inte skapar, men som denna PR **sprider till tre ytor till** — därför
   namngivet nu och inte senare. Route: egen issue (`area:companies`, lane `FE`, `mvp` per
   §6.5-kriteriet — en riktig testanvändare möter branschbevakningarna). Vidga **inte** denna PR med
   den.

### Bra gjort

- `deriveDisplayLabel` är ren och locale-injicerad, så hela detta beslut kunde fattas utan att röra
  en enda renderad sträng.
- `CriterionAdLines`-extraktionen gav en färdig, bevisad form för "en kunskap, en komponent, fyra
  ytor" — B-1 är dess syskon, inte en ny uppfinning.
- Katalograden bar redan svaret; fixen är att låta tre ytor läsa det, inte att uppfinna ett fjärde.

### Sammanfattning

Formen är bunden i B-1 till B-7: talpar hos anroparna via en delad komponent, råa lövantal, ytorna
1/3/4 ändras och yta 2 endast mekaniskt, ingen ny copy, `formatNames` orört, docblockets falska sats
raderad. 0 blockers mot ett delta som ännu inte finns; 1 Major routad som egen issue. Delegera
implementationen till nextjs-ui-engineer. Re-review efter fix: samma agent, report-only, skopad till
fix-deltat (CLAUDE.md §9.6) — och rendered-verdikt kan inte utfärdas förrän R1-R11 med sin
nollkontroll är körda (AGENTS.md §8 punkt 4).

---

## Sessionens oberoende verifiering av hennes mätta påståenden

Innan något av detta skrevs in i kod eller kommentar (minnesregel: en agentrapport BESLUTAR, den
MÄTER inte):

- **`decomposeSelection` kollapsar en helvald nod till ett alternativ** — verifierat i
  `criterion-options.ts`: `allSelected` → `out.push({ code: node.code, … }); continue`. B-2 och
  hennes Major håller mot koden.
- **Terminologin** — `pages.foretag.criteria.sniHelp`: *"Du kan välja en hel avdelning, en
  huvudgrupp eller enskilda koder."* Hennes rättelse stämmer.
- **`.jp-backlink` sätter ingen marginal** — verifierat i `globals.css`.
- **`.jp-cv-meta`-precedensen** — `margin: 0; font-size: var(--text-body-sm);`, verifierad.
- **Referensträdet** — sessionens egen räkning mot `sni-2025.v1.json`: 87 huvudgrupper, 835 löv, 9 med
  exakt ett löv (samma nio koder), max 74. Identisk med hennes.
