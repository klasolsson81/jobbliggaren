# senior-cto-advisor — E2c facet-counts: rate-limit + FE-scope (natt-dom 2026-06-11)

**Datum:** 2026-06-11
**Roll:** decision-maker (CLAUDE.md §9.6). Underlag: `docs/reviews/2026-06-11-sok-paritet-e2c-architect.md` (VAL-frågor §4/§5 + kalibrerings-fynd §6), egen E2b-dom VAL 4 (`-e2b-cto.md`), ADR 0067 Beslut 4 + Beslut 7 rad 102/109, `RateLimitingOptions.cs` + `RateLimitingExtensions.cs` on-disk, `FacetCountsScenarios.cs` on-disk, ADR 0045 Beslut 1.

---

**Klas-STOPP-klassning: INGEN HALT.** Båda valen ryms inom Accepted ADR 0067-mandat + natt-auktoriteten:

- **VAL 1** är ett beslut som redan är **fördelegerat till CTO i skrift**: D1-CTO-domen 2026-06-10 lät rate-limit-taket stå öppet med "tas med FE-vyn i E" (citerat i `FacetCountsScenarios.cs` rad 29 och 43). Detta ÄR den punkten. Policy-utbrytning är dessutom etablerad mekanik-klass: SuggestPolicy (CTO 2026-05-16) och TaxonomyReadPolicy (CTO MAP-3 2026-05-17) skapades båda via CTO-dom utan ADR-amendment eller Klas-STOPP, med riktvärde + security-auditor-verifiering (BLOCKING) som kontrollmekanism. Samma mönster återanvänds.
- **VAL 2** *implementerar* Accepted-text snarare än utvidgar den — ADR 0067 Beslut 7 rad 109 listar explicit »live-count "Visa N annonser" per val« som Fas E-paritetskrav. Det är variant **B** som hade krävt Klas (avvikelse från Accepted-uppräkning), inte A.
- **Enda villkorliga Klas-STOPP:** NBomber-p95 > 300 ms → HALT med architects fallback-ordning (§7) — det är ADR 0045:s Klas-lås, inte denna dom. Flip av observe-only-gaten görs INTE (Klas-lås, ADR 0045 Beslut 6).

## Beslut — VAL 1: Rate-limit

**Variant B — egen `FacetCountsPolicy`, riktvärde 30/10s per user, IOptions-bunden.** Security-auditor verifierar/justerar talen i review (BLOCKING) — exakt samma kontrollform som Suggest/TaxonomyRead-prejudikaten.

### Motivering mot principer

- **Least common mechanism (Saltzer/Schroeder 1975):** dela inte skyddsbudget mellan ytor med olika legitim-frekvensprofil. Facet-profilen (client-side debounce-burst, 20–40 req/min under aktiv filtrering, Ort-popover ×2 parallella) avviker strukturellt från list-RSC-profilen (3–10 req/min normal, per ListRead-doc:ens egen kalibrering). Detta är inte analogi-resonemang — det är **ordagrant samma skäl** som står i `RateLimitingOptions.cs` doc-kommentarer för Suggest och TaxonomyRead. Att välja A här vore att bryta filens egen dokumenterade designregel.
- **Bulkhead-isolering (Nygard, *Release It!* 2nd ed):** architects svältnings-scenario är konkret och asymmetriskt allvarligt: 20 list + 40 facet = 60/min → 429, och det som stryps är **listan** — primärfunktionen degraderas av sin egen dekoration. Counts som svälter counts är acceptabel degradering; counts som svälter sökresultatet är ett feldomän-läckage.
- **Kalibrerings-aritmetik (riktvärdet 30/10s):** fixed-window 60/min absorberar inte burst — hela facet-minutens budget kan förbrukas i ett 10-sekunders-fönster av legitim toggling. 30/10s ger 3 req/s sustained ≈ 180/min tak: ×4–9 headroom över architects 20–40/min-profil, samtidigt som det kapar script-flod inom sekunder (samma rational som Suggest). Symmetrin med Suggest-talen är medveten — samma debounce-drivna (≥300 ms) klientprofil.
- **Säkerhets-klass oförändrad:** auth-gated GROUP BY mot ~44k rader = samma CWE-400/OWASP API4:2023-yta som ListRead skyddar; partitionering per UserId (claim "sub"), spegla befintligt block-mönster.

### Avvisat alternativ

**Variant A (återanvänd ListReadPolicy):** noll ny kod, men priset är ett dokumenterat svältnings-scenario i den riktning som skadar mest, plus ett brott mot filens egen utbrytnings-regel. "Policy-proliferation" väger lätt: detta är sjätte–sjunde policyn i en fil vars hela struktur är byggd för per-profil-policies med IOptions-bindning. Mastercard-testet: en utomstående granskare som läser doc-kommentarerna för Suggest/TaxonomyRead och sedan ser facet-ytan inkastad i ListRead skulle fråga varför regeln övergavs på exakt den yta där den är som mest motiverad.

### Konsekvens — NBomber-kalibreringen (architect §6): åtgärd (c)

