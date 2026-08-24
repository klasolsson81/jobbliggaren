# code-reviewer — PR #1510 (#1505), SCOPAD OMKONTROLL

- **Date:** 2026-08-24
- **PR:** [#1510](https://github.com/klasolsson81/jobbliggaren/pull/1510)
- **Graderat delta:** `7adb1a0f..1ba31db7` (10 filer, +133/−46). Verdikt utfärdat mot **`282ae693`**.
- **Verdikt:** ✓ **Approved**
- **Kvot:** förbrukad. *"En fix som landar efter detta går till CTO-routning, inte till mig."*
- **Transcribed by the driving session** — charter grants `Read`/`Grep`/`Glob`, no `Write`.

## Verdikt per fynd

### 1. Major 1 (e2e-lokatorn räknade fel) — **STÄNGT**

Strukturell skopning verifierad i källan, inte bara i diffen: `page.tsx:365` renderar `<section aria-labelledby="jobb-results-title">`, `Announcer`-provider på `:378` ligger **inuti** den, och dess `<p role="status" aria-live="polite" aria-atomic="true" class="sr-only">` (`announcer.tsx:43`) är sektionens enda träff för `LOAD_REGION`. Skeleton och `JobAdList` bär noll `[aria-live]` (pinnat), och toolbarens `jp-matchsort-note` (`jobb-results-toolbar.tsx:600`) har `role="status"` men varken `aria-live`-attribut eller `sr-only` → matchas inte. Räkningen är alltså 1 både före och efter hydration — **den kan inte längre racea.**

Docblockets tre sifferpåståenden mätta och korrekta: `jobb-hero-search.tsx:668`, `job-ad-typeahead.tsx:287` (bakom `hydrated ?` på `:579`), `header-stats.tsx:219` (`<span>`, inte `<p>` — stämmer). Inga dinglande `FILTER_REGION`-referenser kvar.

### 2. NY I DELTAT: Major — **STÄNGT MEKANISKT vid `282ae693`**

*Fälld och redan lagad när den mättes. Rapporteras för verdikt-tabellens skull.*

`tests/e2e/jobb-live-region.spec.ts:50-52` (vid `1ba31db7`): `// Wait for hydration before counting anything…` + `await expect(page.getByLabel(SEARCH_FIELD_LABEL)).toBeEnabled();` — **den väntade inte på hydration.** Båda armarna i `jobb-hero-search.tsx:579` renderar `id="jobb-q"` (typeahead `job-ad-typeahead.tsx:260-261`, no-JS-fallbacken `:594`), ingendera bär `disabled`, och `<label htmlFor="jobb-q">` finns i server-HTML:en. `toBeEnabled()` löser alltså mot pre-hydration-DOM:en direkt.

*"En kontroll som ser ut att mäta något och mäter noll — **samma defektklass som Major 1**, och kommentaren namngav en grind koden inte har"* (§5 `Comments:`, charter: faktafel i kommentar = Major).

Utfört i `282ae693`: raderna borta, positiv kontroll bytt till `section.jp-hero p[aria-live='polite'].sr-only`. Verifierat: `page.tsx:280` är `<section className="jp-hero">` och omsluter `<JobbHeroSearch>` på `:307`, vars region på `:668` **inte** är `hydrated`-grindad → count ≥ 1 i båda tillstånden. Race-fri utan väntan. `SEARCH_FIELD_LABEL` används fortfarande (`:74`) → ingen oanvänd konstant.

### 3. Major 2 (`jobb-results-toolbar.test.tsx`) — **STÄNGT**

*Strykningen:* `finns nu TVÅ role=status` och `grad-filtrets hjälprad` greppar till noll i `src`. Rätt beslut att stryka snarare än räkna om — `jobb-hero-search.tsx:329` säger att hjälpraden inte längre är en egen `role="status"`, så meningen var stale i två led.

*Tillägget (`:64-71`):* godkänt. `not.toHaveAttribute("role")` fäller på återinförd `role="status"`; M11 KILLED är konsistent med koden. Kommentarens påstående mätt sant: `jobb-results.test.tsx:82-83` stubbar `JobbResultsToolbar: () => null`. Att ett tillägg gör den icke-mekanisk är rätt hanterat: **en `assertion` är kod, inte en påstående-mening, så §9.6:s "aldrig en claim-sentence" är inte brutet.**

### 4. Major 3 (`job-ad-list.tsx:65-67`) — **STÄNGT**

Kommentaren pekar nu på `page.tsx`:s `Announcer` och på att `jobb-results.tsx` skjuter in de två meningarna. Verifierat mot källan. Även den omskrivna kommentaren i `jobb-results.tsx:402-405` är faktakorrekt.

### 5. Minor 4 (skelettpinnen) — **STÄNGT**

`createTranslator` + `t("skeleton.searching")`. Nyckeln finns (`messages/sv/jobads.json:160-161`), innehållet oförändrat, en omdöpt nyckel fäller nu här. Form-paritet med systerfilen verifierad rad för rad.

### 6. Minor 5 (indentering) — **INTE STÄNGT. Skippen accepteras.**

Deltat rör inte `page.tsx`. Ren kosmetik: ingen Prettier på web (§11), ESLint grön, *"en JSX-indentering inuti en enda ruttfil är osynlig för en parallell lane per konstruktion — precis den §9.6-skip som får namnges i PR-kroppen."*

### 7. Minor 6 (kommentartäthet) — **INTE STÄNGT (delvis). Skippen accepteras.**

`loading.tsx:24` står kvar; deltat rör inte filen. Om de nya kommentarerna: **ingen korsar gränsen till omargumentering.** `loading.test.tsx:43-64`:s "Measured —"-stycken är sådant koden inte kan visa och är daterade historiska mätningar → §5-tillåtet. Enda observationen: e2e-docblockets *"Three reviewers found it independently"* är granskningsnarrativ som commit-meddelandet äger — **frasering, alltså inget fynd i en omkontroll.**

## Bra gjort

- `4205f6ed` fångade en följdskada mitt eget skop inte täckte: `emptyTitle` i live-regionen gjorde `getByText("Inga jobb hittades")` till en strict-mode-violation i `jobb.spec.ts:162,180`. Svepet är komplett.
- SSR-pinnen mäter rätt sak: den kräver att regionen är **tom**, inte att en viss sträng saknas — den distinktionen är vad som gör mutationen dödad i stället för överlevande.
- Att `role="alert"`-strykningarna fick pinnar innan de fick vila. De två assertionerna är dokument-breda; det mäter rätt idag men blir spröt om någon annan legitim alert landar i samma render — värt en rad i huvudet, inget mer.

## Process — läs före `agents-done`

> HEAD flyttade **två gånger under denna rapport-läges-omkontroll**: `4110607f` → `4205f6ed` → `282ae693`. Jag graderade `7adb1a0f..1ba31db7` som ombett och mätte därefter båda commits eftersom de rör samma filer. Inget öppet fynd återstår vid `282ae693`.
> §6 kräver att HEAD verifieras oförändrad omedelbart före `agents-done` — **mät om**, för HEAD var inte stabilt medan panelen svarade.

## Sammanfattning

3 Major stängda · 1 ny-i-deltat Major fälld och redan stängd mekaniskt · 1 Minor stängd · 2 Minor som accepterade namngivna skips.
