# Granskningsrapport — Fas B2 DB-migration `F6P7JobAdKlass2SearchColumns`

**Datum:** 2026-06-08
**Agent:** db-migration-writer (agentId `ac6eb1b8f7d03eb7f`)
**Scope:** Platsbanken sök-paritet, ADR 0067 Beslut 2 (Klass 2)
**Filer:** `20260608205054_F6P7JobAdKlass2SearchColumns.cs` (+ Designer), `JobAdConfiguration.cs`, `AppDbContextModelSnapshot.cs`

## Dom: GO
Migrationen är hygienisk, symmetrisk och konsistent över alla tre artefakter (migration, EF-config, snapshot). Inga blockerande fynd. Inga destruktiva operationer i `Up()`. Inga GDPR-kontroller berörs (computed columns härleds från redan sanerad raw_payload, ADR 0032 §8 — ingen ny PII-yta).

## Verifiering mot checklista
1. **STORED ADD COLUMN-mekanik — OK.** Båda kolumnerna `type:"text"`, `nullable:true`, `stored:true`. JSON-path matchar EF-config + snapshot exakt (employment_type top-level; working_hours_type top-level).
2. **Namnglapp-korrekthet — OK (kritiskt fynd-fritt).** `worktime_extent_concept_id` läser `->'working_hours_type'->>'concept_id'` i **alla tre** artefakter (migration, EF-config, snapshot). Ingen pekar fel på `->'worktime_extent'`. Namnglappet dokumenterat + regressionsskyddat (`JobAdGeneratedColumnsTests`).
3. **Partial-index — OK.** Raw SQL, `WHERE … IS NOT NULL`, korrekt namn (ix_job_ads_employment_type_concept_id + ix_job_ads_worktime_extent_concept_id). Raw SQL motiverad (Npgsql fluent saknar partial-index för shadow-props, B1/F2P9-mönster). `Down()` droppar index FÖRE kolumner.
4. **ACCESS EXCLUSIVE / full table rewrite — OK.** Dokumenterad i kommentar (Hetzner-deploy-lås-fönster). 0-rad-backfill-skillnaden mot F6P6 tydligt dokumenterad.
5. **Down()-symmetri — OK.** Up() skapar 2 kol + 2 index; Down() tar bort 2 index + 2 kol.
6. **Idempotens / re-run — OK.** Designer staged, F6P7 efter F6P6 (205054 > 155047). 9 Testcontainers-tester gröna. `DROP INDEX IF EXISTS` för säker re-run.

## Observationer (icke-blockerande)
- **OBS-1:** Drop-ordning mellan två oberoende kolumner irrelevant (inga FK/computed-beroenden). Ingen påverkan.
- **OBS-2 (deploy-koordinering, redan dokumenterad):** 100% NULL-kolumner tills re-ingest. Tills backfill-klass2-jobbet körts är filter ett no-op (0 träffar) — F6 P4-mönstret. Deploy-sekvens (migration → POCO → re-ingest) måste hållas. Avsiktligt + dokumenterat (migration rad 31-32). Deploy-koordineringspunkt, ej migrationsfel.

## GDPR-kontroller
Soft delete: N/A. Audit: N/A (computed columns). Encryption: N/A (härleds från sanerad raw_payload; ingen ny PII-yta; TD-13-yta oförändrad).

## Sammanfattning
Ren B1-klon med korrekt hanterad namnglapp-fälla + korrekt dokumenterad 0-rad-backfill-skillnad. Tre artefakter i full överensstämmelse. **GO för apply.** Enda operativa villkoret = deploy-sekvensering (OBS-2), redan dokumenterad i koden.
