# Current work — JobbPilot

**Status:** **PLATSBANKEN SÖK-PARITET — FAS E2a (YRKE-FILTER NIVÅ-SKIFTE → YRKESGRUPP) BYGGD + alla reviews APPROVED 2026-06-10 (branch `feat/sok-paritet-fe-yrkesgrupp-e2a`, PR mot main, bas-HEAD `f860ddf`). KLAS-GO PÅ RENDERAD UI KVARSTÅR.** E1 KLAR: E1b suggest-kontrakt MERGAD `86b61ae` (#39); E1a hero "Papperskontoret" MERGAD `f860ddf` (#40, Klas rendered-GO). Klas-GO E2 (Approach A). **E2a = atomisk korrekthets-batch (EN commit, 20 filer):** Yrke-pickern skiftar nivå occupation-name → yrkesgrupp (ssyk-level-4, ~400) för 100% Platsbanken-paritet (TD-100-kärna). FE-taxonomy-DTO `occupations`→`occupationGroups` (occupation-name droppad, ACL); `?ssyk=`→`?occupationGroup=` atomisk rename över alla call-sites; recent-shim `ssykList`→`occupationGroupList`; cap `MAX_CONCEPT_IDS` 10→400 (backend-paritet). Rubrik "Yrkesgrupper", pill-label "Yrke" behållen. Backend-data verifierad live (400 yrkesgrupper populerade). code-reviewer 0 Block/0 Major/1 Minor (in-block), security-auditor APPROVED, design-reviewer APPROVED. tsc rent, 93 vitest gröna, build grön. **KVARSTÅR: Klas-GO på renderad UI (Vercel-preview, Beslut 7 rad 104).** Nästa split: E2b (Län→Kommun-kaskad + municipality-DTO), E2c (live facet-count + NBomber), E2d (chip-komponist — kräver chip/residual-bekräftelse), E2e (Rensa-länkar/sortering). **docs-drift (#0B5CAD→navy-canon)** frikopplad från E1a-PR (Klas mergade #40 före approve) → folds in i E2-split efter Klas `approve-spec-edit.sh`.

**Levererat denna session (Fas E2a-PR — pending Klas rendered-GO):**

- **Yrke-nivå-skifte (TD-100-kärna):** FE-taxonomy-DTO `occupationFields[].occupations` → `occupationGroups` (ssyk-level-4); occupation-name droppad ur FE (ACL — recall-substrat backend-side). Pickern matar yrkesgrupp-ids. Empiriskt verifierat: 400 yrkesgrupper populerade (t.ex. "Advokater", "Arbetsförmedlare").
- **Atomisk `?ssyk=`→`?occupationGroup=`** över buildJobbHref/buildQuery/page/picker/results+toolbar/recent (Fowler Rename Field, TS-säkrad). recent-shim `ssykList`→`occupationGroupList`. Cap 10→400.
- **Agent-domar** (`docs/reviews/2026-06-10-sok-paritet-e2a-reviews.md`): architect (E2a-spec), code-reviewer/security-auditor/design-reviewer APPROVED. ADR 0067 impl-notat (Fas E2a) skrivet.

**Levererat denna session (Fas E1a-PR — pending Klas-GO):**

- **/jobb-hero "Papperskontoret" (riktning A):** navy-banner → varm papperston-canvas. Ny `--jp-hero-canvas` (#FAF9F6 light, ärver `--jp-canvas` #0B1525 dark, /jobb-scoped — rör ej app-wide `--jp-canvas`; architect-dom). `.jp-pagehero` (inre sidor) orörd. Alla hero-barn flippade vit-på-navy → ink-på-papper via tokens. Sök-knapp navy-800 primary (ADR 0052). Dark: ljust sökfält + mörk text.
- **Regel-1-fixar:** drop-shadow → border (papper); 40px-titel → 28px H1-token; 12px verifierat redan compliant (6px, oförändrat — ärligt rapporterat). Microcopy: H1 "Lediga jobb", label "Sök efter yrke, arbetsgivare eller ort", placeholder "t.ex. systemutvecklare Göteborg".
- **Ny `--jp-placeholder`-token (#626B78):** WCAG AA ≥4.5:1 (#FFFFFF 5.39:1, #F0F4FB 4.89:1) → löste design-reviewer-VETO (2 Blockers placeholder-kontrast light+dark).
- **Agent-domar:** nextjs-ui-engineer (bygge), dotnet-architect (token-arkitektur), design-reviewer VETO→APPROVED (`docs/reviews/2026-06-10-sok-paritet-e1a-design-review.md`). ADR 0067 impl-notat (Fas E1a) skrivet.

**Levererat denna session (Fas E1b-PR — MERGAD #39):**

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
| `86b61ae` | #39 | Fas E1b — typeahead-suggest FE-kontrakt SuggestionDto[] |
| `f860ddf` | #40 | Fas E1a — /jobb-hero varm papperston-canvas (Papperskontoret) |
| (denna) | — | feat/sok-paritet-fe-yrkesgrupp-e2a — yrke-nivå-skifte → yrkesgrupp (pending Klas-GO) |

---

## Pending operativt för Klas

1. **KLAS-GO PÅ RENDERAD UI — E2a yrke-picker (Beslut 7 rad 104):** design-reviewer APPROVED (0 fynd), men Fas E kräver design-reviewer VETO **+ Klas-GO**. Granska Vercel-preview (PR #41): Yrke-popoverns högerkolumn ska visa ~400 yrkesgrupper (Yrkesområde→Yrkesgrupper). Ge GO → CC sätter automerge-label.
2. **DOCS-DRIFT SPEC-EDIT (Klas-gated, frikopplad från E1a-PR):** stale `#0B5CAD` → navy-canon i `jobbpilot-design-tokens`-skill (genomgående v2-slate: brand-ramp + focus + dark-värden, 5 filer). Klassificeraren blockerar CC-self-approve → **Klas kör `approve-spec-edit.sh`** + scope-val (bara brand-600 vs full ramp); CC synkar (docs-keeper), folds in i nästa E2-split-PR.
3. **KLAS-STOPP — chip/residual-kombinationssemantik (ADR 0067 Beslut 5):** krävs INNAN E2d wirar chip+residual. Bekräfta `(dim-predikat) AND (FTS ∨ title-LIKE ∨ synonym)`.
4. **E2b–E2e (nästa splits, kräver Klas-GO per split):** E2b Län→Kommun-kaskad + municipality-DTO; E2c live facet-count "Visa N annonser" (`FacetCountsAsync`-endpoint + NBomber-gate ADR 0045 300ms p95 BLOCKING); E2d chip-komponist (efter chip/residual-bekräftelse); E2e Rensa-textlänkar + sortering.
5. **Re-ingest Klass 2** (`POST /api/v1/admin/job-ads/backfill-klass2`, ~2,5h) — blockerar Anställningsform/Omfattning-filter (gated tills körd). Kör EJ utan Klas-GO.
6. **CLAUDE.md §11.3-drift** (`make dev`/`pnpm dev:up` finns ej) — skapa-vs-stryk-beslut vid nästa spec-touch (kvarstår).

---

## Historik

All tidigare session-historik (Fas D1, editor-baseline, Fas C2 och bakåt): **`docs/current-work-archive.md`** (omvänd kronologi) + per-session-loggar i **`docs/sessions/`**.
