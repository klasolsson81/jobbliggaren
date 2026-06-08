# CTO-beslutsrapport — Platsbanken sök-paritet (Session 1, design-grind)

**Datum:** 2026-06-08
**Agent:** senior-cto-advisor (agentId `a2b9d182729075263`) — decision-maker. Architect ramade in; CTO fäller domarna. Klas sista ordet på flaggat.
**Underlag:** discovery (2026-06-08), architect-review (2026-06-08), ADR 0062/0043/0042/0039/0045/0032.
**Nästa fria ADR-nummer:** 0067.

---

## Q1 — Yrke-nivå: **Option A** (byt occupation-name → ssyk-level-4) + **reverse-lookup-migration** (ej graceful degradation). **KLAS-GO.**

Paritets-målet = Platsbanken filtrerar yrke på ssyk-level-4 (Evans ubiquitous language kap. 2/14). A krymper trädet ~2179→~400 noder → ADR 0043-dedup-skuld går mot noll (Evans kap. 14 ACL isolerar ej replikerar). B (båda nivåer) avvisad = spekulativ tredje nivå Platsbanken saknar (YAGNI/Fowler kap. 3). C (additivt) avvisad hårt = paritets-/ubiquitous-language-brott. Bakåtkompat: gamla sparade `Ssyk`-listor (occupation-name) → **reverse-lookup-migration occupation-name→parent ssyk-level-4** (deterministisk, en `broader` per name, distinct-normaliseras av ADR 0042 inv. 1). Graceful degradation avvisad: tysta noll-träffar på sparade bevakningar värre än synlig "Okänd kod" (CLAUDE.md §1 Mastercard). `JobAdFilterCriteria` förblir `IReadOnlyList<string>` (VO-Contains-fällan undviks). **Klas-GO:** rör ADR 0042/0043-garantier + sparad-sökning-migration; bekräfta att ingen occupation-name-granular roadmap finns (annars B på bordet).

## Q2 — Datamodell: **STORED för alla filtrerbara** + **Klass 1 → Klass 2-sekvens**. **KLAS-GO per migration.**

STORED entydigt mot ssyk/region/search_vector-precedens (Martin OCP/REP-CCP kap. 8/13); inline jsonb mot 40k bryter ADR 0045 (300ms p95 Klas-låst, CLAUDE.md §2.5). **Sekvens (hård regel):** Klass 1 (`municipality_concept_id`, `occupation_group_concept_id`) — payload finns → STORED ADD COLUMN populerar utan re-ingest → Fas B1. Klass 2 (`employment_type`, `worktime_extent`) — POCO saknar props → raw_payload saknar keys → STORED NULL tills full re-ingest → POCO-tillägg + allowlist + kolumn + re-ingest + explicit i DoF → Fas B2. Klumpa ihop = "falsk klar". Sekvensen är CTO:s (CC följer utan extra GO); migrationen mot 40k bär Klas-GO.

## Q3 — Kommun: **single ParentConceptId + Kind-diskriminator + additiv DTO** (bekräftar architect). **KLAS-GO (ADR 0043-amendment).**

Single parent räcker godtyckligt djup; `Kind` ÄR nivån (separat `Level`-int = DRY-brott/redundant state). `TaxonomyConceptKind += Municipality` (parent=Region), `+= OccupationGroup` (parent=OccupationField). `TaxonomyRegionDto += IReadOnlyList<TaxonomyMunicipalityDto> Municipalities` (speglar OccupationField→Occupations, OCP additivt, port-signatur oförändrad). Kommun→län 1:1 → ingen dedup. ADR 0043 Beslut E föreskriver SJÄLV Klas-GO + amendment för denna batch — vi följer dess inskrivna protokoll.

## Q4 — Distans: **DEFER** + payload-verifierings-trigger i ADR-text (ej TD). **KLAS-input (lutning defer).**

