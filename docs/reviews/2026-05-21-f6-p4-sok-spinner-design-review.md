# Design-review: /jobb sök-laddningsindikator (F6 P4)

**Status:** ✓ Approved (re-review 2026-05-21 — se "Re-review-utfall" nedan)
**Granskat:** 2026-05-21 (rond 1) · 2026-05-21 (re-review)
**Granskare:** design-reviewer (veto-mandat per DESIGN.md §12)
**Auktoritet:** DESIGN.md §1, §3, §5, §6, §9, §10 + jobbpilot-design-{principles,components,a11y,copy,tokens}

> **Re-review-utfall (2026-05-21):** B1 + M1 + M2 + Mi2 verifierade åtgärdade.
> VETO hävt — GO. Full re-review-rapport längst ned i dokumentet.

## Granskat scope

- `web/jobbpilot-web/src/components/job-ads/job-ad-list-skeleton.tsx` (ny)
- `web/jobbpilot-web/src/components/job-ads/job-ad-list-skeleton.test.tsx` (ny)
- `web/jobbpilot-web/src/app/(app)/jobb/loading.tsx` (ny)
- `web/jobbpilot-web/src/app/globals.css` — `.jp-skeleton` / `.jp-job-skeleton`-block
- Visual-verify-korpus: `jobb-loading-skeleton__{light,dark}__{1280,1920}.png`
- Referens: `(app)/jobb/page.tsx` (interaktionspath, Area 5)

---

## FAS-DEFERRAL-MANIFEST

Denna review utfärdar INGEN rendered-veto som skjuter arbete till framtida fas.
Alla fynd nedan hör till F6 P4:s egen leverans-yta (laddningsindikatorn för
`/jobb`) och ska åtgärdas in-block i denna fas per CLAUDE.md §9.6. Inget fynd
deferreras. Manifestet är därmed tomt — ingen fas-deferral begärs.

---

## Blockers (måste fixas innan merge)

### B1 — `loading.tsx` raderar hero + toolbar under varje sökning (Area 5, flödesbegriplighet)

Fil: `web/jobbpilot-web/src/app/(app)/jobb/loading.tsx`

`loading.tsx` är en **route-segment Suspense-fallback** — Next.js App Router
ersätter HELA `/jobb`-segmentets renderade output med den här filen medan
`getJobAds()` körs. Den renderar enbart `JobAdListSkeleton` i en bar
`jp-container jp-page`-wrapper.

Men `/jobb/page.tsx` renderar två ytor ovanför resultatlistan som ligger
*innanför samma route-segment*:

1. `<section className="jp-hero">` — sökfältet (`#jobb-q`), titel
   "Sök bland aktiva annonser", lede, och hero-filter-pills (Ort/Yrke).
2. `JobbResultsToolbar` — träffräknaren ("N träffar"), aktiva filter-chips,
   sorterings-dropdown.

Konsekvens vid en sökning (det exakta flöde F6 P4 säger sig lösa):

- Användaren skriver i sökfältet och trycker Sök. Sökfältet hen just
  interagerade med **försvinner** medan resultatet laddas (upp till 40 s).
- Träffräknaren och de aktiva filter-chipsen försvinner samtidigt.
- Sidan kollapsar från full hero-layout till sex skeleton-rader högt upp,
  och hoppar tillbaka när resultatet landar — en grov layout-shift, motsatsen
  till F6 P4:s uttalade mål ("ingen layout-shift när annonser landar").

Detta bryter mot Norman/Boeke (system status ska vara förankrad till nuläget,
inte ersätta hela kontexten) och GOV.UK/Krug (en förstagångsanvändare som
ser sin sökterm och hela sök-ytan försvinna i 40 s vet inte om sökningen
pågår, om sidan kraschat, eller om hen ska söka om). Skeleton-komponentens
egen kommentar säger att ytan "inte hoppar när riktiga annonser landar" —
det stämmer för listraderna isolerat, men är falskt för sidan som helhet
eftersom hela hero-blocket pendlar in/ut.

