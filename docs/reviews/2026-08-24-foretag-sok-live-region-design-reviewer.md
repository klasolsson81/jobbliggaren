# Design-review: /foretag/sok live-region (PR #1504, #1092)

**Agent:** `design-reviewer` (a11y-veto) · **Datum:** 2026-08-24 · **PR:** [#1504](https://github.com/klasolsson81/jobbliggaren/pull/1504)
**Gren:** `fix/foretag-sok-live-region-1092` · **Delta:** `origin/main...HEAD`, merge-base `207e9bb4` · **Head:** `1b15966b`
**Status:** ✓ Approved — 0 Blocker / 0 Major / 5 Minor
**Auktoritet:** DESIGN.md §8, §9 · WCAG 4.1.3 + ARIA22 · CLAUDE.md §9.6

> **Transkriberad av den anropande sessionen.** Agentens charter är report-only (§6: en granskare som
> pushar innehåll river `agents-done`-grinden), och sessionens harness förbjöd henne att skapa
> report-filen. CLAUDE.md §9.2 lägger transkriptionsplikten på den anropande sessionen och binder den
> vid charterns Output format-cap. Innehållet nedan är hennes, ordagrant.

**Läst:** hela deltat (9 filer), `foretag-sok-announcer.tsx` i sin helhet, båda testfilerna,
`foretag-sok-searchbar.tsx`s regioner, `/jobb`s motsvarighet, `globals.css` (sibling-selektorer),
DESIGN.md §8/§9 och skills `-a11y` + `-copy`.

**Mitt eget fynd #1092 är utkvitterat.** Mekanismen håller mot den väg fyndet namngav. `useEffect`
kan aldrig köra i samma commit som den render som skapade noden — passive effects körs efter
mutation-fasen — så regionen ligger i DOM:en före varje `setMessage`. Vid en sökning
(`router.push`) är providern dessutom redan monterad sedan tidigare render, så noden är bokstavligt
persistent. ARIA22:s testprocedur är uppfylld på alla tre vägarna (klientsökning, cross-route via
`loading.tsx`, hard load/SSR).

## Blockers

Inga.

## Major

Inga.

## Minor

**1. "ONE persistent live region" gäller inte över `loading.tsx` → `page.tsx`**
Fil: `src/app/(app)/foretag/sok/loading.tsx:29` + `src/components/company-criteria/foretag-sok-announcer.tsx:5`
Nuvarande: två `ForetagSokAnnouncer`-instanser på cross-route-vägen, alltså två olika DOM-noder:
fallbackens region bär "Söker företag…", sidan monterar en **ny** region som bär slutmeddelandet.
Docblocken läses som om en nod täcker hela cykeln. · Krävs: antingen hissa providern till
`app/(app)/foretag/sok/layout.tsx` (en layout överlever sin egen `loading.tsx`, så båda värdarna kan
släppa sin wrapper och noden blir verkligt persistent) — eller stryk överdriften ("the ONE persistent
live region for `/foretag/sok`'s load cycle" + `loading.tsx:9-11`). · Motivering: DESIGN.md §9 /
ARIA22. Håller **inte** merge: varje region föds tom och fylls en commit senare, vilket är den
accepterade mitigeringen och strikt bättre än main. Stryknings-vägen stänger mekaniskt (§9.6).

**2. Rationalen för cleanup-blanken är fel på båda benen**
Fil: `foretag-sok-announcer.tsx:41-49` (+ testkommentaren "The cleanup blank, pinned in both directions")
Nuvarande: (a) "Clearing on unmount guarantees every sentence is preceded by an empty region" — vid
en subtree-swap körs unmount-cleanup och mount-effect i **samma** passive-flush och React batchar
dem, så `""` når aldrig DOM:en; blanken räddar alltså inte det fall den namnger. (b) "the trap
`foretag-sok-searchbar.tsx` declares as a known limitation" — grannen *deklarerar* den inte, den
**stängde** den (`:396-402`, mätt i Chromium) genom att namnge objektet. · Krävs: stryk de två
prosastyckena; behåll koden. · Motivering: beteendet är korrekt idag av ett annat skäl —
`suspenseKey` (`page.tsx:116`) innehåller `page`, så skeletonens "Söker företag…" alltid ligger
emellan två resultatmeningar. Risken är en a11y-guard som läser som bärande men inte är det: tar
någon senare bort skeletonens `Announce` tystnar utropen medan testet förblir grönt. Test-premissen i
sig är `code-reviewer`s §5 `Tests:`-mark — jag omgraderar den inte.

**3. `announceResultsReady` bryter systerpanel-mönstret och parar inte med öppningsmeningen**
Fil: `messages/sv/pages.json:295` (+ `en:295`)
Nuvarande: `"Företagen har laddats."` · Krävs: `"Företagen i registret visas."`
(en: `"The companies in the register are shown."`); alternativ närmast systrarna:
`"Företagen är laddade."` · Motivering: DESIGN.md §8 "konkret". Alla fem systernycklar är `är` +
particip ("Filtret {namn} **är tillagt**", "Sökningen **är rensad**") — den här är den enda `har` +
supinum, alltså händelse i stället för tillstånd. Öppningen säger "Söker företag…", stängningen
"laddats": skärmläsaren hör en sökning börja och en laddning sluta. Förslaget speglar dessutom den
synliga `<h2>` "Företag i registret", vilket ger användaren det enda som numret annars hade sagt —
*vilken* mängd som kom. Ingen hård regel bruten (ingen emoji, inget utropstecken, ingen em-dash),
därför Minor.

**4. `/jobb` bär kvar exakt den form som togs bort här**
Fil: `src/components/job-ads/jobb-results-toolbar.tsx:452-459`
Nuvarande: `role="status" aria-live="polite"` direkt på `.jp-results-count` — regionen föds med sin
text, samma defektklass som #1092. · Krävs: fila mot `/jobb` (äkta defekt i levererad kod =
cap-undantagen enligt §9.6), fixa **inte** in-block. · Motivering: efter den här PR:en finns två
konkurrerande husmönster för samma jobb; nästa yta kopierar det svagare. Gradering av det fyndet
tillhör den review som täcker `/jobb`.

**5. "Exactly one region on the surface" är falskt**
Fil: `foretag-sok-results.test.tsx` (kommentaren över `toHaveLength(1)`)
Nuvarande: ytan bär **tre** polite-regioner: filterregionen (`foretag-sok-searchbar.tsx:1014`),
org.nr-sektionen (`:1027`) och den nya. · Krävs: byt "surface" mot "results subtree" — assertionen
själv är korrekt och scopad. · Motivering: faktafel i kommentar (§5 `Comments:`), stängs mekaniskt.

## Svar på de fyra riktade frågorna

- **(1) ARIA22:** ja. `useEffect`-ordningen är vattentät; det som inte håller är persistens-anspråket
  (fynd 1), inte mekanismen.
- **(2) Dubbelt/inget:** inget dubbelt — skeleton, count-rad och empty-state renderar sin text som
  vanligt innehåll utan roll, regionen säger var mening en gång. Inget tappat heller: fel-grenen
  stänger loopen via `ErrorShell`s `role="alert"` (`foretag-sok-results.tsx:229`), `unauthorized` via
  `redirect`.
- **(3) "Företagen har laddats.":** rätt beslut att stänga loopen (4.1.3 är ett golv, inte ett tak)
  och den påstår korrekt inget tal — men formuleringen bör bytas, se fynd 3.
- **(4) Två regioner:** rätt. Polite-meddelanden köas av AT, de slås inte ihop — risken med två
  regioner är ordning, inte förlust. En region med två skrivare hade däremot förlorat filtermeningen
  mekaniskt: searchbaren sätter sin sträng vid commit, laddningen en passive-flush senare, och React
  batchar bort mellanläget. Bunden att hålla: tre polite-källor är taket — en filterändring ger redan
  "Filtret X är tillagt." → "Söker företag…" → "1 234 träffar".

## Bra gjort

- Regionen ligger **utanför** den `key`-remountade Suspense-gränsen — den enda placering som överlever
  varje sökning; testet binder på **nod-identitet**, inte på närvaro.
- Browse-all-grenen uppfinner inget tal och pinnas med `not.toHaveTextContent(/\d/)` — #1149 är orörd.
  Noll-träff-fallet får för första gången ett utrop alls.
- Deltat rör ingen färg, radie, skugga eller border: noll renderad pixel, båda teman opåverkade.
  `sr-only`-`<p>`:n triggar ingen sibling-selektor i `globals.css`.

## Sammanfattning

**0 Blocker, 0 Major, 5 Minor — merge hålls inte.** Fynd 1, 2 och 5 stängs **mekaniskt genom
strykning** (§9.6: en fix lägger inte till en påståendemening). Fynd 3 är en enradig sträng i två
katalogfiler. Fynd 4 ska filas, inte fixas här. Re-review vid behov: samma agent, report-only, scopad
till fix-deltat (CLAUDE.md §9.6).

**Ingen eskalering till Klas.** Hans verdikt 2026-08-24 (Major, in-block, räkningen ska annonseras)
är levererat.
