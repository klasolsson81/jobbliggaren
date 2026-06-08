# Arkitektur-analys — Platsbanken sök-paritet Fas B2 (data-layer, Klass 2)

**Datum:** 2026-06-08
**Agent:** dotnet-architect (agentId `a419a20e6a4fc6b5c`) — teknisk inramning; multi-approach går till senior-cto-advisor.
**Underlag:** ADR 0067 Beslut 2/6/7, on-disk- + live-payload-verifiering 2026-06-08.

## Sammanfattning
OK att gå vidare — inga §2.1-brott, B1-mönstret (F6P6) klonar rent till B2. Sju tekniska inramningar nedan, två flaggade som CTO-multi-approach (scope_of_work-deserialisering + TaxonomyConceptKind-utökning). Den enda strukturella skillnaden mot B1 är load-bearing och måste in i migration-kommentaren: **ADD COLUMN populerar 0 rader** (raw_payload saknar keys tills POCO-tillägg + full snapshot-re-ingest), tvärtemot B1 där rewrite backfillade ~34k rader direkt.

## 1. POCO-tillägg (JobTechHit)
Spegla `JobTechOccupationGroup`-mönstret exakt. Två nya nested-klasser (`JobTechEmploymentType` + `JobTechWorkingHoursType`, båda `{concept_id, label, legacy_ams_taxonomy_id}`) + två props på `JobTechHit` (`EmploymentType` ← `employment_type`, `WorkingHoursType` ← `working_hours_type`). **Naming-rek (entydig):** POCO-property speglar payload-fältet (`WorkingHoursType`), inte taxonomi-typen. POCO är wire-format-ACL:n mot JobTech — namnglapp-översättningen (working_hours_type → worktime_extent) görs på STORED-kolumn-nivå, inte i POCO (matchar B1 där prop heter `OccupationGroup` men kolumnen `occupation_group_concept_id`). Sanitizer kräver ingen ändring — keys finns redan i allowlist (rad 54); allowlist nödvändig men ej tillräcklig (POCO måste deserialisera).

**scope_of_work (CTO-fråga):** ADR 0067 Beslut 2 låser endast employment_type + worktime_extent. Timing-asymmetri: re-ingest sker NU ändå → att deserialisera scope_of_work nu är gratis, retro-fit kräver ny re-ingest. Men beslut (ta med/skippa) → senior-cto-advisor.

## 2. STORED EF-config (JobAdConfiguration.cs)
Efter `MunicipalityConceptId`-blocket. `EmploymentTypeConceptId` ← `raw_payload->'employment_type'->>'concept_id'`; `WorktimeExtentConceptId` ← `raw_payload->'working_hours_type'->>'concept_id'`. **Load-bearing detalj:** kolumn `worktime_extent_concept_id` ↔ shadow-property `WorktimeExtentConceptId` ↔ computedColumnSql läser `working_hours_type`. Tre namn, en mapping — enda raden där copy-paste-glidning från B1 (`->'worktime_extent'`) ger tyst alltid-NULL utan kompileringsfel. Pre-flight-check (§9.4) på denna rad.

## 3. Migration (F6P6-klon)
EN migration (båda kolumnerna = samma Klass 2-enhet, delad re-ingest-livscykel, delat rewrite-fönster). Kommentaren MÅSTE avvika från F6P6: B1 sa "backfill automatiskt, ingen re-ingest" — falskt för B2. ADD COLUMN backfillar INGENTING (raw_payload saknar keys för alla rader). Partial-index initialt tomt, växer med re-ingest. `Down()`: DROP INDEX IF EXISTS (båda) → DropColumn (båda).

## 4. Namnkonventions-inkonsistens
**Följ ADR — inte öppen fråga.** ADR 0067 Accepted, Beslut 2 låser `worktime_extent_concept_id`. B1 namngav efter payload-key, B2 worktime_extent efter taxonomi-typ → en av två kolumner bryter B1-mönstret. Kosmetisk inkonsistens utan funktionell konsekvens (interna shadow-properties). Neutraliseras av explicit namnglapp-kommentar. Enhetlighet framåt = ADR-amendment-fråga, ej B2-blockerare.

## 5. TaxonomyConceptKind-utökning — CTO-multi-approach (b)
employment-type (~6) + worktime-extent (~2) = platta enum-liknande taxonomier utan hierarki. **Väg A (flata Kinds i snapshot):** Kind += 2, TaxonomySnapshotFile + MapRows + snapshot-bump v30→v31, labelByConceptId plockar upp dem auto → label-resolution gratis. **Väg B (hårdkodad enum):** ingen snapshot-touch men parallell label-källa (DRY-spänning, project_crossref_badge_status relevant). Konsistens-vs-enkelhet → CTO avgör. Båda Clean Arch-rena.

## 6. TDD-ordning till test-writer
Testcontainers, INTE InMemory (STORED beräknas bara i riktig Postgres). 1. STORED-populering (ny payload-form) — fångar namnglapp. 2. NULL-spärr (gammal form). 3. Index-existens. 4. POCO-deserialisering (unit). 5. Round-trip (om Väg A).

## 7. Blast-radius + Clean Arch
§2.1 inga brott. Hela B2 i Infrastructure: POCO internal wire-ACL, STORED shadow-properties (Domain orörd, Evans kap. 14), migration + ev. Kind Infrastructure-only. Blast-radius: raw_payload-shape ändras (nya keys), befintliga kolumner oförändrade, sanitizer oförändrad, inga befintliga queries bryts. Full re-ingest enda vägen att populera (admin-trigger 410 Gone). Deploy-sekvens: POCO+migration → re-ingest → filter-UI.

## Referenser
CLAUDE.md §2.1/§3.6/§5.1/§7/§9.4/§9.6; ADR 0067 Beslut 2/6/1; ADR 0043 + amendment; ADR 0032 §4/§8; Evans 2003 kap. 14; Saltzer/Schroeder 1975; memory `feedback_ef_strongly_typed_vo_contains_translation`, `feedback_adr_mechanism_vs_env_phase_triage`, `project_crossref_badge_status`.
