---
session: Platsbanken sök-paritet — Fas B1 (data-layer, Klass 1)
datum: 2026-06-08
slug: platsbanken-sok-paritet-fas-b1
status: levererad — Fas B1-PR mot main (branch feat/sok-paritet-data-layer-b1)
bas-HEAD: 01a6039
branch: feat/sok-paritet-data-layer-b1
agenter:
  - senior-cto-advisor aec40007a9f5aba41 (Beslut 1 Variant C snapshot-gen + Beslut 2 Variant A B1/C1-gräns)
  - dotnet-architect af8a82f5460a193ba (EF/migration/snapshot-design + table-rewrite-korrigering)
  - test-writer a15ef458ae239eaf8 (TDD-tester FÖRST)
  - db-migration-writer ace490d3aa344d752 (F6P6-migration)
  - code-reviewer aef2328aba967a6ac (GO, 0 Blocker/Major, security-auditor ej i scope)
commits:
  - "feat(jobads): Klass 1 STORED-kolumner + taxonomi-snapshot kommun/yrkesgrupp (Fas B1)"
  - "docs(sessions+reviews): Fas B1 docs-sync + agent-domar"
---

# Session: Platsbanken sök-paritet — Fas B1 (data-layer, Klass 1)

Fas B1 av sök-paritets-initiativet (ADR 0067). Data-layer-grunden för kommun- +
yrkesgrupp-dimensionerna: STORED generated columns + taxonomi-snapshot-utökning.
Klass 1 = payload finns redan i raw_payload → ingen re-ingest. Autonomt utfört
efter usage-reset (sessionen pausades 16:05 på 97% usage, återupptogs 17:38 via
ScheduleWakeup-hopp).

## Mål
1. STEG 1 Discovery: verifiera on-disk att raw_payload bär Klass 1-keys.
2. STEG 3 agenter: CTO (multi-approach) + architect (design) + test-writer FÖRST.
3. STEG 2 impl: STORED-kolumner + EF-config + migration + Kind-enum + snapshot + seeder.
4. Verifiera mot Testcontainers. code-reviewer. PR + automerge.

## Discovery-fynd (STEG 1, verifierat dev-DB + on-disk)
- dev-DB 44 801 rader: `occupation_group.concept_id` i 34 843 (= ssyk-paritet),
  `workplace_address.municipality_concept_id` i 33 935 (= region-paritet). **Klass 1-tesen
  håller** — STORED ADD populerar från befintlig raw_payload utan re-ingest.
- Sample: occupation_group = `{label:"Mjukvaru- och systemutvecklare m.fl.", concept_id:"DJh5_yyF_hEM"}`
  (ssyk-level-4 bekräftad via "m.fl."-markör), municipality `AvNB_uwa_6n6` (Stockholm).
- Sanitizer-allowlist + POCO (JobTechHit.OccupationGroup top-level + WorkplaceAddress
  .MunicipalityConceptId) bekräftade. occupation_group är **TOP-LEVEL**, ej nested.
- GraphQL: 290 kommuner + 400 ssyk-level-4, **exakt 1:1 parent** (ingen dedup),
  alla 21+21 parent-ids matchar befintlig snapshot.

## Agent-domar
- **senior-cto-advisor** — Beslut 1 = **Variant C** (committat genererings-script
  `tools/taxonomy-snapshot/`, JSON är sanningskälla, version 29→30; reproducerbarhet
  SE@Google kap.18, determinism, betalar ner ADR 0043 Beslut B-skuld). Beslut 2 =
  **Variant A** (B1 seedar bara nya Kind-rader; LoadAsync/TaxonomyTreeDto orörda → C1;
  YAGNI/SRP/fas-disciplin). Ingen extra Klas-GO.
- **dotnet-architect** — F2P9-klon, occupation_group TOP-LEVEL-path (ej nested).
  **Korrigering:** STORED ADD = full table rewrite/ACCESS EXCLUSIVE (discovery-briefen
  hade fel "metadata-billig"); lokalt sekunder mot 44k. Kind string-persisterad (säkert).
  Snapshot nullable-nested (bakåtkompat). labelByConceptId resolverar nya labels auto (bra C2).
- **test-writer** — TDD FÖRST: 5 generated-column-tester (Testcontainers, path-spärr),
  utökade MapRows/unique-id/round-trip, 2 seeder-integration-tester.
- **db-migration-writer** — F6P6JobAdKlass1SearchColumns (två AddColumn stored + två
  partial-index via raw SQL, F2P9-mönster). Flaggade WaitlistEntry-snapshot-konsolidering.
- **code-reviewer** — GO, 0 Blocker/0 Major/3 Minor (behåll). security-auditor ej i scope
  (taxonomi-koder = icke-PII, ingen endpoint/auth/loggyta).

## Decisions / detours
- **Test-fix:** JobAdGeneratedColumnsTests `SqlQueryRaw` aliasade först resultatkolumner
  till PascalCase → EF härleder förväntade kolumnnamn via snake_case-namnkonventionen →
  "required column municipality_concept_id not present". Fix: råa snake_case-kolumnnamn
  utan alias. Produktionskoden var korrekt (SQL kördes → kolumner finns).
- **EF-snapshot-konsolidering:** AppDbContextModelSnapshot tog bort ett duplicerat
  WaitlistEntry-relationsblock (pre-existerande artefakt från ExtendWaitlistEntryWithAcceptance).
  Verifierat benignt (kanoniskt block intakt, alla migrationer applicerar mot Testcontainers).
  Behålls (att hand-återinföra dubblett vore strikt sämre).
- **Snapshot-script är additivt** (ej full regenerering) → bevarar exakt den kanoniskt
  dedupliserade occupation-name-datan (2323, byte-identisk).

## Verifiering (alla gröna, Testcontainers ej InMemory)
unit 630 · generated-column 5 · taxonomi-integration 10 · arkitektur 78 · ListJobAds-filter 13 (regression).

## Nästa steg
1. Klas granskar B1-PR-diff (post-merge, automerge).
2. **Fas B2** (Klass 2: employment_type + worktime_extent — POCO-tillägg + allowlist +
   migration + full re-ingest; kolumner NULL tills snapshot-cron kört, explicit i DoD) —
   **eller** **Fas C1** (query/filter-layer: filter-SPOT + ApplyCriteria + ListJobAds +
   ITaxonomyReadModel-DTO kommun/ssyk-level-4 + nivåbyte + integration-tester). Klas-GO per fas.
3. (Senare) C2 reverse-lookup-migration, D1/D2 facets + parser, E FE + färg-identitet.
4. tmp/platsbanken/-screenshots raderas vid initiativ-slut.
