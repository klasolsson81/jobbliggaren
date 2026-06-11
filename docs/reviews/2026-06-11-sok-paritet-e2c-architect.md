# dotnet-architect — Fas E2c: live facet-counts (ADR 0067 Beslut 4 + Beslut 7 rad 102/109)

**Datum:** 2026-06-11
**Scope:** Endpoint + Application-query + VAL 4-korrigering + NBomber-aktivering. Tre entydiga domar (endpoint-form, residual-parser-konsistens, total-utelämnande), två genuint multi-approach-frågor (rate-limit-policy, FE-scope) till senior-cto-advisor, samt två viktiga fynd: (a) dagens `ExcludeDimension` bryter mot CTO VAL 4 (känd, fixas i E2c), (b) NBomber-scenariots last-kalibrering är aritmetiskt fel mot ett 60/min-tak (90 req/min i samma user-bucket vid parallell-körning → 429-förorenad p95).

---

## 1. Endpoint-form och query-kontrakt

**Dom: egen `GET /api/v1/job-ads/facet-counts` — inte genuint multi-approach.** Param-på-list-endpointen avvisas på skrivna principer: CQRS en-handler-en-sak (polymorf response-form per query-param = kontrakts-smell + zod-förgrening); olika konsumenter/kadens (lista = RSC per router.push, facetter = client-side debounce — sammanslagning tvingar RSC betala GROUP BY per sidladdning); rate-limit-separation omöjlig på delad route; NBomber-instrumentet antar redan routen.

**Query-kontrakt (bekräftar NBomber-scenariots form):**

```
GET /api/v1/job-ads/facet-counts
    ?dimension=<OccupationGroup|Municipality|Region>   (obligatorisk)
    &occupationGroup=<id>...&municipality=<id>...&region=<id>...  (repeterade, valfria)
    &q=<text>                                          (valfri)
```

- **En dimension per request** (Ort-popovern gör 2 parallella). Batching = Speculative Generality.
- **Enum-bindning:** minimal-API binder namn case-insensitive; **OBS** `TryParse` accepterar numeriska strängar utanför definierad mängd (`?dimension=7` binder) → `RuleFor(q => q.Dimension).IsInEnum()` i validatorn **obligatorisk** (samma skäl som SortBy). Infrastructure-switcharnas throw är defense-in-depth, inte primärt skydd.
- **Validator:** spegla `ListJobAdsQueryValidator` exakt — per lista cap `SearchCriteria.MaxConceptIds` (400) + concept-id-regex; `Q` via `QMinLength`/`QMaxLength` (Domain-konstanter, DRY).
- **Response-DTO: rå `IReadOnlyDictionary<string,int>`, INGEN `Total`.** En total ur facett-kriteriet vore semantiskt fel (X-exkluderad WHERE → SUM ≠ toolbarens totalCount); korrekt total = extra CountAsync för ett tal FE redan har live. "Visa N annonser"-talet ägs av `PagedResult.TotalCount` — SPOT.
- **Cache-Control: `private, no-store`** (dynamiskt per filter + auth — `/taxonomy/labels`-domen). Ärver `RequireAuthorization()` via gruppen.

## 2. Application-lagret

`GetFacetCountsQuery(Dimension, OccupationGroup?, Municipality?, Region?, Q?) : IQuery<IReadOnlyDictionary<string,int>>` + tunn handler + validator i `Application/JobAds/Queries/GetFacetCounts/`.

- **`ISearchQueryParser` SKA köras på Q — entydigt korrekthetskrav (residual-konsistens):** annars räknar facett-vägen mot annan WHERE än listan för samma användar-input → "Solna (12)" men listan visar 14. Spegelbild av `ListJobAdsQueryHandler`. SPOT (Hunt/Thomas).
- **`ICapturesRecentSearch` ska INTE implementeras** — facett-räkning är ingen sökhändelse; auto-capture skulle skriva recent-rad per popover-toggle. Markeras explicit i query-doc.
- DI via source-gen/scanning — inga registrerings-ändringar. `LoggingBehavior` ger latens-mätning gratis.
- **Tester:** handler-unit (ResidualQ — inte rå Q — når kriteriet; null→[]) + endpoint-integ (401 anonym, 400 ogiltig dimension inkl. numerisk out-of-range, 400 cap-brott, 200 happy med dict-shape).

## 3. VAL 4-implementationen (`ExcludeDimension`)

**[Viktigt]** `JobAdSearchQuery.cs:126-134` tömmer endast egna listan — fel mot CTO VAL 4. Åtgärd:

```csharp
FacetDimension.Municipality or FacetDimension.Region =>
    criteria with { Municipality = [], Region = [] },
```

Uppdatera XML-doc på `FacetCountsAsync` + testfilens KÄRNAN-kommentar. Befintliga 7 tester ändras inte i assertions (ingen kombinerar ort-facett med aktivt ort-filter). **Nya Testcontainers-tester:** (1) Municipality-facett med Region aktivt → region-filtret exkluderat; (2) spegelbild Region-facett; (3) Municipality-facett med båda ort-listorna; (4) OccupationGroup-facett med båda ort-listorna → **E2b-unionen ärvd via SPOT** (regressionsvakt).

## 4. Rate-limit — variantanalys (CTO avgör)

