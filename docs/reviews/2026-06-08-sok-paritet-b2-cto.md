# CTO-beslutsrapport — Platsbanken sök-paritet Fas B2 (data-layer, Klass 2)

**Datum:** 2026-06-08
**Agent:** senior-cto-advisor (agentId `aaf6d17f29fc23ab4`) — decision-maker. CC ger ingen egen rek (`feedback_cto_decides_multi_approach`). Klas sista ordet på flaggat.
**Underlag (on-disk-verifierat 2026-06-08):** ADR 0067 Beslut 2/3/5/7 + Konsekvenser. `JobTechSearchResponse.cs` (POCO — Klass 2-fält saknas), `JobTechPayloadSanitizer.cs:54` (allowlist innehåller redan employment_type/working_hours_type/scope_of_work/duration), `SyncPlatsbankenSnapshotJob.cs` (recurring child-scope, ~47k idempotent upsert, trunkerings-floors), `AdminJobAdsEndpoints.cs` (snapshot-trigger 410 Gone; **`backfill-ssyk` STEG-6-precedens** — Hangfire fire-and-forget, idempotent, NULL-filter, Klas-UI-trigger), `TaxonomyConceptKind.cs` (5 Kinds), `tools/taxonomy-snapshot/generate.mjs` (Variant C-precedens).

---

### BESLUT (a) — Re-ingest-trigger lokalt + verifiering: **Variant B, formad efter STEG-6-precedensen — leverera path NU, kör re-ingest med Klas-GO**

B2-PR:n levererar (1) POCO-tillägg + (2) allowlist-verifiering + (3) STORED-migration + (4) tester **och** en idempotent, restart-säker engångs-trigger för full re-ingest som följer den etablerade `backfill-ssyk`-precedensen. Re-ingest-**körningen** mot de ~44k raderna är en **Klas-GO-grindad operativ åtgärd**, inte en del av PR-diffen. DoD dokumenterar explicit NULL-tillståndet tills körningen skett (ADR 0067 Beslut 2 "falsk klar"-kravet). Konkret en **mildrad Variant C** (defer körningen) **plus byggd trigger-väg**.

**Motivering:** STEG-6 (`backfill-ssyk`) etablerade exakt mönstret B2 behöver — idempotent, NULL-filter-restart-vänligt engångsjobb, fire-and-forget mot Worker-Hangfire, Klas-UI-styrt. B2:s NULL→populerat är **samma klass** som ssyk-backfillen (DRY på lösnings-mönster, Hunt/Thomas 1999 kap. 7). Att binda PR-DoD till en faktiskt körd ~300MB JobTech-GET (rate-limit, trunkering 21-364MB, 3-försök retry) gör merge beroende av flaky extern I/O utanför CI (Ford/Parsons/Kua 2017 kap. 4 hermeticitet). ADR 0067 kräver INTE körning i PR:n — bara att NULL-tillståndet är explicit i DoD. Path-korrekthet verifieras mot Testcontainers med sample (ej 44k-körning) — test-pyramidens poäng.

**Avvisat:** Variant A (tvinga Hangfire-schedule/dashboard — bräcklig, icke-reproducerbar). Ren Variant B (ny ad-hoc CLI — uppfinner andra mönster, SRP/REP-spänning). Ren Variant C (defer allt — underlevererar, ingen observerbar lokal verifiering).

**Klas-GO:** **JA — flaggad.** (1) 44k-migrations-GO = del av PR (fas-GO). (2) Att **köra** ~300MB re-ingest = separat operativ GO efter merge. CC kör INTE 44k-re-ingest utan explicit GO; PR-merge kräver ej körd ingest.

---

### BESLUT (b) — Taxonomi-snapshot-utökning för Klass 2-labels: **Variant A — seeda INTE i B2; defereras till C1/E**

`TaxonomyConceptKind` utökas inte med EmploymentType/WorktimeExtent i B2. Snapshot rörs inte. employment-type (5) + worktime-extent (2) seedas först där label-exponering konsumeras (C1 DTO / E UI).

**Motivering:** Exakt B1-precedens (B1-CTO Beslut 2 = Variant A, stare decisis). YAGNI/Speculative Generality (Beck; Fowler kap. 3) — filtrering sker på concept-id, inte label; label-resolution behövs först i D1/E. SRP/change-reason (Martin kap. 7). **Asymmetri mot B1 noterad:** ADR 0067 Beslut 7 B1-raden listar uttryckligen "taxonomi-snapshot-utökning + seeder", B2-raden gör det INTE — medvetet utelämnande som matchar YAGNI. Om B2 tvingas röra snapshoten → STOPP (scope-glidning).

**Klas-GO:** Nej utöver fas-GO.

---

### BESLUT (c) — scope_of_work-hantering: **Variant A (strikt) — håll B2 till de två concept-id-dimensionerna; scope_of_work defereras**

B2 lägger till POCO-typer + STORED-kolumner endast för employment_type + worktime_extent. scope_of_work {min,max} (procent, inget concept_id) modelleras inte.

**Motivering:** ADR 0067 Beslut 2 låser scope explicit (scope_of_work nämns ej). Platsbanken-paritet är måttstocken: "Omfattning"-filtret = worktime_extent (Heltid/Deltid), inte procent — scope_of_work är inte ett Platsbanken-filter → per definition utanför B2. YAGNI (Fowler kap. 3). Strukturell skillnad: scope_of_work saknar concept_id → passar inte STORED-`->>'concept_id'`-modellen (annan kolumn-form, annan change-reason, SRP). Beslut 3-trigger-instrumentet (payload-verifierings-trigger) gäller om framtida behov uppstår.

**Klas-GO:** Nej utöver fas-GO.

---

### Sammanfattad dom

| Beslut | Vald variant | Klas-GO? |
|---|---|---|
| (a) Re-ingest-trigger | **B (mildrad C + byggd trigger per STEG-6-precedens)** | **JA — flaggad:** köra ~300MB re-ingest = separat operativ GO efter merge; PR-merge kräver ej körd ingest |
| (b) Snapshot-utökning | **A** — seeda INTE; defer C1/E; om snapshot tvingas → STOPP | Nej utöver fas-GO |
| (c) scope_of_work | **A (strikt)** — bara employment_type + worktime_extent; scope_of_work defer | Nej utöver fas-GO |

**On-disk-fynd som skärper impl (in-block):** (1) allowlist har redan keys → steg "sanitizer-allowlist-verifiering" = verifiering, ej tillägg. (2) `[JsonPropertyName("working_hours_type")]` på WorktimeExtent/WorkingHoursType-prop (payload ≠ kolumn). (3) Utöka `JobTechHitDeserializationTests` med Klass 2-fält (deserialize + round-trip + through-sanitizer).

**Referenser:** Martin *Clean Architecture* (2017) kap. 7/13/22/28; Evans *DDD* (2003) kap. 2; Fowler *Refactoring* 2nd (2018) kap. 3; Beck (YAGNI); Hunt/Thomas (1999) kap. 7; Winters et al (2020) kap. 18/22; Ford/Parsons/Kua (2017) kap. 4; ADR 0067 Beslut 2/3/5/7; ADR 0043 Beslut B + amendment; ADR 0032 §9-amendment; B1-CTO Beslut 2; CLAUDE.md §2.4/§2.5/§9.2/§9.6.
