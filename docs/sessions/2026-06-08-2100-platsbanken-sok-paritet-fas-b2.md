---
session: Platsbanken sök-paritet — Fas B2 (data-layer, Klass 2)
datum: 2026-06-08
slug: platsbanken-sok-paritet-fas-b2
status: levererad — Fas B2-PR mot main (branch feat/sok-paritet-data-layer-b2)
bas-HEAD: 154fb07
branch: feat/sok-paritet-data-layer-b2
agenter:
  - dotnet-architect a419a20e6a4fc6b5c (POCO/STORED/migration-design + namnglapp-fälla)
  - senior-cto-advisor aaf6d17f29fc23ab4 (Beslut a Variant B re-ingest-trigger + b Variant A snapshot-defer + c Variant A strikt scope_of_work)
  - senior-cto-advisor a6e8d247d857d0116 (followup: Variant H delad backfill-kärna)
  - security-auditor abab6e7abef9c8608 (GO — icke-PII klassifikations-data)
  - test-writer af88e6b66d7b3fcfa (TDD FÖRST: 5 POCO + 4 generated-column)
  - db-migration-writer ac6eb1b8f7d03eb7f (F6P7 GO)
  - code-reviewer a177c52762e13dd93 (GO, 0 Blocker/0 Major/0 Minor)
commits:
  - "feat(jobads): Klass 2 STORED-kolumner (employment_type + worktime_extent) + re-ingest-trigger (Fas B2)"
  - "docs(sessions+reviews): Fas B2 docs-sync + agent-domar"
---

# Session: Platsbanken sök-paritet — Fas B2 (data-layer, Klass 2)

Fas B2 av sök-paritets-initiativet (ADR 0067). Klass 2-dimensionerna (anställningsform
+ omfattning) skiljer sig fundamentalt från B1: payloaden SAKNADE keys (POCO
deserialiserade dem aldrig) → STORED-kolumnerna blir NULL för alla ~44k rader tills
POCO-tillägg + full re-ingest. Det gjorde re-ingest-mekaniken sessionens tyngsta del.

## Mål
1. STEG 1 Discovery: verifiera POCO-frånvaro + live payload-path + dev-DB key-frånvaro.
2. STEG 2 agenter: architect + CTO (multi-approach a/b/c) + security + test-writer FÖRST.
3. STEG 3 impl: POCO + STORED + migration + re-ingest-trigger.
4. Verifiera mot Testcontainers. db-migration + code-reviewer. PR + automerge.

## Discovery-fynd (STEG 1, verifierat live + dev-DB)
- **Live JobTech-payload** (`curl jobsearch.api.jobtechdev.se/search?limit=1`), alla TOP-LEVEL:
  `employment_type {concept_id:"PFZr_Syz_cUq", label:"Vanlig anställning"}`,
  `working_hours_type {concept_id:"6YE1_gAC_R2G", label:"Heltid"}` (**NAMNGLAPP**:
  payload-fält `working_hours_type`, taxonomi/kolumn `worktime-extent`),
  `scope_of_work {min:100,max:100}` (procent, inget concept_id), `duration` (utanför scope).
- **dev-DB 44801 rader:** employment_type/working_hours_type/scope_of_work/duration = **0 rader**
  i raw_payload → re-ingest absolut nödvändig. occupation_group = 34843 (B1-baseline finns).
- POCO `JobTechHit` saknade fälten (väntat). Sanitizer-allowlist hade redan keys (rad 54).
- raw_payload byggs `Sanitize(JsonSerializer.Serialize(hit))` → POCO är flaskhalsen
  (allowlist nödvändig men ej tillräcklig).
- Re-ingest-mekanik: admin-snapshot-trigger avvecklad (410 Gone). Per-ID-refetch-precedens
  `backfill-ssyk` (STEG 6) löste EXAKT samma problem (POCO-fält tillagda, gamla payload
  saknar keys, snapshot trunkerar → per-ID deterministisk).

## Agent-domar
- **dotnet-architect** — F6P6-klon, POCO speglar JobTechOccupationGroup (prop följer wire-key,
  översättning på kolumn-nivå). 0-rad-backfill = load-bearing skillnad mot B1. Namnglapp-fälla
  (kolumn worktime_extent ↔ path working_hours_type) = enda raden där copy-paste-glidning ger
  tyst NULL. scope_of_work + Kind-utökning → CTO.
