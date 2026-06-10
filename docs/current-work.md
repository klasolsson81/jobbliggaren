# Current work — JobbPilot

**Status:** **PLATSBANKEN SÖK-PARITET — FAS D1 (FACET-COUNTS + UTÖKAD TYPEAHEAD-SUGGEST) LEVERERAD 2026-06-10 (branch `feat/sok-paritet-facets-d1`, PR mot main, bas-HEAD `06b7840`).** `FacetCountsAsync` (ny metod på `IJobAdSearchQuery`, EJ ny port — SPOT) med facett-exkluderings-semantik (count för dimension X reflekterar alla andra filter men inte X) via GROUP BY shadow-column; `FacetDimension = {OccupationGroup, Municipality, Region}` (B2-dims uteslutna tills re-ingest — falsk-klar). Utökad suggest: `SuggestByPrefixAsync` på `ITaxonomyReadModel` (in-memory ACL-snapshot) + ny `SuggestionKind`-enum; `SuggestJobAdTermsQuery` retur `string[]`→`SuggestionDto[]` (FE-brott medvetet, ingen shim). NBomber-instrument författat men **parkerat** (gate-exekvering bunden till Fas E per ADR 0067 "före live"). 23 nya Testcontainers-tester gröna. **Nästa: Fas D2 (`ISearchQueryParser` residual-fritext — Klas-STOPP) ELLER Fas E (FE-picker) — Klas-GO.**

**Levererat denna session (Fas D1-PR):**

- **`FacetCountsAsync` (ADR 0067 Beslut 4):** ny port-metod (`IJobAdSearchQuery`); Infrastructure-impl i `JobAdSearchQuery` — `ExcludeDimension` klonar filter-SPOT:en med X-listan tömd (`criteria with { X = [] }`, exkluderings-mekaniken; SPOT bevarad, ingen `ApplyCriteriaExcept`-duplikat), `ShadowColumn`-switch → GROUP BY på STORED shadow-column, NULL-shadow exkluderas (`EF.Property<string?> != null`). Rå concept-id→count (namn-omedveten, ADR 0043 Beslut E). Status=Active ärvs via ApplyCriteria-SPOT.
- **`FacetDimension`-enum:** `{OccupationGroup, Municipality, Region}`. EmploymentType/WorktimeExtent UTESLUTNA (NULL-data för ~44k rader tills re-ingest — CTO VAL 1 = Variant A, falsk-klar-disciplin). Tillkommer additivt vid B2-data.
- **Utökad typeahead-suggest (ADR 0067 Beslut 5a):** `SuggestByPrefixAsync` på `ITaxonomyReadModel` (in-memory prefix-scan av cachad snapshot, bryter EJ ADR 0043 extern-hop-förbud); `SuggestionKind`-enum (ny Application-typ — `TaxonomyConceptKind` är `internal`, får ej korsa gränsen; ACL via `MapKind`). occupation-name UTESLUTET (saknar filter-dimension — CTO VAL 4). Union-handler: taxonomi först, sedan titel-prefix (oförändrad LIKE-escape-gren), dedup `(Kind, ConceptId)`/`(Title, Label)`, cap till limit. `SuggestionDto(Kind, ConceptId?, Label)`.
- **Kontraktsbrott (medvetet, CTO VAL 5):** `SuggestJobAdTermsQuery` retur `IReadOnlyList<string>`→`IReadOnlyList<SuggestionDto>`. `/suggest`-endpoint returnerar nu `SuggestionDto[]`. `web/.../job-ad-typeahead.tsx` inkompatibel tills Fas E migrerar FE. Inget bakåtkompat-shim (transient read-API utan persistens ≠ C2:s RecentJobSearchDto-shim).
- **NBomber-instrument (CTO Väg B reconcile):** `FacetCountsScenarios.cs` författat mot planerad Fas E-endpoint men **EJ registrerat i `Program.cs` aktiv körning** (ingen endpoint i D1 = port-only per CTO VAL 2). Gatens exekvering (300 ms p95, ADR 0045 klass a) binds till Fas E ("före per-option går live"). **Ingen p95-dom i D1, ingen live-aktivering i D1** (anti-falsk-klar).
- **Arch-test:** `TaxonomyAclLayerTests`-allowlist utökad med femte legitim `ITaxonomyReadModel`-konsument (`SuggestJobAdTermsQueryHandler`, query-handler).
- **Tester:** 20 nya (7 FacetCounts + 9 SuggestByPrefix + 4 union) + 3 uppdaterade (suggest-kontraktsbrott). Testcontainers (ej InMemory) för GROUP BY-/shadow-prop-vägen. 23 D1-tester verifierat gröna lokalt.
- **Agent-domar (`docs/reviews/2026-06-10-sok-paritet-d1-*.md`):** dotnet-architect (signatur + GROUP BY-placering + suggest-union), senior-cto-advisor (5 multi-approach-val + NBomber-reconcile Väg B), code-reviewer (0 Block / 0 Major / 2 Minor FYI), security-auditor (0 Crit / 0 High / 1 Minor). Minor fixade in-block.

