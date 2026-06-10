# code-reviewer — Platsbanken sök-paritet Fas D1

**Datum:** 2026-06-10
**Agent:** code-reviewer (agentId `a756cc96331ca905a`)
**Verdikt:** ✓ Approved — mergeklar. **0 Block / 0 Major / 2 Minor (FYI)**
**HEAD:** `06b7840` (working tree, branch `feat/sok-paritet-facets-d1`)

## Granskning per fokusområde

1. **SPOT (ADR 0039 Beslut 1) — GODKÄND.** `FacetCountsAsync` återanvänder `ApplyCriteria` som enda filter-väg; exkludering via `ExcludeDimension` (`criteria with { X = [] }`), ingen `ApplyCriteriaExcept`-duplikat. `ShadowColumn`/`ExcludeDimension`-switcharna duplicerar ej filter-kunskap (äger GroupBy-kolumn resp. tömd lista, ej predikat). Status=Active ärvs via ApplyCriteria.
2. **GROUP BY-LINQ — GODKÄND.** NULL-nyckel-exkludering korrekt (`.Where(EF.Property != null)` före `.GroupBy`); `EF.Property`-nyckel translaterbar (samma mönster som produktions-ApplyCriteria); AsNoTracking ärvs; projektion till anonym typ före materialisering.
3. **Union-handler — GODKÄND.** Dedup-nyckel korrekt; limit-cap över unionen; titel-LIKE-escape oförändrad (ADR 0042 Beslut C); CancellationToken propagerad.
4. **ACL — GODKÄND, ren.** `TaxonomyConceptKind` (internal) läcker ej; `MapKind` översätter före gräns-passage, fail-fast throw; Occupation utesluten före MapKind.
5. **§5/§3.6/nullable/async — GODKÄND.** Inga anti-patterns; AsNoTracking + projektion + Take-cap; Async-suffix konsekvent.
6. **Tester — GODKÄND.** Exkludering (båda sidor), NULL-nyckel, dedup, cap täckta; Testcontainers (ej InMemory) för GROUP BY-vägen.

## Minor (FYI — inga åtgärdskrav, ingen TD)
1. `#pragma warning disable CA2012` upprepad i tre testfiler (etablerat ValueTask-stub-mönster; delad helper vore YAGNI).
2. `FacetCountsScenarios.cs` route/query = PLACEHOLDER — korrekt hanterat (authored, ej registrerad, dokumenterad anti-falsk-klar).

## Måste i PR-body (CTO VAL 5)
Suggest-kontraktet bryts medvetet `IReadOnlyList<string>` → `IReadOnlyList<SuggestionDto>`; `web/.../job-ad-typeahead.tsx` inkompatibel tills Fas E. Inget shim. Passiv Klas-medvetenhet, ej blocker.

**Åtgärdat in-block efter review:** dedup-kommentaren i handlern justerad (Minor sammanföll med security-auditor).
