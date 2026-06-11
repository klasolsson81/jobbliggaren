# Current work — JobbPilot

**Status:** **PLATSBANKEN SÖK-PARITET — E2b MERGAD (#46 `cb42575`) + E2e MERGAD (#47 `0a4f48d`) + FAS E2c LEVERERAD 2026-06-11 (autonom natt-körning, branch `feat/sok-paritet-facet-counts-e2c`).** E2c: `GET /api/v1/job-ads/facet-counts` (residual-konsistent handler, IsInEnum-skydd, FacetCountsPolicy 30/10s — security-auditor BLOCKING-fastställd) + VAL 4-ort-exkludering i `ExcludeDimension` + **NBomber-gaten uppfylld FÖRE FE-wiring: p95 = 26,8/25,0 ms ≪ 300 ms-budgeten (×11-marginal, 0 fails)** + FE per-option-counts i popover-raderna ("Solna (12)", tre-tillstånds-semantik känd-nolla/okänt) + "Visa N annonser"-stängknapp (totalCount via useSyncExternalStore-store — aldrig facett-summa). Reviews: security APPROVED (0 fynd, 30/10s fastställt) · design Approved 0 VETO (1 Major singular-böjning + 1 Minor — åtgärdade in-block) · code 0 Block (2 Major degraderings-502 + abort-cleanup — åtgärdade in-block). E2d = HÅRD STOPP (chip/residual-bekräftelse saknas) → morgonrapport levererad. **Tillägg samma session (Fas E2f — Klas rendered-feedback på morgonen):** tom kaskad-kolumn tills vänster-val (Platsbanken-paritet), kommun-rader renderas markerade vid helläns-val + "hela länet minus en kommun"-semantik (`toggleMunicipalityInRegion`, preciserar CTO VAL 1 till PÅ-toggling), de-gröna rubriker (aktiv nav/drawer → ink-text med accent-bar som indikator; jobb-/app-titlar, träffräknar-tal, peek-titlar, översikts-datum → ink — "grönt = interaktion, inte information"). Reviews: code Approved 0/0 (4 Minor — 3 in-block, 1 opportunistisk), design 0 VETO/1 Major spec-sync (globals.css + ADR 0068-notat fixade in-block; **DESIGN.md + tokens-skillen = spec-edit → Klas approve-hook**). ADR 0067-notat (E2f) + ADR 0068-notat (accent-text-rollen) skrivna. **Fas E2g (Klas bugg + label-direktiv, samma dag):** hero-öns filterval synkar nu vid externa URL-ändringar (chip-×/Rensa/recent-navigering) via `useOptimistic` (CTO Variant A — props/URL är enda sanningen); recent-search-labels visar "Data/IT" vid helt yrkesområde, "{första} +N till" vid flera val (kommuner/regioner samma mönster, graceful vid taxonomi-drift). Design-fasen G1–G4 är KLAR och MERGAD (#42–#45; G4 landing-redesign mergad `c43a9d8` — current-works tidigare "G4 pending Klas-GO" var stale). E2b byggd under Klas-förauktoriserad autonom automerge-auktoritet (natt-prompt 2026-06-11): kritiskt architect-fynd (backend-AND region×kommun ≠ Platsbankens verifierade union-semantik) → CTO VAL 1 = **Variant D backend geo-union** (region∪kommun när båda satta; ort = EN dimension i två granulariteter) + per-län-normalisering FE; "Hela länet"-rad togglar ETT region-id (aldrig materialiserade kommun-ids). Reviews: code-reviewer 1 Block/1 Major — **båda åtgärdade in-block** + 3 Minor (2 fixade, 1 = pre-existing saved-searches-zod-drift till Klas-triage); security-auditor **APPROVED 0 fynd**; design-reviewer **Approved 0 VETO/0 Major** ("Hela länet"-label explicit godkänd). Full backend-svit 1771 grön efter test-uppdatering; 735 vitest, tsc/eslint/build gröna. Nästa: E2e (Rensa/sortering) → E2c (facet-counts + NBomber observe-only) per natt-promptens ordning; E2d = HÅRD STOPP (chip/residual-bekräftelse saknas).

**Levererat denna session 2026-06-11 (Fas E2h-PR — chips-i-sökfältet, branch `feat/sok-paritet-chip-in-field-e2h`):**

- **Klas produktspec efter E2d-rendered-test** (val sökte direkt + tömde fältet = fel flöde): val av förslag → **tagg-chip I sökfältet**; ALLT blir taggar (även fritext-ord, en chip per q-ord — wire-ärligt per ADR 0062); mellanslag/komma avgränsar; live-resultat per tagg; fältet nollställs aldrig av ett val; Tab väljer markerat förslag. Specen = Klas eget auto-chip-produktbeslut (övertrumfar E2d-VAL-3-baslinjen). Sparad i memory `project_e2h_chip_in_field_spec`.
- **CTO VAL 1=A:** chips deriveras HELT ur URL:en (useOptimistic; enda lokala staten = utkastet) — E2d-buggklassen orepresenterbar. **VAL 2=B:** `router.replace`+`{scroll:false}` för fält-commits (toolbar pushar — dokumenterad asymmetri). **VAL 3=A:** recent-capture-mellansteg accepteras/observeras. **VAL 4=B:** ny `ChipSearchField` komponerar `JobAdTypeahead` (additiva `selectOnTab`/`onEmptyBackspace`/`inputRef`).
- **Ny ren tokenizer** (`lib/job-ads/tokenize.ts`, 17 tester): exakt UNIK ci-taxonomi-match → dimension-chip via oförändrad `composeSuggestionChip` (SPOT); ambiguitet/miss → fritext-q; ledande `+`/`-` strippas (**`-`-strip = NOT-neutralisering** — websearch_to_tsquery-upptäckten; minus-operatorn förblir genuint Klas-pending); q-max-FE-guard (`Q_MAX_LENGTH`=100 delad konstant).
- **SPOT-extraktion in-block:** delade `buildChipModels`/`removeChipFromState` (chip-models.ts) för toolbar + fält (E2d-CTO F4-mandatet levererat); toolbarens useState-kopior → useOptimistic-prop-derivering.
- **Reviews:** design-reviewer **VETO (2 Blockers WCAG 2.4.7/2.4.3 + 4 Major) → alla in-block-åtgärdade → re-review Approved**; code-reviewer 0 Block/2 Major (utkast-sentinel-avvikelse → CTO-addendum-ACK; testgap → 5 nya edge-case-tester) + 4 Minor (fixade). **818 vitest** (+39), tsc/eslint/build gröna. ADR 0067 impl-notat (E2h) skrivet. Rendered = pending Klas.

**Levererat denna session 2026-06-11 (Fas E2d-PR — typeahead-chip-komponist, MERGAD #51 `1061bc2`):**

- **`JobAdTypeahead` wirad live (ADR 0067 Beslut 5b):** kontrakt `onSelect(string)`→`onSelect(SuggestionDto)`; full WAI-ARIA combobox (pil-nav + `aria-activedescendant` + `role="option"`, `onMouseDown`-fokusbevarande). Taxonomi-förslag → strukturerat dimension-chip via `composeSuggestionChip` + `router.push(buildJobbHref)`; fri text / Title-förslag → q (residual-fritext, recall-bevarande ADR 0062/D2 — aldrig hårt AND).
- **Ny `JobbHeroSearch`-ö (CTO VAL 1 = Variant A):** separat ö bredvid `JobbHeroFilters`, båda skriver samma URL via `buildJobbHref` (SPOT, tredje konsument); URL = ENDA sanningen (E2g — chip-state ur props, ingen useState-kopia; utkast synkar via prev-prop-sentinel under render, ej effect). **No-JS GET-form-fallback bevarad (§5.2)** med hidden inputs.
- **`composeSuggestionChip` (ren FE-funktion + 11 tester):** exhaustiv kind→dimension-switch + `assertNever`. **VAL 2a:** OccupationField → materialisera barn-yrkesgrupper. **VAL 2b:** Occupation förbli ute (status quo). **VAL 2c GATED:** EmploymentType design-redo via exhaustiv switch (compile-tvång vid B2), WIRAS EJ (NULL-data, falsk-klar-disciplin).
- **VAL 3 = auto-chip-vid-submit → KLAS-STOPP (architect + CTO):** "volvo göteborg heltid"+Enter utan tabb = substantiellt D2 VAL 1:s avvisade token-lyft + ändrar submit-recall-semantik = Klas-STOPP-gated. EJ byggd; baslinjen (hela strängen → q) är Accepted-default. **Klas väljer A/B/C (STOPP-fråga i PR-rapporten).** Blockerar EJ resten av E2d.
- **Ärvda E2d-Minors (E2f-review):** `selectAllLabel` → funktion "Hela {länsnamn}"; `dialogLabel`-prop (dialog aria-label = "Ort"/"Yrke"); tri-state `aria-checked="mixed"` vid partiellt val.
- **Reviews:** code-reviewer Approved 0 Block/0 Major/2 Minor; design-reviewer Approved 0 Blocker/0 Major/2 Minor (Minor 1 rateLimited-overflow åtgärdad in-block). security-auditor EJ triggad (ingen ny backend-input-yta). tsc/eslint/**779 vitest** (var 758+; +21 netto)/pnpm build gröna. ADR 0067 impl-notat (Fas E2d) skrivet.

**Levererat denna session 2026-06-11 (Fas E2c-PR — autonom natt-körning):**

- **Backend:** `GetFacetCountsQuery`/handler/validator (residual-konsistens: Q genom `ISearchQueryParser` — samma WHERE som listan; `ICapturesRecentSearch` medvetet ej implementerat; ingen `Total` — SPOT mot `PagedResult.TotalCount`); `IsInEnum()` mot numerisk out-of-range-bindning (400, ej 500); endpoint med `FacetCountsPolicy` 30/10s/user (CTO VAL 1, least common mechanism — security-auditor BLOCKING-fastställd) + `private, no-store`. **VAL 4:** `ExcludeDimension` exkluderar HELA ort-dimensionen för Municipality/Region-facetterna + 4 Testcontainers-tester (inkl. geo-union-ärvning via SPOT).
- **NBomber (Beslut 4-gaten procedurellt):** D1-parkerade scenarierna aktiverade, omkalibrerade till FacetCountsPolicy-aritmetiken (15 req/10s < 30/10s — D1:s kalibrerings-fel löst strukturellt), reellt Stockholms-läns-id. **Lokal mätning FÖRE FE-wiring: p95 26,8 ms / 25,0 ms (0 fails) ≪ 300 ms** — fallback-trappan behövdes ej. Observe-only består (flip = Klas-lås).
- **FE (CTO VAL 2 = A):** per-option-counts i popover-raderna via `use-facet-counts`-hook (debounce 300ms + AbortController + enabled-gating) → route-handler (dimension-allowlist; fel → 502, ALDRIG 200+{} — "(0)" vid backend-fel vore desinformation); "Hela länet"-raden bär region-facettens count; "Visa N annonser"-stängknapp med singular-böjning (totalCount via ny `total-count-store`, useSyncExternalStore — toolbar publicerar, hero prenumererar; öarna saknar gemensam client-förälder pga streaming).
- **Reviews (alla in-block-åtgärdade):** security-auditor APPROVED 0 Crit/High/Med (3 Low ej blockerande; 30/10s-talen verifierade mot uppmätt p95) · design-reviewer 0 VETO/1 Major (singular) + 1 Minor (13px) · code-reviewer 0 Block/2 Major (502-degradering + abort-cleanup) + 3 Minor. ADR 0067 impl-notat (Fas E2c) skrivet.

**Levererat denna session 2026-06-11 (Fas E2e-PR — MERGAD #47 `0a4f48d`):**

- **Rensa = röd text-länk (ADR 0067 rad 109):** ny kanonisk `.jp-clearlink` (`--jp-danger`, underline, `<button>` — "ej knapp" = visuell behandling) ersätter accent-gröna `.jp-popover__clear` (noll rester, grep-verifierat). Popover-Rensa + ny "Rensa alla filter" i toolbaren (nollar tre axlar, bevarar q+sortBy+pageSize, gated på chips). WCAG AA båda teman.
- **Sort-labels per Klas-prompt:** "Relevans / Datum (nyast) / Ansökningsdatum (sista ansökan)". "(CV-match)"-faktafelet rättat (ADR 0042 Beslut F). ExpiresAtAsc verifierad: asc NULLS LAST + Id-tiebreak = sista-ansökan-snart-först.
- **Tester +4** (Rensa-beteende/synlighet/sortBy-bevarande, label-uppsättning); 739 vitest, tsc/eslint/build gröna. ADR 0067 impl-notat (Fas E2e) skrivet.
- **Reviews:** code-reviewer Approved (2 Minor: sortBy-test fixad in-block; label-vs-ADR-ordalydelse dokumenterad i notatet), design-reviewer Approved (3 Minor: kontrasttabell-sync = spec-edit → Klas; fokus-polish ärvd pre-existing; hover-state medvetet icke-val).

**Levererat denna session 2026-06-11 (Fas E2b-PR — MERGAD #46 `cb42575`):**

- **Backend geo-union (CTO VAL 1 Variant D, ~10 rader + 5 Testcontainers-tester):** `ApplyCriteria` unionerar region∪kommun när BÅDA listorna är icke-tomma (sekventiellt AND gav noll träffar för region=län-X + kommun-i-län-Y). Web-verifierat (architect + CTO oberoende): JobTech/Platsbanken kombinerar geografi-filter inkluderande ("most local promoted"). Ensamma grenar oförändrade; AND mot yrke/q består; SPOT bevarad. Recall-garanti test-låst med syntetisk region-only-annons. Mekanik-konkretisering inom Accepted ADR 0067 (ingen ADR 0042-amendment — Beslut B beslutade aldrig region×kommun; klargörande-not tillagd i ADR 0042).
- **Ort-pickern Län→Kommun (TD-100 kommun-paritet):** två-kolumns kaskad via samma `JobbFilterPopover`; **dual-axis-kontrakt** (CTO VAL 3 — `groupAxis`-props, Yrke = degenererat enaxel-fall, ingen mode-flagga; enkelkolumns-läget borttaget, noll konsumenter). "Hela länet"-raden togglar region-id (en chip, 414-skydd); kommun-rader togglar `?municipality=`. **Per-län-normalisering** (`lib/job-ads/ort-selection.ts`, ren funktion + 12 unit-tester): kommun-val släcker länets helläns-val och vice versa — UX-kosmetik ovanpå unionen, ingen korrekthets-bärare.
- **`?municipality=` atomiskt (E2a-mönstret, EN commit):** taxonomy-zod (`municipalities` REQUIRED, occupations strippas fortsatt), buildJobbHref + buildPageHref (paginering — F3-felklassen täppt) + hidden inputs + Suspense-key + selectedConceptIds + toolbar-chips (region → kommun → yrkesgrupp, delad MapPin) + recent-shim (`municipalityList`/`municipalityLabels` konsumeras, fanns wire-side sedan C2).
- **C2-shimmet borttaget:** `RecentJobSearchDto.SsykList`/`SsykLabels` raderade (architect F5-planen "tas bort i Fas E" utförd); kontraktstest-vakthund mot återuppståndelse; wire-frånvaro asserterad i integ-tester.
- **"Obestämd ort/Utomlands" DEFERRAD med payload-trigger (CTO VAL 2):** snapshotten saknar noderna; de 1 293 ortlösa annonserna saknar BÅDA dimensionerna (per-län-rad vore död UI-yta). Explicit rest mot ADR 0067 rad 109 — TD-100-stängning kräver löst/Klas-accepterad.
- **E2c-spec låst (CTO VAL 4):** ort-facetten i `FacetCountsAsync` ska exkludera HELA ort-dimensionen (region+municipality) ur WHERE — dagens `ExcludeDimension` tömmer bara municipality (latent, noll konsumenter; byggs rätt i E2c).
- **Agent-domar (`docs/reviews/2026-06-11-sok-paritet-e2b-*.md`):** dotnet-architect (variant-analys A/B/B′/C/D + web-verifierad JobTech-semantik), senior-cto-advisor (VAL 1–4, INGEN HALT — inom Accepted mandat), code-reviewer (1 Block + 1 Major åtgärdade in-block), security-auditor (APPROVED 0 fynd), design-reviewer (Approved; "Hela länet"-dom; 2 Minor → E2d-touchen). ADR 0067 implementerings-notat (Fas E2b) + ADR 0042 klargörande-not skrivna.

**Tidigare session 2026-06-10 (Fas E2a-PR — MERGAD #41):**

- **Yrke-nivå-skifte (TD-100-kärna):** FE-taxonomy-DTO `occupationFields[].occupations` → `occupationGroups` (ssyk-level-4); occupation-name droppad ur FE (ACL — recall-substrat backend-side). Pickern matar yrkesgrupp-ids. Empiriskt verifierat: 400 yrkesgrupper populerade (t.ex. "Advokater", "Arbetsförmedlare").
- **Atomisk `?ssyk=`→`?occupationGroup=`** över buildJobbHref/buildQuery/page/picker/results+toolbar/recent (Fowler Rename Field, TS-säkrad). recent-shim `ssykList`→`occupationGroupList`. Cap 10→400.
- **Agent-domar** (`docs/reviews/2026-06-10-sok-paritet-e2a-reviews.md`): architect (E2a-spec), code-reviewer/security-auditor/design-reviewer APPROVED. ADR 0067 impl-notat (Fas E2a) skrivet.

**Tidigare session 2026-06-10 (Fas E1a-PR — MERGAD #40):**

- **/jobb-hero "Papperskontoret" (riktning A):** navy-banner → varm papperston-canvas. Ny `--jp-hero-canvas` (#FAF9F6 light, ärver `--jp-canvas` #0B1525 dark, /jobb-scoped — rör ej app-wide `--jp-canvas`; architect-dom). `.jp-pagehero` (inre sidor) orörd. Alla hero-barn flippade vit-på-navy → ink-på-papper via tokens. Sök-knapp navy-800 primary (ADR 0052). Dark: ljust sökfält + mörk text.
- **Regel-1-fixar:** drop-shadow → border (papper); 40px-titel → 28px H1-token; 12px verifierat redan compliant (6px, oförändrat — ärligt rapporterat). Microcopy: H1 "Lediga jobb", label "Sök efter yrke, arbetsgivare eller ort", placeholder "t.ex. systemutvecklare Göteborg".
- **Ny `--jp-placeholder`-token (#626B78):** WCAG AA ≥4.5:1 (#FFFFFF 5.39:1, #F0F4FB 4.89:1) → löste design-reviewer-VETO (2 Blockers placeholder-kontrast light+dark).
- **Agent-domar:** nextjs-ui-engineer (bygge), dotnet-architect (token-arkitektur), design-reviewer VETO→APPROVED (`docs/reviews/2026-06-10-sok-paritet-e1a-design-review.md`). ADR 0067 impl-notat (Fas E1a) skrivet.

**Tidigare session 2026-06-10 (Fas E1b-PR — MERGAD #39):**

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
| `2922a25` | #41 | Fas E2a — yrke-nivå-skifte → yrkesgrupp (ssyk-level-4) |
| `7140c6b` | #42 | Fas G1 — grön accent-identitet + F4-banner (ADR 0068) |
| `74a25a9` | #43 | Fas G2 — banner-konsekvens (Sök jobb, 1136-alignment, F4-platta-rollout) |
| `08abb7b` | #44 | Fas G3 — konsekvensfixar (Sök jobb top-left, vit pagehero-CTA, a:hover-rotfix) |
| `c43a9d8` | #45 | Fas G4 — landing-redesign (produkt-forward ljus hero, login → topbar) |
| `cb42575` | #46 | Fas E2b — Län→Kommun-kaskad + geo-union region∪kommun |
| `0a4f48d` | #47 | Fas E2e — Rensa-röda-textlänkar + sorterings-labels |
| `65c3c8f` | #48 | Fas E2c — facet-counts-endpoint + NBomber-gate + FE live-counts |
| `e4071fe` | #49 | Fas E2f — tom kaskad-kolumn + markerade kommun-rader + de-gröna rubriker |
| (denna) | — | fix/sok-paritet-e2g-state-sync-recent-labels — hero-ö-synk (useOptimistic) + recent-labels (+N till / hel-områdes-kollaps) |

---

## Pending operativt för Klas

1. **Post-merge-granskning E2b (ADR 0065 automerge):** PR-diffen + Vercel-rendering av Ort-pickern (Län→Kommun, "Hela länet"-label) — design-reviewer godkände mot kod/diff; rendered-verifiering var pending live-deploy per runbook (auth-gated /jobb). Notera ram-utvidgningen: ~10 backend-rader geo-union (CTO-dom — Platsbanken-semantik var union, inte AND; full motivering i `docs/reviews/2026-06-11-sok-paritet-e2b-cto.md`).
2. **KLAS-STOPP — chip/residual-kombinationssemantik (ADR 0067 Beslut 5):** krävs INNAN E2d wirar chip+residual. Bekräfta `(dim-predikat) AND (FTS ∨ title-LIKE ∨ synonym)`. Natt-promptens bekräftelse-rad lämnades tom → E2d HALT.
3. **Klas-triage — `saved-searches.ts`-zod-drift (pre-existing, code-reviewer Minor):** FE-schemat kräver `ssyk`/`ssykLabels` men backend `SavedSearchDto` bär OccupationGroup/Municipality/Region sedan C2 — latent hård zod-fail för första FE-konsument av sparade sökningar. Egen touch innan saved-search-FE-ytan byggs.
4. **Logo-översyn (separat, Klas-ägd):** guld `#FFCD00` vs handoffens `#E8C77B` + og/twitter-wordmark — tas när du vill.
5. **Re-ingest Klass 2** (`POST /api/v1/admin/job-ads/backfill-klass2`, ~2,5h) — blockerar Anställningsform/Omfattning-filter (gated tills körd). Kör EJ utan Klas-GO.
6. **CLAUDE.md §11.3-drift** (`make dev`/`pnpm dev:up` finns ej) — skapa-vs-stryk-beslut vid nästa spec-touch (kvarstår).
7. **"Obestämd ort/Utomlands"** — deferrad med payload-verifierings-trigger (ADR 0067 impl-notat E2b); explicit rest mot TD-100-stängningen.

---

## Historik

All tidigare session-historik (Fas D1, editor-baseline, Fas C2 och bakåt): **`docs/current-work-archive.md`** (omvänd kronologi) + per-session-loggar i **`docs/sessions/`**.