`role="status"`-annonsen "Söker bland annonser…" mildrar det för skärmläsare
men löser inte den visuella kontextförlusten för seende användare.

**Krävs:** laddningsindikatorn får inte ta ner hero-sökytan och toolbar:en.
Konkret omstrukturering (nextjs-ui-engineer väljer mekanik, men resultatet
måste vara att hero + sökfält står kvar synligt och stabilt under
laddningen):

- Alternativ A: ta bort `loading.tsx` och flytta Suspense-gränsen *inåt* —
  låt `page.tsx` rendera hero-sektionen synkront och wrappa endast
  resultatdelen (`renderResult(...)` / `JobAdList`) i en `<Suspense>` med
  `JobAdListSkeleton` som `fallback`. Då står hero + toolbar kvar; bara
  listan byts mot skeleton.
- Alternativ B: om `loading.tsx`-mekaniken behålls måste den återge hela
  sid-chromet (hero + en toolbar-platshållare) ovanför skeleton-listan så
  layouten är identisk med den laddade sidan. Detta dubblerar markup och
  är bräckligare — Alternativ A är att föredra.

Detta är en task-completion-blocker per ADR 0047: en förstagångsanvändare
kan inte med säkerhet avgöra att sökningen pågår när sökytan hen just använde
försvinner. Område 5 granskas mot interaktionspath, inte bara statisk
skärmbild — skärmbilderna i korpusen visar fallbacken isolerat och döljer
just denna defekt.

---

## Major (bör fixas innan merge)

### M1 — Skeleton-listan saknar toolbar-/träffräknar-platshållare (Area 5, status)

Fil: `web/jobbpilot-web/src/components/job-ads/job-ad-list-skeleton.tsx`

Även när B1 är löst (hero kvar): den laddade `/jobb`-vyn har en
`JobbResultsToolbar` ovanför listan med "N träffar". Skeleton-fallbacken
hoppar direkt till listraderna. När resultatet landar skjuts listan ner av
toolbar-raden som dyker upp — en mindre men reell layout-shift, och
träffräknaren (systemstatus: "hur mycket hittades") saknas helt under väntan.

Detta löses naturligt om B1 åtgärdas via Alternativ A (toolbar renderas
synkront utanför Suspense-gränsen). Om Alternativ B väljs: lägg en
neutral platshållar-rad för toolbar-höjden i skeleton-markupen.
Klassificerat Major, inte Blocker, eftersom kärnuppgiften fortfarande är
begriplig — men status ("antal träffar") och layout-stabilitet är degraderade.

### M2 — `JobAdListSkeleton` saknar `aria-hidden`-konsekvens mot ledande `<span>`

Fil: `web/jobbpilot-web/src/components/job-ads/job-ad-list-skeleton.tsx:39-41`

Wrappern bär `aria-labelledby="jobb-laddar-text"` OCH innehåller
`<span id="jobb-laddar-text" className="sr-only">`. Det fungerar, men `id`:t
`jobb-laddar-text` är globalt och inte komponent-scopat. Om två
`JobAdListSkeleton` någonsin renderas samtidigt (t.ex. om komponenten
återanvänds som inline-fallback på annan yta i framtiden) blir `id`:t
duplicerat — ogiltig HTML och oförutsägbar accessible-name-resolution.

För nuvarande enda användning (route-segment-fallback, en instans) är detta
inte en aktiv bugg, men `JobAdListSkeletonProps` exponerar redan `rows` som
återanvändbar parameter och komponent-doc:en beskriver den som generell.
**Krävs:** scoping av `id` via `useId()` (React 18+), eller — enklare och
likvärdigt a11y-mässigt — slopa `<span>` + `aria-labelledby` och sätt
`aria-label="Söker bland annonser…"` direkt på `role="status"`-wrappern.
`aria-label` på en `role="status"`-region ger samma accessible name utan
DOM-nod och utan `id`-kollisionsrisk. Testet
`getByRole("status", { name: "Söker bland annonser…" })` fortsätter passera.

---

## Minor (nice-to-fix, inte blocker)

### Mi1 — Skeleton-blockens kontrast mot kort-ytan är låg i light mode