**Commit-kedja (squash-merge-SHA på main, sök-paritet-bågen):**

| SHA | PR | Beskrivning |
|---|---|---|
| `01a6039` | #29 | Fas A — ADR 0067 + ADR 0043-amendment (design) |
| `154fb07` | #30 | Fas B1 — Klass 1 data-layer (kommun/yrkesgrupp STORED) |
| `1fd9600` | #31 | Fas B2 — Klass 2 data-layer (anställningsform/omfattning + re-ingest-trigger) |
| `bc54a84` | #32 | Fas C1 — query/filter-layer + yrke-nivåbyte |
| `481e1f0` | #33 | Fas C2 — VO-expansion + reverse-lookup-migration + jsonb-bakåtkompat |
| `cefa60f` | #34 | chore/editor-baseline — .editorconfig + .vscode + docs-drift-fix |
| `e06c678` | #35 | docs(spec) — CLAUDE.md §1.6-rad current-work-archive |
| `06b7840` | #36 | docs(design) — handoff-bundles + agent-roster-CTO-rapport |
| (denna) | — | feat/sok-paritet-facets-d1 — facet-counts + utökad suggest-union |

---

## Pending operativt för Klas

1. **Granska Fas D1-PR post-merge** (automerge-label sätts av CC; `ci`-aggregatet bär kvaliteten + agent-reports inline).
2. **FE-kontraktsbrott medveten-flagga (CTO VAL 5):** suggest retur `string[]`→`SuggestionDto[]`. Ingen mellanliggande FE-deploy mot D1-backend förväntas före Fas E. **Säg till om en sådan deploy planeras** → då omprövas shim (Väg A). Annars kör vi vidare.
3. **NBomber facet-counts-gate körs i Fas E** (när endpoint finns). Vill du ha en tunn mät-endpoint redan nu (CTO Väg A) istället för parkerat instrument? Default = parkerat (Väg B).
4. **GO för nästa fas:** D2 (`ISearchQueryParser` residual-fritext — chip-AND/residual-FTS-semantik = Klas-STOPP per ADR 0067) eller E (FE-picker + live-count + ny färg-identitet, design-reviewer VETO).
5. **Re-ingest Klass 2** (`POST /api/v1/admin/job-ads/backfill-klass2`, ~2,5h) — opåverkad, blockerar B2-dims-wiring (employment_type/worktime_extent) i FacetDimension + suggest. Kör EJ utan Klas-GO.
6. **CLAUDE.md §11.3-drift** (`make dev`/`pnpm dev:up` finns ej) — skapa-vs-stryk-beslut vid nästa spec-touch (kvarstår).

---

## Historik

All tidigare session-historik (editor-baseline, Fas C2 och bakåt): **`docs/current-work-archive.md`** (omvänd kronologi) + per-session-loggar i **`docs/sessions/`**.
