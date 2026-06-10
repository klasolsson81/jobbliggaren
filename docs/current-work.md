# Current work — JobbPilot

**Status:** **PLATSBANKEN SÖK-PARITET — FAS D2 (`ISearchQueryParser` RESIDUAL-FRITEXT) LEVERERAD 2026-06-10 (branch `feat/sok-paritet-query-parser-d2`, PR mot main, bas-HEAD `ed959c0`).** `ISearchQueryParser`-port + `ParsedSearchQuery(string? ResidualQ)` (Variant A+A — ren ResidualQ-normalisering, INGA dimensions-fält; CTO VAL 1). `SearchQueryParser` (`internal sealed`, Application/JobAds/Internal — ren CPU, bor HELT i Application, ej Infra; CTO VAL 2). Parsern wirad i `ListJobAdsQueryHandler`: live-`query.Q` → `ResidualQ` → `JobAdFilterCriteria.Q` → FTS-hybridens OR-additiva gren (kraschsäker — residual blir aldrig hårt AND, kompilator-garanterat eftersom kontraktet saknar dimensions-fält). 32 parser-unit-fall + 5 handler-fall + 4 Testcontainers-integ + sök-regression (35) gröna. **ADR 0067-kontraktet (5c, pre-C2) reconcilat → implementerings-notat (CTO VAL 3, ingen amendment). Kombinationssemantik = Klas-STOPP (GO först vid Fas E-wiring). Nästa: Fas E (FE-picker + chip-komposition + live-count + ny färg-identitet) — Klas-GO.**

**Levererat denna session (Fas D2-PR):**

- **`ISearchQueryParser`-port + `ParsedSearchQuery`-DTO (ADR 0067 Beslut 5c):** Application/JobAds/Abstractions. Kontrakt = `Parse(string? raw) → ParsedSearchQuery(string? ResidualQ)`. Variant A+A (CTO VAL 1): parsern extraherar INGA dimensioner — dimension-disambiguering är FE-chip-ansvar (Beslut 5b/Fas E), inte "gissande backend". Vestigiala dimensions-fält avvisade (Fowler Speculative Generality/YAGNI).
- **`SearchQueryParser` impl (CTO VAL 2):** `internal sealed`, Application/JobAds/Internal. Ren CPU (ingen IOptions/taxonomi/Npgsql) → bor HELT i Application (Martin kap. 22). `IOccupationSynonymExpander`-Infra-precedensen gäller ej (den splitten = IOptions-binding som saknas här). Normalisering: whitespace-kollaps (inkl. tab/newline, IsWhiteSpace FÖRE Cc/Cf-strip), strip Unicode Control/Format (null-byte/C0/zero-width/RTL-override), sub-`QMinLength`(2)→null (1-tecken-`%a%`-near-full-scan-skydd), >`QMaxLength`(100)→**rune-säker trunkering** (backar aldrig mitt i surrogatpar). Kastar ALDRIG.
- **DRY-konsolidering:** `SearchCriteria.QMinLength`/`QMaxLength` private→`public const` (parallellt med `MaxConceptIds`); `ListJobAdsQueryValidator` + parsern refererar EN sanningskälla i stället för literalerna 2/100 (Hunt/Thomas DRY/SPOT).
- **Residual-Q-inkoppling:** `ListJobAdsQueryHandler`-ctor +`ISearchQueryParser`; `parser.Parse(query.Q).ResidualQ` → `JobAdFilterCriteria.Q` → q-FTS-hybrid (ADR 0062). `RunSavedSearch` parsar EJ om sitt Q (persisterat, redan validerat vid spar-tid) — scope-korrekt, ej SPOT-brott. DI singleton i Application Common/DependencyInjection (samma commit).
- **InternalsVisibleTo:** Application.csproj → Application.UnitTests + Api.IntegrationTests (för parser-instansiering i test; speglar Infrastructure.csproj).
- **Tester:** ny `SearchQueryParserTests` (32 fall, ren CPU + kraschsäkerhet + surrogat-gräns) + `ListJobAdsQueryHandlerTests` (uppdaterad 2-arg ctor + 5 nya parser-inkopplings-fall) + ny `ListJobAdsResidualQueryTests` (4 Testcontainers — recall-bevarande, ingen-träff utan krasch, residual AND region, kontrolltecken→strip). 3 befintliga integ-filer uppdaterade för 2-arg ctorn. Application 728 / Domain 440 / Architecture 78 / integ residual 4 + sök-regression 35 gröna. Bygg 0 warn/0 err, format-verify exit 0.
- **Agent-domar (`docs/reviews/2026-06-10-sok-paritet-d2-*.md`):** dotnet-architect (kontrakts-spänning + lager + reconciliation-natur), senior-cto-advisor (VAL 1–6: Variant A+A, Application-only, notat-ej-amendment, kombinationssemantik, in-block, scope-vakt), code-reviewer (0 Block / **1 Major surrogat-split — åtgärdad in-block** + rune-säker trunkering + 2 gränstester / 2 Minor acceptabla), security-auditor (**APPROVED**, 0 Crit/High/Major — parsern minskar netto-attack-ytan). ADR 0067 implementerings-notat 2026-06-10 (Fas D2) skrivet.

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
| `ed959c0` | #37 | Fas D1 — facet-counts + utökad typeahead-suggest |
| (denna) | — | feat/sok-paritet-query-parser-d2 — ISearchQueryParser residual-fritext |

---

## Pending operativt för Klas

1. **Granska Fas D2-PR post-merge** (automerge-label sätts av CC; `ci`-aggregatet bär kvaliteten + agent-reports inline). code-reviewer Major (surrogat-split) åtgärdad in-block med rune-säker trunkering + gränstester; security-auditor APPROVED.
2. **KLAS-STOPP — chip/residual-kombinationssemantik (ADR 0067 Beslut 5 mildrad Klas-STOPP):** D2 byggde backend-parsern men wirar INTE FE-chip-state. Innan Fas E wirar chip+residual ihop, bekräfta semantiken: dimensioner AND-mellan / OR-inom (ADR 0042 B); residual-Q AND-block bredvid dimensionerna men OR-bevarande inom q-grenen (FTS ∨ title-LIKE ∨ synonym, ADR 0062). Dvs `(dim-predikat) AND (FTS ∨ title-LIKE ∨ synonym)` — Q smalnar additivt mot dimensionerna men breddar inom sig själv, aldrig eget AND-fält. **Se STOPP-rapporten för full presentation.**
3. **FE-kontraktsbrott från D1 (kvarstår):** `/suggest` retur `SuggestionDto[]`; `web/.../job-ad-typeahead.tsx` migreras i Fas E. Säg till om mellanliggande FE-deploy planeras.
4. **NBomber facet-counts-gate (D1) körs i Fas E** (när endpoint finns). Default = parkerat (Väg B).
5. **Re-ingest Klass 2** (`POST /api/v1/admin/job-ads/backfill-klass2`, ~2,5h) — blockerar B2-dims (employment_type/worktime_extent) i FacetDimension + suggest + ev. framtida parser-kontrakt-tillägg. Kör EJ utan Klas-GO.
6. **CLAUDE.md §11.3-drift** (`make dev`/`pnpm dev:up` finns ej) — skapa-vs-stryk-beslut vid nästa spec-touch (kvarstår).

---

## Historik

All tidigare session-historik (Fas D1, editor-baseline, Fas C2 och bakåt): **`docs/current-work-archive.md`** (omvänd kronologi) + per-session-loggar i **`docs/sessions/`**.