Frekvensprofil: popover-öppning Yrke=1 req, Ort=2; debouncad toggle 1–2 req; aktiv filtrerings-minut ≈ 20–40 facet-req/min PLUS ~20 RSC-list-refetches mot ListReadPolicy (live-commit).

- **Variant A — återanvänd `ListReadPolicy` (60/min/user):** ingen ny policy; MEN delad budget-kollision är konkret: 20 list + 40 facet = 60/min → aktiv filtrerare 429:ar och det som stryps är även LISTAN (svältnings-scenariot som motiverade SuggestPolicy-utbrytningen — least common mechanism).
- **Variant B — egen `FacetCountsPolicy`:** prejudikat ×2 (Suggest 30/10s, TaxonomyRead 20/60s); burst-tolerant tak (t.ex. 30/10s) matchar profilen; IOptions-bundna tal. Kostnad: policy-proliferation.

Neutral notering: filens prejudikat har hittills alltid valt dedikerad policy när frekvensprofilen avviker från list-läsning.

## 5. FE-scope — variantanalys (CTO avgör)

Gemensam mekanik: route-handler `/api/jobb/facet-counts` à la suggest (tyst degradering — counts försvinner, popovern användbar); self-contained debounce-hook ≥300ms + AbortController (INTE TanStack Query, ADR 0042-notat); zod `z.record(z.string(), z.number().int())`; labels ur redan-laddad taxonomi (saknad nyckel = 0). Payload ~400 entries ≈ 10–15 KB.

- **Variant A — per-option-counts i popover-raderna + "Visa N annonser"-stängknapp (toolbarens redan-kända totalCount):** full Platsbanken-affordance-paritet; knappen kostar noll requests. − Funktionellt redundant under live-commit; risk att användaren tror inget händer förrän knapptryck (design-yta).
- **Variant B — enbart per-option-counts, ingen knapp:** ärligast mot live-commit-modellen; minst yta. − Avviker från Platsbankens interaktionsmönster (paritets-dom, ej arkitektur-dom).
- **Variant C — enbart knapp, inga per-option:** uppfyller INTE ADR 0067 Beslut 4 ("full defer av per-option" explicit avvisad — kärn-UX-krav). Falsk-klar mot rad 102/109. Avvisnings-grund redovisad; formell dom CTO:s.

## 6. NBomber-aktivering — bekräftad + kalibrerings-fynd

Footer-stegen bekräftas: Program.cs-gren `"facet-counts" or "all"` + budget-dict; route-konstanten stämmer (endast PLACEHOLDER-region-id byts); `LOADTEST_BEARER_TOKEN` via dev-test-kontot. Observe-only — `::warning::` + exit 0, INGEN flip (Klas-lås). "BLOCKING före live" uppfylls **procedurellt:** backend → lokal NBomber-körning → p95-utfall i PR-body → FÖRST därefter FE-live-wiring; vid p95 > 300ms HALT med fallback-plan.

**[Viktigt] Kalibrerings-fel:** scenario 1 (60/min) + scenario 2 (30/min) parallellt i samma user-bucket = 90/min > 60-taket → 429-förorenad p95. Åtgärd vid aktivering (styrs av CTO:s policy-dom): (a) höj taket i loadtest-miljön via RateLimitingOptions, (b) kör scenarierna sekventiellt, eller (c) dedikerad policy med burst-tak (30/10s = 180/min) löser det. Dokumentera i scenario-kommentaren.

## 7. Perf-bedömning + fallback-ordning (HALT-rapportens innehåll)

**300 ms p95 håller sannolikt med god marginal** — värsta fallet (OccupationGroup utan filter) = Seq Scan (Status-predikatet ej indexerat) + HashAggregate över ~400 grupper på ~43k rader; huvudrisk är heap-bredd (raw_payload jsonb inline) — varm cache tiotals ms. q-filtrerade vägar billigare (GIN-bitmap). Ingen dom föregrips — mätningen finns för detta.

**Fallback-ordning vid brott (eskalerar till Klas per ADR 0045):** (1) covering partial composite-index per dimension (`WHERE col IS NOT NULL AND status='Active'`) → index-only scan; (2) kort-TTL `IMemoryCache` (~30–60s, ej per-user, ingen PII); (3) ADR 0064-analog Worker-precompute. CC förbereder INGEN av dessa i E2c-koden (YAGNI).

## Referenser

CLAUDE.md §2.3/§2.5/§3.6/§5.1/§9.6; ADR 0067 Beslut 4 + rad 102/109 + E2b-notat; CTO VAL 4 (`2026-06-11-sok-paritet-e2b-cto.md`); `IJobAdSearchQuery.cs`/`JobAdSearchQuery.cs`; `JobAdsEndpoints.cs`/`RateLimitingExtensions.cs`/`ListJobAdsQueryValidator.cs`/`app/api/jobb/suggest/route.ts`; `FacetCountsScenarios.cs`; `JobAdFacetCountsTests.cs`; Saltzer/Schroeder 1975; Evans 2003 kap. 2; Hunt/Thomas 1999; Fowler 2018; Nygard 2018.

**Till senior-cto-advisor:** (4) ListReadPolicy vs egen FacetCountsPolicy, (5) FE-scope A/B (C redovisad med avvisnings-grund). Övriga domar entydiga mot skrivna principer.