`.jp-skeleton` använder `--jp-surface-3` (`#E8EDF4` light) på `.jp-job-skeleton`
som har `--jp-surface` (`#FFFFFF`). Kontrasten är ~1.1:1. Detta är **tillåtet**
— skeleton-blocken är `aria-hidden` och rent dekorativa, så WCAG 1.4.11
(3:1 för UI-komponenter) gäller inte dem (jämför disabled-element-undantaget i
a11y-skillen §4). Men i light-1920-skärmbilden är platshållarna nästan osynliga
mot vitt på en stor skärm. Civic-utility kräver inte högre kontrast här, men
överväg `--jp-surface-2` → en aning mörkare fyllyta, eller behåll
`--jp-surface-3` och acceptera den dämpade looken. Inget krav — flaggas
för medvetet val. Dark mode ser bra ut (`#283C5E` på `#1B2B47` läsbart i
skärmbild).

### Mi2 — `rows`-prop exponeras men har ingen anropare som använder den

`JobAdListSkeletonProps.rows` finns och testas (`rows={3}`), men enda
produktionsanropet (`loading.tsx`) använder default 6. YAGNI — propen är
billig och väl-dokumenterad, så detta är endast en notering, ingen åtgärd
krävs. Om B1 löses via Alternativ A kan en framtida inline-Suspense med
färre rader motivera propen.

---

## Bedömning: känd avvikelse — inline svensk copy vs CLAUDE.md §5.2 next-intl

**Bedömning: acceptabelt. Ingen åtgärd i denna komponent.**

CLAUDE.md §5.2 förbjuder hårdkodade strängar och föreskriver `next-intl` med
`messages/sv.json`. Implementören har verifierat att projektet inte har
`next-intl` installerat och att hela kodbasen använder inline svensk copy
(bekräftat: `page.tsx` har inline "Sök bland aktiva annonser", felmeddelanden
m.m. direkt i JSX).

Att införa `next-intl` enbart för en skeleton-komponents enda sträng vore en
arkitektur-ändring utan mandat — den hör inte till F6 P4:s scope och skulle
skapa en inkonsekvent kodbas (en fil i18n-iserad, resten inte). Per
CLAUDE.md §9.6 är detta en genuin annan-fas-fråga: i18n-infrastruktur är ett
eget arbetspaket. Att följa det faktiska, konsekventa kodbas-mönstret
(inline svenska) är rätt val här.

**Dock:** detta är en latent skuld, inte en upplöst fråga. Strängen
"Söker bland annonser…" är korrekt civic-utility-copy (se nedan) och behöver
inte ändras — men i18n-gapet bör fångas som TD om det inte redan finns en
post för avsaknad `next-intl`-infrastruktur. Verifiera mot `docs/tech-debt.md`;
ligger ansvaret hos nextjs-ui-engineer/Klas, inte denna review.

---

## Verifierat korrekt (Area 1–4)

### Civic-utility-estetik (Area 1) — godkänt

