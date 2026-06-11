# senior-cto-advisor — E2g: hero-ö-state-synk + recent-search-labels (dom 2026-06-11)

**Datum:** 2026-06-11 · **Roll:** decision-maker. Underlag: Klas bugg-rapport + screenshot (chip-× rensade filtret men popovern visade länet markerat) + Klas label-direktiv (Senaste sökningar).

## Beslut (punkt 1 — stale filter-state i hero-ön)

**Variant A — `useOptimistic` med props som bas.** Klas-STOPP-klassning: **ingen STOPP** — entydigt motiverat buggfix-mekanikval inom pågående fas (§9.2/§9.6); CC implementerar direkt.

### Motivering mot principer

- **DRY / single source of truth (Hunt/Thomas 1999):** buggen är en dubblerad knowledge piece — "valda filter" lever både i URL:en (sanningen, ADR 0042) och i öns `useState` (kopia som aldrig synkas). `useOptimistic` eliminerar kopian som självständig sanning: props (ur URL) är kanonisk bas, det optimistiska värdet är ett medvetet transient overlay som garanterat konvergerar mot basen när transitionen landar. B–D lappar symptomet; A tar bort felklassen.
- **SoC (Martin 2017 kap. 7):** URL = filtersanning, RSC = data, ön = interaktionslatens-maskering. C återinför en parallell state-maskin med egen synk-logik (ny change-reason — SRP-smitta).
- **Framework-idiomatik:** React 19:s `useOptimistic` är designat för exakt detta; repo:t har redan aktivt valt bort C-mönstret (`react-hooks/set-state-in-effect` refereras som förbjudet i typeahead + popover). Mastercard-testet: A.

### Avvisade

- **B (key-remount):** varje egen toggle remountar ön och STÄNGER POPOVERN — förstör live-commit-UX:en (F6 P4 B1 var skälet ön ligger utanför Suspense). En fix som bryter featurens designkrav är ingen fix.
- **C (props→state-synk i effect):** bryter aktiv lintregel + Reacts dokumenterade anti-pattern; behåller dubbel sanning. Snabblösning förklädd — avvisas.
- **D (ren derive-from-props):** regresserar medvetet dokumenterad UX (toggles tröga tills RSC landat — varför lokal state infördes).

### Implementation-krav (in-block, samma commit)

1. Bas = `{occupationGroup, region, municipality}` ur props; optimistisk uppdatering INUTI samma `startTransition` som `router.push`.
2. **Alla** läsare byter till optimistiska värdet — `ortCount`, pill-badges, `facetFilter`, `push()`-argumenten. Halvmigrerad läsning återskapar buggen i annan yta.
3. Kör sviten (jsdom: mockad router → fallback till initial-props efter transition — enkel-interaktions-flöden bör hålla) + **nytt test för själva buggen**: rerender med nya props (extern URL-ändring) → popover/pills speglar nya props.

## Punkt 2 — recent-search-label (Klas-direktiv; mekanik bekräftad)

- **(a) Tree-uppslag i list-handlern: JA** — `ITaxonomyReadModel.GetTreeAsync` är in-memory-snapshot (ingen extern hop, ADR 0043). Bygg fält→grupp-set-lookup EN gång per `Handle`, inte per rad.
- **(b) Regeln i handlern: JA** — label är read-model-presentation (ADR 0039 Beslut 3-testet), konsistent med att `DeriveLabel` redan bor där. Ingen VO, ingen persistens.
- **(c) Fallgropar:** helt område + extra grupper → behandla som (iii) räknat på GRUPPER (Klas-förslaget bekräftas; blandade enheter i "+N" vore missvisande); två hela områden → också (iii), ej specialfall; "exakt alla grupper" = MÄNGD-likhet; taxonomi-drift → graceful degradering till (iii), jämför mot trädet aldrig hårdkodade antal; "{första}" måste vara deterministisk (resolvad label-ordning); region-fallbacken får samma +N-mönster (trivial extrapolering av direktivet); `DeriveLabel`-testerna utökas med (i)/1-grupp/(iii)/blandfallet/kommun-+N.

## Genuina TDs
Inga — båda punkterna in-block i E2g (§9.6).

## Referenser
Hunt/Thomas (1999) DRY; Martin (2017) kap. 7; React 19 docs (useOptimistic, "You Might Not Need an Effect"); ADR 0039 Beslut 3, ADR 0042, ADR 0043; CLAUDE.md §9.6.