work-place-model ej toppfält/ej POCO. (a) relation-lookup = extern hop, bryter ADR 0042 rad 21/0043 → avvisas. (b) annan key = overifierad (ADR 0043 Beslut E-grund). (c) text-heuristik = låg precision, förorenar FTS (ADR 0062). Lutning: defer tills kärn-dimensioner gett paritet, sedan riktad discovery. Distans-i-leverans = Klas-override → tillbaka till CTO för (b)-vs-(c).

## Q5 — Facet-counts: **total nu (lever); per-option = `FacetCountsAsync` ny metod på `IJobAdSearchQuery` (EJ ny port), NBomber FÖRE.** Bygg-beslut CTO; budget-brott kan eskalera.

Total-count lever via `CountAsync`. Per-option = GROUP BY-aggregat per dimension (facetterad dimension exkluderad ur WHERE = facett-semantik). Ny port avvisad = duplicerar ApplyCriteria-filter → SPOT-brott (ADR 0062 Beslut 3); ny metod faller under ADR 0062 Beslut 4 provider-assembly-axel. N separata counts avvisade (N+1, ADR 0060). Full defer avvisad (kärn-UX-krav Klas §1.5). Ny omätt hot-path → **NBomber BLOCKING före live** (ADR 0045 300ms, CLAUDE.md §2.5); budget-brott → cache-strategi (ADR 0064-analog) → eskalerar till Klas. Facett-exkluderings-semantik specas explicit i ADR.

## Q6 — Query-token-parser: **`ISearchQueryParser` i Application** (lager = CTO-beslut). **Semantik additiv-vs-AND = KLAS-STOPP** (lutning: additiv/OR + disambiguerings-chip).