- Skeleton-rader, inte spinner — korrekt enligt jobbpilot-design-components
  ("full row skeletons, not spinner" / "prefer Skeleton over Spinner for
  first renders").
- Ingen shimmer, ingen puls, ingen glow, ingen gradient — `.jp-skeleton` är
  rent statisk DOM. Bekräftat i CSS (inga `@keyframes`, inga animationer på
  blocken) och i alla fyra skärmbilder.
- Platt neutral fyllyta via `--jp-surface-3` — korrekt token, "papper inte
  glas" (principles regel 1).
- Inga AI-clichéer: ingen emoji, inga "✨/🚀", inget glas, inga neon-accenter,
  ingen lila/cyan, ingen `shadow-2xl`.
- `.jp-job-skeleton` speglar `.jp-job`-radens mått: `padding: 18px 22px`,
  `border: 1px solid var(--jp-border)`, `border-radius: var(--jp-r-md)` (6px) —
  identiskt med `.jp-job`-chassit. Listraderna i sig hoppar inte. (Sid-nivå-
  hoppet är B1, separat fynd.)
- Radius: `--jp-r-sm` (4px) på inner-blocken, `--jp-r-md` (6px) på chassit —
  båda inom 6px-gränsen.
- Eftersom inget animeras behövs ingen `prefers-reduced-motion`-särregel —
  korrekt resonemang, CSS-kommentaren noterar det.

### Design tokens (Area 2) — godkänt

- Enbart `--jp-*`-tokens: `--jp-surface-3`, `--jp-surface`, `--jp-border`,
  `--jp-r-sm`, `--jp-r-md`. Inga hårdkodade hex, inga Tailwind-default-färger
  (`slate-*`/`gray-*`/`zinc-*`).
- Light + dark följer `--jp-*`-kaskaden automatiskt — verifierat i båda
  skärmbild-paren.

### Tillgänglighet (Area 3) — godkänt (med M2 som hygien-notering)

- `role="status"` + `aria-live="polite"` + `aria-busy="true"` på wrappern —
  korrekt mönster för en Suspense-fallback (a11y-skill §6, "Status/count
  updates").
- `aria-live="polite"` (inte `assertive`) — korrekt, en laddning är inte ett
  kritiskt avbrott.
- Skeleton-`<ul>` bär `aria-hidden="true"` — de dekorativa blocken läses inte
  upp som tomma element; skärmläsaren får en kort mening, inte brus.
- Inga interaktiva element i fallbacken → ingen fokus-/tab-ordnings-påverkan,
  ingen `outline: none`-risk.
- 5 vitest-tester täcker live-region, accessible name, default + explicit
  radantal och `aria-hidden` — meningsfull testtäckning.

### Svensk copy (Area 4) — godkänt

- "Söker bland annonser…" — du-ton implicit, saklig, kort, inget utropstecken,
  ingen emoji. Korrekt Unicode-ellips (`…`), inte tre punkter — exakt
  enligt jobbpilot-design-copy §4 ("Loading"). Matchar mönstret
  "Hämtar jobbannonser…" i a11y-skillen.
- Kommentarer i kod är sakliga och refererar rätt skills.

---

## Sammanfattning

**1 Blocker, 2 Major, 2 Minor.**

Skeleton-komponenten själv (`JobAdListSkeleton`) är civic-utility-trogen,
token-ren, a11y-medveten och har korrekt svensk copy — isolerat sett ett bra
hantverk. Problemet ligger i **integrationen**: `loading.tsx` som
route-segment-fallback raderar hela sök-hero:n och träffräknaren under varje
sökning, vilket är den exakta kontextförlust F6 P4 sade sig lösa (B1, ADR 0047
task-completion-blocker). Detta syns inte i de levererade skärmbilderna
eftersom de visar fallbacken isolerat — det framkommer först när
interaktionspathen granskas, vilket är varför Area 5 kräver path-granskning.

**Verdikt: VETO — changes requested.** Blockas på B1. M1 + M2 ska
adresseras i samma batch. Re-review krävs när B1 (Suspense-gräns flyttad
inåt, eller hero återgiven i fallbacken) + M1 + M2 är åtgärdade. Delegeras
till nextjs-ui-engineer. Alternativ A för B1 rekommenderas (flytta
`<Suspense>` in i `page.tsx` runt resultatdelen, slopa `loading.tsx`).

---

# Re-review (2026-05-21)

**Status:** ✓ Approved — VETO hävt
**Granskat scope (rond 2):**

- `web/jobbpilot-web/src/app/(app)/jobb/page.tsx` (ändrad)
- `web/jobbpilot-web/src/components/job-ads/jobb-results.tsx` (ny)
- `web/jobbpilot-web/src/components/job-ads/job-ad-list-skeleton.tsx` (ändrad)
- `web/jobbpilot-web/src/components/job-ads/job-ad-list-skeleton.test.tsx` (ändrad)
- `web/jobbpilot-web/src/app/globals.css` — `.jp-skeleton--count` / `.jp-skeleton--sort`
- `web/jobbpilot-web/src/app/(app)/jobb/loading.tsx` — verifierad BORTTAGEN
- Visual-verify: `jobb-loading__{light,dark}__{1280,1920}.png`
- `jobb-results-toolbar.tsx` (referens — layout-paritet, Area 5)

## FAS-DEFERRAL-MANIFEST

Re-review utfärdar ingen rendered-veto och ingen fas-deferral. Inget GO
villkoras mot framtida fas. Manifestet är tomt.

## Blockers — verifierade åtgärdade

### B1 — LÖST. `loading.tsx` raderar inte längre hero under sökning

Verifierat mot interaktionspath (Area 5, ej bara statisk skärmbild):

- `loading.tsx` är borttagen. `Glob` av `(app)/jobb/**` returnerar enbart
  `page.tsx` + `[id]/page.tsx` — ingen route-segment-fallback finns kvar.
- `page.tsx` renderar `<section className="jp-hero">` (titel, lede, GET-form
  med `#jobb-q`, `JobbHeroFilters`) SYNKRONT, utanför Suspense-gränsen.
  Endast `<JobbResults>` är wrappad i `<Suspense fallback={<JobAdListSkeleton/>}>`.
- `JobbResults` är en ny `async` Server Component som `await`:ar `getJobAds()`
  + `resolveTaxonomyLabels()` internt — den enda data-beroende ytan ligger
  därmed innanför gränsen, allt hero-chrome utanför. Detta är Alternativ A
  ur rond 1, den rekommenderade lösningen.
- Suspense-`key` = `${resultsKey}|${ssykKey}|${regionKey}`, härledd ur
  searchParams → fallbacken visas även vid `/jobb`→`/jobb`-navigering, inte
  bara vid första load.
- Skärmbild-evidens (`jobb-loading__light/dark__1280/1920`): under en aktiv
  sökning står hero kvar med söktermen "systemutvecklare" synlig i `#jobb-q`.
  Endast resultatytan visar skeleton. Den kontextförlust som var B1 (sökfältet
  användaren just använde försvinner) finns inte längre.

Norman/Boeke (status förankrad till nuläget) och Krug/GOV.UK (förstagångs-
användaren ser sin sökterm och söker-ytan stå kvar) är uppfyllda. ADR 0047
task-completion-kravet är mött.

## Major — verifierade åtgärdade

### M1 — LÖST. Skeleton har toolbar-/träffräknar-platshållare

- `JobAdListSkeleton` renderar `<div className="jp-results-toolbar">` med
  `.jp-skeleton--count` (vänster) + `.jp-skeleton--sort` (höger) ovanför
  6-rads-listan. Markup speglar `JobbResultsToolbar`-radens två sidor.
- CSS-paritet verifierad: `.jp-skeleton--sort { height: 40px }` matchar
  `.jp-select { height: 40px }`; `.jp-skeleton--count { height: 20px }` mot
  `.jp-results-count` (`font-size: 17px`). Platshållaren bär samma
  `.jp-results-toolbar`-container (`margin-top: 24px`, `space-between`) som
  den laddade toolbaren → ingen layout-shift när data landar.
- Skärmbild bekräftar: platshållarraden syns ovanför listraderna i alla fyra
  capturer, vänster/höger-placering korrekt.
- Korrekt designval att toolbaren ligger innanför Suspense-gränsen:
  träffräknaren är data-beroende (`totalCount` + chip-labels) och kan inte
  visa rätt antal innan `getJobAds()` landat. `jobb-results.tsx`-doc:en
  motiverar detta tydligt.

### M2 — LÖST. Ingen global DOM-id, accessible name via `aria-label`

- `<span id="jobb-laddar-text">` är borttagen. `role="status"`-wrappern bär
  `aria-label="Söker bland annonser…"` direkt.
- Verifierat: `JobAdListSkeleton` innehåller inget `id`-attribut alls.
  Regressionstestet `container.querySelector("[id]")` → `null` befäster att
  flera samtidiga instanser inte kan kollidera.
- `aria-label` på `role="status"`-region ger samma accessible name som det
  tidigare `aria-labelledby`-mönstret, utan DOM-nod. Testet
  `getByRole("status", { name: "Söker bland annonser…" })` passerar
  fortfarande (verifierat i testfilen).

## Minor

### Mi2 — LÖST. Oanvänd `rows`-prop borttagen

`JobAdListSkeletonProps` finns inte längre — antalet rader är en lokal
`SKELETON_ROWS = 6`-konstant. Komponenten tar inga props. YAGNI åtgärdad.
Testet `rows={3}` är borttaget; `renders six skeleton rows` täcker default.

### Mi1 — kvarstår som medvetet val (ingen åtgärd krävd)

Skeleton-blockens kontrast i light mode (`--jp-surface-3` på `--jp-surface`)
flaggades rond 1 som tillåten (blocken är `aria-hidden`, dekorativa — WCAG
1.4.11 gäller ej). Skärmbilderna `__light__1280/1920` bekräftar den dämpade
men närvarande looken; dark mode (`__dark__`) läsbar. Inget krav. Stängd.

## Area 1–4 — omverifierat på den ändrade ytan

- **Civic-utility (Area 1):** skeleton-rader, inte spinner. Ingen shimmer/
  puls/glow/gradient — `.jp-skeleton` rent statiskt. Inga AI-clichéer.
  Radius `--jp-r-sm`/`--jp-r-md` inom 6px. Bekräftat i CSS + alla 4 capturer.
- **Tokens (Area 2):** enbart `--jp-*`. Nya `.jp-skeleton--count/--sort` är
  rena geometri-regler (height/width), inga färger — ärver `.jp-skeleton`s
  `--jp-surface-3`. Inga hårdkodade hex, inga Tailwind-default-färger.
- **A11y (Area 3):** `role="status"` + `aria-live="polite"` + `aria-busy`.
  Skeleton-`<ul>` och toolbar-platshållaren `aria-hidden="true"` →
  skärmläsaren får en kort mening, inte tomt brus. Inga interaktiva element i
  fallbacken → ingen fokus-/tab-påverkan. 6 vitest-tester (105/105 totalt i
  job-ads). Light + dark verifierade.
- **Svensk copy (Area 4):** "Söker bland annonser…" — du-ton, saklig, kort,
  inget utropstecken, ingen emoji, korrekt Unicode-ellips.

## Area 5 — task-completion / flödesbegriplighet

Walk av interaktionspathen (sökning → fallback → resultat):

1. Användaren skriver i `#jobb-q` och trycker Sök → hero med söktermen står
   kvar synlig (B1 löst). System-status är förankrad till nuläget.
2. Resultatytan byts mot skeleton som speglar toolbar + lista → ingen
   layout-shift in eller ut (M1 löst).
3. Skärmläsare får "Söker bland annonser…" via polite live-region.
4. Resultat landar i samma geometri som skeleton:en upptog.

Kärnuppgiften (söka och se att sökningen pågår) är genomförbar utan
gissning. Inga irreversibla åtgärder. Inga sammanflätade formulär.

## Sammanfattning

**0 Blockers, 0 Major, 0 öppna Minor.** B1 + M1 + M2 + Mi2 verifierade
genuint åtgärdade — sökfält och filter förblir synliga under sökning, ingen
layout-shift, ingen DOM-id-kollision. Civic-utility och WCAG 2.1 AA bekräftade
i light + dark. Idiomatisk Next.js streaming-pattern (Suspense-gräns inåt,
async resultat-komponent) — `jobb-results.tsx` är väldokumenterad och bär
motiveringen för var data-gränsen går.

**Verdikt: GO. VETO hävt.** Mergeklar.

**Notering (utanför F6 P4-scope, ej blockerande):** Ort/Yrke-pills
(`JobbHeroFilters`) syns inte i de fyra visual-capturerna trots att
`.jp-hero__pills` renderas synkront i hero. Bedöms som capture-artefakt, inte
en F6 P4-regression — pills är hero-beroende och ligger korrekt utanför
Suspense-gränsen. Rekommenderas att nextjs-ui-engineer dubbelkollar att
pills-raden faktiskt renderar i `/jobb` light/dark vid nästa visual-verify-
pass; ingen åtgärd krävs inom denna review.