- **senior-cto-advisor (a/b/c)** — (a) **Variant B**: bygg idempotent re-ingest-trigger per
  backfill-ssyk-precedens, KÖR re-ingest = Klas-GO efter merge (ej PR-villkor; flaky extern I/O
  utanför CI). (b) **Variant A**: seeda INTE snapshot i B2 (defer C1/E; ADR B2-rad listar ingen
  snapshot-utökning — medvetet). (c) **Variant A strikt**: bara employment_type + worktime_extent
  concept-id; scope_of_work (procent, ej Platsbanken-filter) defer.
- **senior-cto-advisor (followup)** — **Variant H**: extrahera delad re-ingest-kärna
  (`JobAdRefetchBackfillRunner`), ssyk publik yta orörd, Klass2 = tunn wrapper. DRY/OCP +
  additiv-över-muterande (vs Variant G som muterar kört historiskt jobb). Rule-of-three ej
  blockerande (variationsaxeln empiriskt fastställd = predikatet).
- **security-auditor** — GO, inga fynd. Taxonomi-koder + statiska labels = icke-PII publik data;
  default-deny-sanitizer gör tillägget säkert by construction; ingen third-country/AI-yta.
- **test-writer** — TDD FÖRST: 5 POCO-deserialiserings + 4 generated-column (Testcontainers).
- **db-migration-writer** — F6P7 GO. Namnglapp korrekt i alla tre artefakter; 0-rad-backfill +
  ACCESS EXCLUSIVE dokumenterat; deploy-sekvens-not (OBS-2).
- **code-reviewer** — GO, 0 Blocker/0 Major/0 Minor. Namnglapp konsekvent i 7 beröringspunkter;
  Variant H ren; DI same-commit; audit-ratchet legitim.

## Decisions / detours
- **Variant H blast-radius på ssyk-test:** befintlig `BackfillJobAdSsykJobTests` (7 loop-tester)
  konstruerade jobbet med gamla konstruktorn → bröts när loopen flyttade till runnern. Lösning:
  retargetade testerna till runnern (`JobAdRefetchBackfillRunnerTests`, git-rename R080) — beteendet
  följde koden dit. CTO:s antagande att ssyk-test-ytan inte ändras var fel (det var ett unit-test,
  inte bara TD-97:s framtida integration-test), men retarget bevarar all täckning.
- **Audit-allowlist-ratchet:** `ISystemEventAuditor`-konsumtionen relokerades ssyk-jobb→runner →
  arkitektur-test `AuditingLayerTests` krävde allowlist-uppdatering (BackfillJobAdSsykJob →
  JobAdRefetchBackfillRunner). Legitim ratchet (runnern är system-job-lagrets enda nya consumer).
- **MTP-filter:** xUnit v3 / Microsoft.Testing.Platform — `--filter` finns ej; `--filter-query
  "/*/*/Klass/*"` fungerar för integration-projektet.
- **Re-ingest INTE körd:** per CTO Variant B + startpromptens Klas-STOPP-flagga är körningen av
  ~44k-re-ingest en separat operativ Klas-GO efter merge. Kolumnerna är NULL by design tills dess
  (dokumenterat i migration + endpoint + DoD).

## Verifiering (alla gröna, Testcontainers ej InMemory)
unit 635 · generated-column 9 (5 B1 + 4 B2) · integration 387 totalt · arkitektur 78.

## Nästa steg
1. Klas granskar B2-PR-diff (post-merge, automerge).
2. **KÖR re-ingest** efter merge (`POST /backfill-klass2`, Admin-auth; verifiera kolumn-count
   före/efter; ~2,5h vid default-throttle) — separat Klas-GO, operativt tyngsta steget.
3. **Fas C1** (query/filter-layer: filter-SPOT + ApplyCriteria + ListJobAds + ITaxonomyReadModel-DTO
   kommun/ssyk-level-4 + nivåbyte occupation-name→ssyk-level-4 + integration-tester). Klas-GO.
4. (Senare) C2 reverse-lookup-migration + VO-expansion, D1/D2 facets + parser, E FE + färg-identitet.
5. Api/Worker var nede vid sessionstart — starta om så F6P7 appliceras på dev-DB.
6. tmp/platsbanken/-screenshots raderas vid initiativ-slut.
