# Session 2026-06-13 — Fas E Filter-panel, Klass 2 PR-1 (options-källa)

**HEAD vid start:** `a377f1d` (B2 query-wiring #60). Branch: `feat/klass2-taxonomy-options`.

## Scope-avstämning (chatt först)
Promptens kandidat-split (1 picker-träd, 2 Klass 2-filter, 3 facet-counts) var
delvis inaktuell: **#1 + #3 redan levererade** (E2a/E2b/E2c). Genuin återstod =
**#2 Klass 2-filterpanel**. Explore-kartläggning bekräftade att employmentType/
worktimeExtent saknar ALL FE-filter-UI. Backend-Explore avtäckte blockeraren:
**options-discovery + label-resolution saknas helt** i taxonomi-ACL:n (TreeDto,
ConceptKind, snapshot, seeder, generator exponerar/laddar inte Klass 2;
`ResolveTaxonomyLabels` ger "Okänd kod").

**Klas-GO (AskUserQuestion):** hela Klass 2 (backend + FE), split till CTO →
backend-scope inom Fas E auktoriserat.

## Agent-domar
- **dotnet-architect** (advisor): variant-analys A/B/C/D + flat-list-DTO +
  generator-vs-fryst + ADR-status. `docs/reviews/2026-06-13-sok-paritet-e-klass2-architect.md`.
- **senior-cto-advisor** (decision-maker): BESLUT 1–5.
  `docs/reviews/2026-06-13-sok-paritet-e-klass2-cto.md`.
  - B1: **Variant B** (fryst embedded seed, ej generate.mjs).
  - B2: två platta topp-collections + egen `TaxonomyOptionDto`.
  - B3: **ADR 0043-amendment krävs** (motiverar generator-avvikelsen).
  - B4: PR-split PR-1→PR-2→PR-3→PR-4 (PR-3 separat; saved-search-labels = PR-4).
  - B5: **STOPP** — UI-form Klas-produktbeslut.
- **BESLUT 5 löst via Klas Platsbanken-bild:** Omfattning = radio (Alla/Heltid/
  Deltid), Anställningsform = checkbox-multi, Publicerad = radio (finns via since).
  Nyans: Platsbanken visar 5 kurerade anställningsform-etiketter (utelämnar
  "Vanlig anställning" 24k); vår korpus har 8 råa → kurering = PR-2-presentation.

## PR-1 leverans (denna session)
Options-källan i taxonomi-ACL:n:
- `TaxonomyConceptKind` += `EmploymentType`, `WorktimeExtent` (platta, parentless).
- `klass2-taxonomy.json` (ny frusen embedded resource) — 8 employment + 2 worktime,
  concept-id + svensk label härledda ur **dev-korpus** `raw_payload` 2026-06-13
  (web-search-fritt, auktoritativt: exakt värdena som kan stå i STORED-kolumnerna).
- `Klass2TaxonomyFile` deserialiserings-record; csproj `<EmbeddedResource>`.
- Seeder: `LoadKlass2`, `CompositeVersion` (`{taxonomyVersion}+klass2-{v}` →
  tvingar re-seed), `MapRows`-grenar (platta rader, ingen parent).
- `TaxonomyTreeDto` += `EmploymentTypes`/`WorktimeExtents` (`TaxonomyOptionDto`).
- `TaxonomyReadModel.LoadAsync` bygger platta listor; `labelByConceptId`
  kind-agnostisk → Klass 2-reverse-lookup gratis; suggestable orörd.
- FE `taxonomy.ts`: `taxonomyOptionSchema` + REQUIRED `employmentTypes`/
  `worktimeExtents` (drift fails loud).
- ADR 0043-amendment 2026-06-13.

**Ingen migration** (Kind=string max 20, ingen CHECK; bara nya rader) — CTO B3
verifierad on-disk.

## Gates
- test-writer: 7 unit (MapRows/LoadKlass2/CompositeVersion) + 3 Testcontainers-
  integration + 9 call-sites lagade. Båda testprojekt grön build.
- Unit: TaxonomySnapshotSeederTests 25, TaxonomyQueryHandlersTests 4 — gröna.
- Integration: TaxonomyReadModelIntegrationTests 16 — gröna (reverse-lookup
  resolvar `6YE1_gAC_R2G`→"Heltid", `PFZr_Syz_cUq`→"Vanlig anställning").
- FE: vitest 108 (6 berörda filer), tsc rent, eslint rent, pnpm build grön.
- dotnet format --verify-no-changes: rent.
- **code-reviewer: ✓ Approved 0/0/0** (2 icke-blockerande observationer, no-action).
- security-auditor EJ triggad: ingen ny PII/auth/secret/extern-integrations-yta
  (publik taxonomi-referensdata).

## Operativt
- Stack vid start: Docker uppe, men Api(5049)/FE(3000) nere (000) — flaggat;
  ej behövda för PR-1 (Testcontainers äger egen DB). Måste upp inför PR-2 visual-verify.
- `loadtest-reports/` otrackad sedan start (ej min).

## Nästa
PR-2 (FE Klass-2 filterpanel) mot PR-1:s `/taxonomy`-fält. Hård dependency
PR-1→PR-2→PR-3. Kurerings-frågan (8 rå vs Platsbankens 5) avgörs i PR-2
rendered-review med Klas.

---

## PR-2 (samma session) — FE Klass 2 filterpanel

Branch `feat/klass2-filter-panel` (off main efter PR-1 #61-merge).

**Klas-beslut (AskUserQuestion):** anställningsform-val = **ärliga 8** (vår korpus
1:1, riktiga JobTech-labels, ingen kurering/mappning) — ej Platsbankens kurerade 5.
Eliminerar mis-mapping-risk; ACL ärlig. Omfattning = radio (Platsbanken-bild),
Anställningsform = checkbox-multi.

**Bygge (nextjs-ui-engineer):** ny `jobb-klass2-panel.tsx` (enkolumns popover,
`role="radiogroup"` roving-tabindex för Omfattning + `role="group"` checkbox för
Anställningsform, per-sektion `.jp-clearlink`, `useDismissable`, "Visa N"-footer).
Plumbing: `JobbUrlState`/`buildJobbHref`/`ListJobAdsQuery`/`buildQuery`/`chip-models`
(DimensionAxis +2 + buildChipModels + buildTaxonomyLabelResolver) + tredje "Filter"-
pill i `jobb-hero-filters` (`useOptimistic` FilterSelection +2) + toolbar-chips
(Clock=omfattning, FileText=anställningsform) + page.tsx-parsning + no-JS hidden
inputs + "Rensa alla filter" +2. CSS `.jp-panel*`/`.jp-radioitem*` (accent-800-fyll =
checkitem-paritet). NO facet-counts (PR-3).

**Reviews:**
- code-reviewer: **1 Major + 1 Minor → in-block-fixade.**
  - Major: recent-search-replay tappade tyst Klass 2. Premissen "DTO bär inte
    fälten" var FEL — backend `RecentJobSearchDto.EmploymentTypeList/WorktimeExtentList`
    finns sedan B2/#60. Fix: FE-zod `recent-searches.ts` +2 fält; `recent-search-row`
    + `recent-searches-hero-chip` konsumerar dem; replay-test.
  - Minor: `sameUrlState` (tokenize.ts) kompletterad med de 2 dims.
- design-reviewer: **✓ Approved 0/0/2** (border-strong-indikatorkontrast 2,52:1 =
  pre-existing `.jp-checkitem`-baseline → TD-kandidat, ej in-block; worktime-ordning =
  rendered-review). FAS-DEFERRAL-MANIFEST för rendered (auth-gated /jobb, stack nere).

**Gates:** tsc rent, vitest 128 (bygge) + recent/tokenize/search-params 63 (efter fix),
pnpm build grön, eslint rent.

**Rendered-flaggor till Klas (lokalt/post-deploy):** (1) Omfattning Deltid-före-Heltid
(Label Ordinal) vs Platsbankens Heltid-först; (2) pill-namn "Filter"; (3) "Vanlig
anställning" (24k) synlig i honest-8; (4) dark/fokus/pilnav/NVDA per manifest.

**Känd uppföljning:** border-strong-indikatorkontrast (radio+checkbox-kontrakt,
tvärgående) — design-reviewer TD-kandidat, ej denna PR.

---

## PR-3 (samma session) — Klass 2 facet-counts (FE-only)

Branch `feat/klass2-facet-counts` (off main efter PR-2 #63-merge). Backend stödde
redan EmploymentType/WorktimeExtent i `FacetDimension` + `GetFacetCountsQuery` (B2).

- `FACET_DIMENSIONS` (job-ads.ts) +2; `facetDimensionSchema = z.enum(...)` följer →
  route-handler-allowlist auto-utökad.
- `FacetCountsFilterState` (hook) + `FacetCountsFilter` (api) + route-handler +
  `getFacetCounts`-buildQuery + hook-params: +employmentType/worktimeExtent. Klass 2
  ingår i facett-filtret → Ort/Yrke-facetterna reflekterar nu också Klass 2 (backend
  `ExcludeDimension` exkluderar egen dim). `filterKey` +2 (ingen stale count).
- `jobb-hero-filters`: `facetFilter` +2, 2 nya `useFacetCounts` gated på
  `openPop === "filter"`, props till panelen.
- `JobbKlass2Panel`: count-props + render `({n.toLocaleString("sv-SE")})` per
  Heltid/Deltid-radio + anställningsform-checkbox. "Alla"-radion bär INGET tal
  (SPOT = totalCount). null/saknad-nyckel-degradering = E2c-paritet.
- CSS `.jp-radioitem__count` co-selektor med `.jp-checkitem__count` (noll nya tokens).
- Tester: 4 nya count-tester (per-option, "Alla"-undantag, saknad-nyckel→0, null).

**Reviews:** code-reviewer **✓ 0/0/0**; design-reviewer **✓ 0/0/1** (moot — E2c
renderar också "(0)" för saknad nyckel när dicten finns). FAS-DEFERRAL-MANIFEST
rendered. Gates: tsc rent, vitest 38 (panel+hero), pnpm build grön.
