---
session: d1-facet-counts-suggest-union
datum: 2026-06-10
slug: d1-facet-counts-suggest-union
status: levererad (PR mot main, automerge-label, ci-gate pending)
commits: 0632e2d (feat) + docs-sync (denna)
bas-HEAD: 06b7840
---

# Session 2026-06-10 — Platsbanken sök-paritet Fas D1

## Mål

Fas D1 per ADR 0067 Beslut 4 (facet-counts) + Beslut 5a (utökad typeahead-suggest):
`FacetCountsAsync` med facett-exkludering, NBomber-gate (BLOCKING 300 ms p95),
och taxonomi-union-suggest med `{kind, conceptId, label}`-kontrakt.

## Vad som levererades

### FacetCountsAsync (Beslut 4)
- Ny metod på `IJobAdSearchQuery` (EJ ny port — ADR 0062 Beslut 3 SPOT). GROUP BY
  shadow-column i Infrastructure (ADR 0062 Beslut 4 provider-assembly-axel).
- Facett-exkludering via `ExcludeDimension` = `criteria with { X = [] }` (tom lista =
  inget filter, befintlig semantik). SPOT bevarad — `ApplyCriteria` är enda filter-vägen,
  ingen `ApplyCriteriaExcept`-duplikat (dotnet-architect A3).
- `ShadowColumn`-switch (dimension→kolumnnamn, Infrastructure-hemlighet). NULL-shadow
  exkluderas → ingen null-nyckel (matchar partial-index-predikatet).
- `FacetDimension = {OccupationGroup, Municipality, Region}` — B2-dims uteslutna (CTO VAL 1).

### Utökad suggest (Beslut 5a)
- `SuggestByPrefixAsync` på `ITaxonomyReadModel` (in-memory prefix-scan av cachad snapshot;
  ny `Suggestable`-fält i CacheState, förberäknat). `MapKind` översätter internal
  `TaxonomyConceptKind` → publik `SuggestionKind` (ACL — kritiskt on-disk-fynd: enumen
  är `internal`, får ej korsa Application-gränsen).
- Union-handler: taxonomi först (deterministisk enum→label-ordning), sedan titel-prefix
  (oförändrad LIKE-escape-gren), dedup, cap. `SuggestionDto(Kind, ConceptId?, Label)`.
- occupation-name uteslutet (saknar filter-dimension — CTO VAL 4); recall bevaras ändå
  via q-FTS-synonym-grenen.

## Beslut & detours

### CTO 5 multi-approach-val (`a3e62a54498790c30`)
1. FacetDimension-scope → Variant A (3 dims, B2 uteslutna; falsk-klar).
2. Endpoint nu? → Variant A (port-only D1; endpoint = Fas E; `CountAsync`-prejudikat).
3. Kind-modell → Variant B (ny `SuggestionKind`-enum; §5.1 + ACL).
4. Occupation i suggest? → Variant A (uteslut; chip utan filter-mål).
5. Suggest-shim? → Variant A (inget shim; transient read-API ≠ C2-persistens-shim).

### NBomber-reconcile (CTO `a4773166ed750ce7f`, Väg B)
Spänning upptäckt mitt i: VAL 2 (port-only) gör NBomber-HTTP-gaten omätbar i D1
(ingen endpoint). CTO-dom: ADR 0067 Beslut 4:s villkor är "före per-option **går live**"
— och VAL 2 lägger "live" i Fas E. Plus ADR 0045 Beslut 5/6: NBomber är observe-only
produkt-gate, ej CI-gate. → Väg B: författa instrumentet (`FacetCountsScenarios.cs`),
registrera det INTE i aktiv körning, bind exekvering till Fas E. Dokumenterad defer
(anti-falsk-klar). Ingen Klas-STOPP — informations-flagga i PR-body.

### Detours
1. **`dotnet test`-filter-syntax:** MTP/xUnit v3 — `dotnet test --filter` fungerade ej.
   Körde test-exe direkt med native `-filter "/asm/ns/class/method"` query-språk. 23/23 gröna.
2. **Arch-test fångade ny ACL-konsument:** `TaxonomyAclLayerTests` allowlist exakt-set —
   `SuggestJobAdTermsQueryHandler` är femte legitim `ITaxonomyReadModel`-konsument
   (query-handler). Lade till i allowlisten (additivt). Pre-commit fångade detta korrekt.
3. **Format-gate:** switch-expression-whitespace i `JobAdSearchQuery.cs` → `dotnet format`
   fixade, verify-no-changes grön.

## Reviews
- code-reviewer (`a756cc96331ca905a`): 0 Block / 0 Major / 2 Minor (FYI — CA2012-pragma-upprepning,
  NBomber-placeholder; båda korrekt hanterade). Mergeklar.
- security-auditor (`a8d51280b806479ee`): 0 Crit / 0 High / 0 Major / 1 Minor (dedup-kommentar-hygien
  → fixad in-block). NOLL GDPR-brott (concept-id = publik referensdata, ej PII).

## Status & nästa session
- Backend src + tester bygger 0 warn/0 err; format-verify grön; 23 D1-tester gröna.
- Stacken (Api 5049/Worker/FE 3000) var NERE vid sessionsstart — startas om + verifieras
  före sessionsslut (memory-rutinen).
- Nästa: Fas D2 (`ISearchQueryParser` — Klas-STOPP) eller Fas E (FE-picker) — Klas-GO.
- Öppet för Klas: NBomber-gate Väg A vs B (default B); ev. mellanliggande FE-deploy → shim-omprövning.