Parsern = ACL (Evans kap. 14), generalisering av `IOccupationSynonymExpander` (DRY), ren CPU i Application (Martin kap. 22, ingen Npgsql). Kontrakt `Parse(string)→ParsedSearchQuery(SsykConceptIds, RegionConceptIds, EmploymentTypeConceptIds, ResidualQ)`; handler slår ihop + bygger `JobAdSearchCriteria`; Infrastructure-port oförändrad. **Semantik = produktbeslut** (rör TD-86 #1 recall-gap 198 vs 800+): hård AND ↑precision ↓recall (förvärrar gapet); additiv/OR ↑recall ↑brus. CTO-lutning: **additiv/OR default + synlig disambiguerings-affordance** (klickbart filter-chip "Menade du yrket Lärare?" — recall-bevarande, opt-in precision, civic-utility kontroll). **KLAS-STOPP** — Klas väljer recall/precision-balansen. Fas D2 (efter dimensionerna finns).

## Q7 — VO vs runtime: **VO = occupation_group, municipality, employment_type, worktime_extent, work_place_model; runtime = facet-counts, parsade tokens** (rå Q sparas). Princip-entydigt (ej Klas-GO).

ADR 0039 Beslut 3-test (sökningens identitet → VO; presentation → runtime). Varje VO-fält bär 4 ADR 0042-invarianter (normalisering/cap/tom/jsonb-bakåtkompat) → egen testbar batch (test-writer FÖRST, security-auditor cap). **Q1×Q7-interaktion:** Q1=A:s `Ssyk`-nivåbyte ÄR en VO-bakåtkompat-händelse → reverse-lookup-migration (Q1) + VO-expansion (Q7) designas ihop i Fas C2. Ordning: C1 (runtime-query) före C2 (VO).

## Q8 — Faser: **B→B1/B2, C→C1/C2, D→D1/D2. Session 1 = ENDAST Fas A (ADR:er).**

| Fas | Innehåll | Klas-GO | Session |
|---|---|---|---|
| A | Design + ADR 0067 (Proposed) + ADR 0043-amendment (Proposed) | Accepted-flip = Klas | **Session 1** |
| B1 | Klass 1 STORED (municipality+occupation_group) + EF + migration + snapshot (kommun+ssyk-level-4) + seeder. Ingen re-ingest | JA (40k-migration + amendment) | Nästa |
| B2 | Klass 2 STORED (employment_type+worktime_extent) — POCO + allowlist + migration + full re-ingest (NULL tills cron, i DoF) | JA (POCO+re-ingest) | Senare |
| C1 | Runtime-query: filter-SPOT + ApplyCriteria + ListJobAdsQuery + ITaxonomyReadModel + validators + integ-test | JA (Q1=A nivåbyte) | Efter B |
| C2 | SearchCriteria-VO-expansion + Q1 reverse-lookup-migration + jsonb-bakåtkompat (test-writer FÖRST) | JA (sparad-sökning-migration) | Efter C1 |
| D1 | Total-count + per-option facet-counts (FacetCountsAsync, NBomber FÖRE) | NBomber-utfall kan eskalera | Efter C1 |
| D2 | Query-token-parser — additiv-vs-AND = Klas-STOPP | JA (Klas-STOPP) | Sist backend |
| E | FE-UI: kaskad + Filter-panel + live-count + Rensa + ny färg-identitet | design-reviewer VETO + Klas-GO | Efter backend |
| Tvärgående | Recall-gap-mätning (TD-86 #1) efter B2+C1 | — | Mätpunkt |

**Session 1 levererar endast Fas A** (CLAUDE.md §9.2 — fas-skifte till kod-mot-40k-data kräver Klas-GO; design-grind stänger med ADR:er, ej migrationer). B1-start = nästa session efter Klas läst ADR + GO Accepted-flip + B1-migration.

## TD-triage (§9.6/§9.7 — CC-mandat, ej Klas-GO)

- **TD-86:** sök-fas-2-triggern uppfylld → aktiveras + **splittras**. Markeras `ERSATT 2026-06-08 av sök-paritets-initiativet (ADR 0067)`. #1 recall-gap → tvärgående mätpunkt (in-fas); #3 query-parser → absorberas Q6/Fas D2 (in-fas); #4 P2-backfill → verifierings-punkt Fas B2 (in-fas); #2 common-term-perf → behåll smal efterföljar-TD om ej löst av Q1=A-selektivitet, re-mät efter C1.
- **TD-100:** behåll **Trigger**, korsref ADR 0067, aktiveras Fas E (FE-dependency saknas än). Dess 100%-paritets-spec + SSYK-verifiering = Fas E acceptance criteria.

## ADR-beslut: **EN ny ADR 0067 (Proposed) + ETT ADR 0043-amendment (Proposed).** Accepted-flip = Klas.

ADR 0067 = Q1/Q2/Q5/Q6/Q7/Q8 + Q4-defer (REP/CCP — ett knowledge piece "hur sök-ytan når Platsbanken-paritet"; split avvisad = cross-ref-spindelnät, ADR 0045-precedens). ADR 0043-amendment = Q3 (kommun, Kind+=Municipality/OccupationGroup) + Q1 träd-nivå-skifte + hävande av "shadow-prop ORÖRD"-garanti (ADR 0043 Beslut E föreskriver själv amendment-vägen; amendment ej supersession — ACL-kärnan består). ADR 0042 amendas EJ (invarianterna tillämpas, ändras ej; korsref i 0067 räcker — Klas bedömer ev. additivt 0042-korsref-notat).

## Klas-flaggade beslut (sammanfattat)
1. **Q1** reverse-lookup-migration vs degradering (CTO valde migration; occupation-name-roadmap → B istället).
2. **Q4** distans defer vs prioritera nu.
3. **Q6** parser-semantik additiv/OR (CTO-lutning) vs AND.
4. **ADR Accepted-flip** + ev. ADR 0042-korsref-notat.

**Källor:** Evans DDD (2003) kap. 2/5/14; Martin Clean Architecture (2017) kap. 8/13/22; Beck/Fowler Refactoring 2nd (2018) kap. 3; Hunt/Thomas (1999) DRY/SPOT; Ford/Parsons/Kua (2017) kap. 2/4; Microsoft Learn (generated columns/CQRS); ADR 0062/0043/0042/0039/0045/0032/0060/0064; CLAUDE.md §1/§2.1/§2.4/§2.5/§9.2/§9.6/§9.7.
