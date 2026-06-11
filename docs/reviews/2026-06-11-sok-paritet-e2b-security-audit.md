# Security-audit: Fas E2b — Platsbanken sök-paritet (feat/sok-paritet-fe-kommun-kaskad-e2b)

**Status:** ✓ APPROVED
**Granskat:** 2026-06-11
**Auktoritet:** CLAUDE.md §5.4, GDPR Art. 5/32, ADR 0042 Beslut B, ADR 0067
**Scope:** 3 commits (637f170, 597fd84, d931b2f) — `git diff main...HEAD`, 26 filer

---

### Granskningspunkt 1: Municipality-input-vägen (SQL-injektion + DoS)

**SQL-injektion: Ingen yta.** Union-grenen i `src/JobbPilot.Infrastructure/JobAds/JobAdSearchQuery.cs:185-192` är ren EF-LINQ:

```csharp
source = source.Where(j =>
    municipalityValues.Contains(EF.Property<string?>(j, "MunicipalityConceptId"))
    || regionValues.Contains(EF.Property<string?>(j, "RegionConceptId")));
```

`Contains` över en parameterlista översätts av EF Core/Npgsql till parametriserat `= ANY(@p)` (array-parameter) — ingen strängkonkatenering, ingen rå SQL. `EF.Property<string?>`-referenserna är hårdkodade shadow-property-namn, inte user-input.

**Defense-in-depth-kedjan är intakt i tre lager** (Saltzer/Schroeder default-deny):

1. `ListJobAdsQueryValidator.cs:40-48` — `Municipality` har både cap (`Count <= SearchCriteria.MaxConceptIds` = 400) och per-element-regex `^[A-Za-z0-9_-]{1,32}\z`. Symmetriskt med OccupationGroup/Region.
2. `SearchCriteria.Create` (Domain) — samma cap + regex via `ValidateConceptList` (`SearchCriteria.TooManyMunicipality`/`InvalidMunicipality`).
3. Endpoint (`JobAdsEndpoints.cs`) kör `RequireRateLimiting(ListReadPolicy)`.

Charset-regexen (alfanumeriskt + `_-`, max 32) eliminerar dessutom injection-stuffing redan före handlern — även om EF inte hade parametriserat (vilket den gör).

**DoS-bedömning av unionen: Godkänd.** Worst case `IN(400) OR IN(400)`:

- Båda kolumnerna är STORED generated columns med partiella B-tree-index (`ix_job_ads_municipality_concept_id` / region-motsvarigheten, `WHERE ... IS NOT NULL`, migration `20260608155047_F6P6JobAdKlass1SearchColumns.cs:56-59`). Query-predikatet `IN (...)` implicerar `IS NOT NULL` → de partiella indexen är användbara; Postgres hanterar OR-mellan-indexerade-predikat med BitmapOr av två index-scans.
- Värdedomänen är liten (21 regioner, 290 kommuner) — selektiviteten är god och 400-cappen är generös men ändlig (den verkliga DoS-vektorn vore obegränsad lista, vilket cappen utesluter, per ADR 0042-amendmentens egen analys).
- Unionen ersätter två sekventiella AND-`Where` med EN `Where` — query-komplexiteten ökar inte; planen blir snarare billigare än två separata filtersteg med noll-resultat.
- Param-antalet är oförändrat mot vad C1 redan tillät (400+400 var möjligt förut också, bara med AND-semantik).

ADR 0045-budgeten bevakas av observe-only loadtest + `LoggingBehavior`-latensen — ingen ny mätlucka introduceras.

**Reverse-lookup-cappen håller för tre dimensioner:** `ResolveTaxonomyLabelsQueryValidator.MaxConceptIdsPerCall` = 400×4 = 1600; FE:s `selectedConceptIds = [...occupationGroup, ...region, ...municipality]` (jobb-results.tsx) maxar på 1200. Ingen ValidationException-yta vid legitim maxlast.

### Granskningspunkt 2: FE-rendering (XSS)

**Ingen rå HTML-yta.** Alla nya/ändrade renderingspunkter går genom React JSX-text (automatisk escaping):

- Chips i `jobb-results-toolbar.tsx` renderar `{chip.label}` som text-child — inklusive fallbacken `"Okänd kod (<id>)"` (test verifierar plain-text-rendering, `jobb-results-toolbar.test.tsx`).
- Popover-rader (`jobb-filter-popover.tsx`) renderar `label` via `CheckRow` som JSX-text.
- Hidden inputs i `jobb/page.tsx` (`value={v}` för municipality) — React escapar attributvärden.
- Repo-bred grep: enda `dangerouslySetInnerHTML` är theme-providerns statiska script (pre-existerande, utanför diffen); inga `eval`/`new Function`/`innerHTML`.

Den medvetna designen från security-auditor-flaggan 2026-05-17 består: `taxonomyLabelSchema` validerar INTE conceptId-format (stale ids kan ha annat format) eftersom rendering sker som ren text — och backend-validatorns charset-cap (`ResolveTaxonomyLabelsQueryValidator`) begränsar ändå den reflekterade id-strängen som defense-in-depth. Konsistent.

`ort-selection.ts` är ren listmanipulation av conceptId-strängar — ingen rendering, ingen storage, ingen yta.

### Granskningspunkt 3: Ssyk-shim-borttagningen

**Ingen informationsläcka, ingen kontraktsrisk.** `SsykList`/`SsykLabels` var deprecated alltid-tomma sedan C2 (`[]`) — borttagningen i `RecentJobSearchDto.cs` minskar wire-ytan, exponerar inget nytt. FE-zod (`recent-searches.ts`) refererar inte längre `ssykList` (verifierat: noll träffar) och de fält FE nu kräver (`occupationGroupList`, `municipalityList`) levereras av handlern. Wire-kontraktet är namnbaserat (camelCase JSON + zod) så DTO:ns positionsomflyttning är intern. Informationsflödet är netto-minskande.

### Granskningspunkt 4: PII/logging

**Oförändrad yta.** Diffen innehåller noll logg-satser (grep mot hela diffen: inga `logger`/`console.*`/`Log*`-rader). Geografi-conceptIds (taxonomikoder för kommun/län) är inte PII i sig; recent-search-lagringen av `Municipality` fanns redan sedan C2 och ändras inte av E2b (endast DTO-exponeringen, som redan var avsedd). Inga nya kolumner, ingen retention-förändring, ingen ny extern integration, inga secrets, ingen AI-yta.

---

### Findings

**Critical:** Inga. **High:** Inga. **Medium:** Inga. **Low:** Inga.

### Praise

- Tre-lagers-validering (validator → Domain-VO → rate-limit) bevarad symmetriskt för municipality — ingen dimension är svagare än de andra.
- "Hela länet = ETT region-id, aldrig materialiserade kommun-ids" (ort-selection.ts) är samtidigt 414-skydd och param-minimering — bra säkerhets-bieffekt av UX-beslutet.
- Plain-text-rendering-disciplinen för `"Okänd kod (<id>)"` är test-låst i både taxonomy.test.ts och jobb-results-toolbar.test.tsx.
- Denormaliserat URL-state (handredigerat/stale) förblir korrekt backend-side under union — ingen klient-tillit för korrekthet.

### Sammanfattning

E2b ändrar kombinationslogik (AND→union) inom ett redan härdat param-kontrakt; ingen ny input-yta, ingen ny rendering-yta, ingen PII/logging-förändring. CTO:s bedömning att kontraktet var orört bekräftas — auditen hittade inget som motsäger den.

**Verdict: APPROVED — säkerhetsmässigt mergeklar.**
