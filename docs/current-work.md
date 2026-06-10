# Current work — JobbPilot

**Status:** **PLATSBANKEN SÖK-PARITET — FAS E1b (TYPEAHEAD-SUGGEST FE-KONTRAKT) LEVERERAD 2026-06-10 (branch `feat/sok-paritet-fe-suggest-e1b`, PR mot main, bas-HEAD `13eb0af`).** Fas E LÅST design-riktning A "Papperskontoret" + varm papperston `#FAF9F6` (Klas-GO 2026-06-10). CTO splittade E1→E1a (design-grind, design-reviewer VETO + Klas-GO) + E1b (kontrakts-plumbing, code-reviewer-gated), E1b först. E1b migrerar `/suggest`-FE-konsumtionen `string[]` → `SuggestionDto[]` (`{kind, conceptId, label}`, Beslut 5a); `suggestionKindFromWire` mappar wire-heltal → namn (defensivt int|string-union, speglar `sortByFromWire`); `JobAdTypeahead` renderar `label` (chip-komposition = E2). Komponenten ej wirad live → noll UI-regression. 45 vitest gröna, pnpm build grön; code-reviewer 0 Block/0 Major/1 Minor (in-block), security-auditor APPROVED. **KLAS-STOPP — scope-omförhandling (CTO-flaggat): `?ssyk=`→`?occupationGroup=`-param-rename + recent-shim + picker-nivå-skifte är verifierat entanglade med E2:s yrkesgrupp-skifte (stale FE-taxonomy-DTO, delad `buildJobbHref`, live-picker matar occupation-name-ids) → CTO flyttar dem till E2; Klas-promptens E1-lista omförhandlas. Se STOPP-rapport.** Nästa (efter Klas-GO på scope): E1a design-grind (hero varm canvas + regel-1-fixar + microcopy + docs-drift).

**Levererat denna session (Fas E1b-PR):**

- **Suggest-kontrakt migrerat (ADR 0067 Beslut 5a):** `lib/dto/job-ads.ts` — nytt `suggestionDtoSchema` (`kind`/`conceptId`/`label`) + `suggestionKindFromWire` (wire-heltal Title=0..OccupationGroup=4 → namn via `SUGGESTION_KIND_ORDER`; defensivt int|string-union). Verifierat on-disk: `SuggestionKind` är native C#-enum utan `JsonStringEnumConverter` → serialiseras som HELTAL (samma int-konvention som `JobAdSortBy` i recent-searches `sortByFromWire`).
- **`JobAdTypeahead` konsumerar `SuggestionDto[]`:** renderar `item.label` (React-escapad text), `key=${kind}:${conceptId??label}`, `choose(item.label)` (behåller `onSelect(string)`-kontrakt). `kind`/`conceptId` parsas som kontraktsfält men chip-komposition är E2 (CTO-dom). Komponenten ej wirad live → noll UI-regression, visual-verify ej triggad.
- **Tester:** `job-ads.test.ts` +7 schema-fall (int→namn 0–4, sträng-namn defensivt, out-of-range/okänd → fail-stängt, array-mix); `job-ad-typeahead.test.tsx` fixtures `string[]`→SuggestionDto-objekt (int kind). 45 vitest gröna, tsc/eslint rena, pnpm build grön.
- **Agent-domar (`docs/reviews/2026-06-10-sok-paritet-e1*-*.md`):** senior-cto-advisor (E1-split + radius-cleanup + E1b-entanglement-återtriage Approach A — KRÄVER Klas-GO), dotnet-architect (varm-canvas-token /jobb-scoped `--jp-hero-canvas`), code-reviewer (0 Block/0 Major/1 Minor in-block), security-auditor (APPROVED). ADR 0067 implementerings-notat 2026-06-10 (Fas E-uppdelning + E1b) skrivet.

**Föregående leverans (Fas D2-PR):**

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
| `13eb0af` | #38 | Fas D2 — ISearchQueryParser residual-fritext |
| (denna) | — | feat/sok-paritet-fe-suggest-e1b — typeahead-suggest FE-kontrakt SuggestionDto[] |

---

## Pending operativt för Klas

1. **Granska Fas E1b-PR post-merge** (automerge-label sätts av CC; `ci`-aggregatet bär kvaliteten + agent-reports inline). code-reviewer 0 Block/0 Major/1 Minor (in-block); security-auditor APPROVED. `JobAdTypeahead` ej wirad live → noll UI-regression.
2. **KLAS-GO BEHÖVS — scope-omförhandling Fas E (CTO-flaggat, CLAUDE.md §9.6 punkt 5):** `?ssyk=`→`?occupationGroup=`-param-rename, `RecentJobSearchDto`-shim-borttagning och live-picker-nivå-skifte är verifierat entanglade med E2:s yrkesgrupp-skifte (stale FE-taxonomy-DTO modellerar occupation-name; delad `buildJobbHref`; live-picker matar occupation-name-ids → backend ignorerar `ssyk` = tyst no-op idag; renamet utan picker-skifte → Yrke-filter regresserar till noll träffar). CTO-dom Approach A: flytta alla tre till E2 som atomiskt block. Klas-promptens E1-lista (alla tre under E1) omförhandlas → **bekräfta E1b=suggest-only + flytt till E2, ELLER avvik (t.ex. tidigarelägg E2).** Se STOPP-rapport.
3. **KLAS-STOPP — chip/residual-kombinationssemantik (ADR 0067 Beslut 5 mildrad Klas-STOPP):** kvarstår från D2. Innan E2 wirar chip+residual: bekräfta `(dim-predikat) AND (FTS ∨ title-LIKE ∨ synonym)` — Q smalnar additivt mot dimensionerna men breddar inom sig själv.
4. **E1a design-grind (näst på tur, oberoende av scope-frågan):** hero navy→varm canvas `#FAF9F6` (ny `--jp-hero-canvas`-token, /jobb-scoped) + regel-1-fixar (drop-shadow/40px-titel/12px-radie) + egen microcopy + docs-drift `#0B5CAD`→navy-800 i design-tokens-skill (spec-edit → Klas kör `approve-spec-edit.sh`). design-reviewer VETO + Klas-GO på renderad UI.
5. **NBomber facet-counts-gate (D1) körs i E2** (när endpoint finns). Default = parkerat (Väg B).
6. **Re-ingest Klass 2** (`POST /api/v1/admin/job-ads/backfill-klass2`, ~2,5h) — blockerar B2-dims + Anställningsform/Omfattning-filter. Kör EJ utan Klas-GO.
7. **CLAUDE.md §11.3-drift** (`make dev`/`pnpm dev:up` finns ej) — skapa-vs-stryk-beslut vid nästa spec-touch (kvarstår).

---

## Historik

All tidigare session-historik (Fas D1, editor-baseline, Fas C2 och bakåt): **`docs/current-work-archive.md`** (omvänd kronologi) + per-session-loggar i **`docs/sessions/`**.
