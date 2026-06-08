# Code-review — Fas B2 Platsbanken sök-paritet (ADR 0067 Beslut 2)

**Status:** ✓ GO — Approved, mergeklar
**Granskad:** 2026-06-08
**Agent:** code-reviewer (agentId `a177c52762e13dd93`)
**Auktoritet:** CLAUDE.md §2.1/§2.3/§3/§3.6/§5/§7/§9.6
**Scope:** Backend — Infrastructure (data-layer + migration), Application (re-ingest-kärna + wrappers), Api (endpoint), Worker (DI + Hangfire-wrapper), tester. 17 filer, +2032/−253.
**Veto:** Ej utnyttjat.

## Sammanfattning
**0 Blocker, 0 Major, 0 Minor.** Disciplinerad förändring där varje icke-trivialt val är kommenterat med rotorsak + ADR-referens. Namnglappet (`working_hours_type` → `worktime_extent`) — största korrekthets-risken — konsekvent genomfört i alla sju beröringspunkter + grindat av tester. Variant H-extraktionen följer DRY/OCP rent utan att bryta ssyk:ens publika yta. Inga anti-pattern-träffar.

## Verifierade korrekthetspunkter
1. **Namnglapp — genomgående konsekvent** i POCO (`JobTechSearchResponse.cs:114` prop speglar wire-key), config, migration, snapshot, sanitizer (allowlist pre-existing), unit-test (positiv bind + `ShouldNotContain("worktime_extent")`), integration-test (isolerad path-spärr mot Postgres).
2. **Clean Arch (§2.1) — intakt.** Domain orörd. Runnern i Application, ingen Hangfire-referens. `[DisableConcurrentExecution]` i Worker-wrappern. Shadow-properties (ingen domän-förorening).
3. **Variant H (DRY/OCP) — ren.** Delad kärna tar Expression-predikat + options + auditJobType; nytt behov = ny tunn wrapper (OCP). ssyk reducerad till delegation, publik signatur oförändrad. Testfil-rename R080 bevarar git-historik.
4. **§3.6 LINQ — korrekt.** `.AsNoTracking()`, `.AsAsyncEnumerable()` streamar (Worker-mem-cap), projektion till ExternalId (ingen SELECT *), deterministisk OrderBy, per-item child-scope.
5. **§5 anti-patterns — inga träffar.** IDateTimeProvider injicerad, inget .Result/.Wait/dynamic, ingen tom catch (Errors++ + logg), OperationCanceledException re-throwar, CancellationToken propagerad hela kedjan.
6. **§7 testkrav — uppfyllt med marginal.** Runner 7 loop-tester (beteendet följde koden till runnern), POCO +5 inkl namnglapp-spärr, integration +4 Testcontainers (Testcontainers över InMemory korrekt val — HasComputedColumnSql ignoreras av InMemory).
7. **DI i samma commit (`feedback_di_with_handlers_same_commit`) — uppfyllt.** Runner + Klass2-options (Bind+Validate+ValidateOnStart) + Klass2-job + Worker-wrapper i samma staged set. Inget broken intermediate state.
8. **Audit-allowlist-ratchet — legitim.** `BackfillJobAdSsykJob` → `JobAdRefetchBackfillRunner` (audit-konsumtion relokerad till delad kärna; wrappers konsumerar ej auditorn direkt). Återanvänder JobAdsSynced med JobType="backfill-klass2".
9. **§9.6 in-block vs TD — korrekt fas-disciplin.** Hela B2 in-block. Backfill-körningen korrekt avgränsad som Klas-GO-grindad operativ åtgärd (ej TD). Kolumner NULL by design tills körning, dokumenterat i migration + endpoint + jobb.

## Mindre observationer (ej fynd, FYI)
- `AdminJobAdsEndpoints.cs` `CancellationToken.None` i Hangfire-enqueue — korrekt (Hangfire injicerar egen token vid exekvering; expression-token platshållare). Speglar /backfill-ssyk.
- Migration-kommentaren noterar ADD COLUMN STORED = full table rewrite/ACCESS EXCLUSIVE (Hetzner-deploy). Bra att paus-effekten är i koden, ej upptäckt i drift.

## Delegationer
Inga. Inga Blocker/Major att åtgärda; inga testluckor till test-writer.

**Dom: GO.** Mergeklar. Sätt `automerge`-labeln per ADR 0065 Amendment 2026-06-07.