VAL 1-B löser kalibrerings-felet **utan miljö-hack**: scenario 1 (1 RPS = 10 req/10s-fönster) + scenario 2 (0,5 RPS = 5 req/10s) parallellt = 15 req/10s i facet-bucketen — strikt under 30/10s, och list-bucketen träffas inte alls. Åtgärd (a) (höjt tak i loadtest-miljön) avvisas — att mäta mot annan konfig än prod-default förorenar fitness-funktionens utsaga (Ford/Parsons/Kua 2017: fitness function ska mäta systemet som det skeppas). Åtgärd (b) (sekventiellt) avvisas — onödig när (c) löser det strukturellt, och parallell-körningen är själva reflektion-signalens poäng. **CC uppdaterar scenario-kommentarerna** (LAST-KALIBRERING + scenario-kommentarerna) från "ListReadPolicy ~60/min"-antagandet till FacetCountsPolicy 30/10s med ny aritmetik — kommentarer som ljuger om taket är samma felklass som fel kod.

## Beslut — VAL 2: FE-scope

**Variant A — per-option-counts i popover-raderna + "Visa N annonser"-stängknapp som visar toolbarens redan-kända totalCount.**

### Motivering mot principer

- **Accepted-text är styrande:** ADR 0067 Beslut 7 rad 109 räknar upp »live-count "Visa N annonser" per val« som del av Fas E-paritets-detaljerna från Klas-referensen. A implementerar Accepted-beslutet; B avviker från det. Att i natt-läge välja bort en Klas-specad affordance på eget design-omdöme vore process-inversion — samma triage-logik som E2b VAL 1-domen.
- **Paritets-mandatet:** `project_platsbanken_parity_baseline` + ADR 0067-kontextraden. Platsbankens popover-stängning VIA count-knappen är inlärt interaktionsmönster för målgruppen; B:s "ärlighet" köps med en discoverability-regression i stäng-flödet.
- **Noll arkitektur-kostnad, SPOT bevarad (Hunt/Thomas 1999):** knappen renderar `PagedResult.TotalCount` som FE redan håller live — noll extra requests, noll ny backend-yta. Detta **fastställer samtidigt architects §1-dom**: ingen `Total` i facet-DTO:n.
- **Affordance-risken är en copy/design-yta, inte arkitektur:** knappen ljuger inte — den stänger popovern och listan visar N annonser. Risken "ser ut som commit" hanteras med mikrocopy/visuell behandling och ligger i **design-reviewerns gate**. CC flaggar punkten explicit i design-review-underlaget.

### Avvisade alternativ

- **Variant B:** löser förväxlings-risken genom att ta bort en Klas-specad paritets-affordance — att amputera kravet är inte en lösning på dess design-utmaning. Hade krävt Klas-amendment av Beslut 7 rad 109.
- **Variant C:** **avvisning FASTSTÄLLS formellt.** ADR 0067 Beslut 4 avvisar "full defer av per-option" ordagrant. C är falsk-klar mot rad 102/109.

### Trade-offs accepterade

- En funktionellt redundant knapp under live-commit-modellen — paritets-kostnad; redundansen är gratis och risken designhanteras i design-review.
- 30/10s är riktvärde utan prod-mätdata — security-auditor verifierar i E2c-review, revisit-trigger Fas 7+ ärvs.

## In-block-direktiv till CC

1. `RateLimitingOptions.FacetCounts` (30/10s) med doc-kommentar enligt Suggest-mallen + `FacetCountsPolicy`-konstant och block i `RateLimitingExtensions.cs` (per-UserId, spegla Suggest).
2. Endpoint per architect §1 (alla entydiga domar fastställs: egen route, `IsInEnum()`-validatorn, ingen `Total`, `private, no-store`).
3. NBomber-aktivering per footer-stegen + kommentar-omkalibrering enligt VAL 1-konsekvensen. Procedur-ordningen i architect §6 gäller: backend → lokal körning → p95 i PR-body → FÖRST därefter FE-live-wiring; p95 > 300 ms = HALT till Klas med architects fallback-ordning (§7).
4. FE Variant A per architect §5:s gemensamma mekanik (route-handler à la suggest, self-contained debounce-hook ≥300 ms + AbortController, zod `z.record`, tyst degradering). Flagga commit-förväxlings-risken explicit till design-reviewer.
5. Security-auditor obligatorisk (rate-limit-tal = BLOCKING-verifiering) utöver code-reviewer + dotnet-architect-gaterna.

## Genuina TDs (lyfts)

Inga. Allt är in-block i E2c.

## Referenser

- Saltzer/Schroeder (1975) — least common mechanism; Nygard, *Release It!* 2nd ed (2018) — bulkhead; Hunt/Thomas (1999) — SPOT; Ford/Parsons/Kua (2017) — fitness function mäter skeppad konfig; Fowler (2018) — Speculative Generality
- ADR 0067 Beslut 4 + Beslut 7 rad 102/109; ADR 0045 Beslut 1/6; ADR 0065; CLAUDE.md §1/§2.5/§9.6
- `src/JobbPilot.Api/RateLimiting/RateLimitingOptions.cs`, `perf/JobbPilot.LoadTests/Scenarios/FacetCountsScenarios.cs`, `docs/reviews/2026-06-11-sok-paritet-e2c-architect.md`, `docs/reviews/2026-06-11-sok-paritet-e2b-cto.md` (VAL 4)

**Sammanfattning för CC:** VAL 1 = B (FacetCountsPolicy 30/10s, riktvärde) → NBomber-åtgärd (c) med kommentar-omkalibrering; VAL 2 = A (counts + "Visa N annonser"-knapp på `PagedResult.TotalCount`); C-avvisningen fastställd; ingen Klas-HALT — enda villkorliga stoppet är p95-brott mot 300 ms-budgeten. Bygg inatt.
