# dotnet-architect — Platsbanken sök-paritet Fas D1 (FacetCountsAsync + utökad suggest)

**Datum:** 2026-06-10
**Agent:** dotnet-architect (agentId `a109c11b4169441fe`)
**Scope:** ADR 0067 Beslut 4 (facet-counts) + Beslut 5a (suggest-union). Design-dom — ingen kod.
**HEAD vid analys:** `06b7840`

---

## Sammanfattning

OK att bygga — ramen är Clean-Arch-kompatibel och vilar på etablerad SPOT (ADR 0039/0062).
Två multi-approach-beslut till senior-cto-advisor innan CC kör (FacetDimension-scope +
suggest-DTO-kind-modell + endpoint-nu); resten ADR-låst. **Kritisk on-disk-låsning:**
`TaxonomyConceptKind` är `internal` i Infrastructure och kan **inte** korsa Application-gränsen —
suggest-unionens `kind` får inte återanvända den enumen.

## A. FacetCountsAsync

- **A1 (entydig):** ny metod på befintlig `IJobAdSearchQuery` (ny port förbjuds, ADR 0062 Beslut 3 SPOT).
  `ValueTask<IReadOnlyDictionary<string,int>> FacetCountsAsync(JobAdFilterCriteria criteria,
  FacetDimension dimension, CancellationToken ct)`. Rå concept-id→count, namn-omedveten (ADR 0043 Beslut E).
- **A2 (multi-approach→CTO):** `FacetDimension`-enum i `Application.JobAds.Abstractions` (public, del av portkontrakt).
  Lutning: `{OccupationGroup, Municipality, Region}` — uteslut B2-dims (NULL-data tills re-ingest = falsk-klar).
- **A3 (entydig):** exkludering via **klona criteria med X-listan tömd** (`criteria with { X = [] }` — empty=no-filter
  befintlig semantik), EJ `ApplyCriteriaExcept`. SPOT/DRY (ADR 0039 Beslut 1) — `ApplyCriteria` förblir enda filter-vägen.
- **A4 (entydig):** GROUP BY i **Infrastructure** (ADR 0062 Beslut 4 provider-assembly-axel). Privat `static
  ShadowColumn(FacetDimension)`-switch i impl:en (kolumnnamn är Infra-hemlighet); `.Where(EF.Property<string?>(j,col)
  != null)` före GroupBy → ingen null-nyckel, matchar partial-index-predikatet.
- **A5 (entydig):** retur `IReadOnlyDictionary<string,int>` concept-id→count, rå.
- **A6 (multi-approach→CTO):** D1 levererar bara port-metod + impl + Testcontainers-test (lutning); endpoint = Fas E.

## B. Utökad suggest

- **B1 (multi-approach→CTO):** ny DTO `SuggestionDto(string Kind, string? ConceptId, string Label)` (ConceptId
  nullable — titel-träffar saknar concept-id). `SuggestJobAdTermsQuery` returtyp `IReadOnlyList<string>` →
  `IReadOnlyList<SuggestionDto>`. **Kind får ej vara `TaxonomyConceptKind` (internal).** Lutning: ny `public enum
  SuggestionKind` (§5.1 anti-magic-string).
- **B2 (delvis entydig):** union i Application-handlern. Titel-prefix = befintlig `IAppDbContext`-query. Taxonomi-prefix
  = **ny metod på `ITaxonomyReadModel`** (`SuggestByPrefixAsync` — exponera EJ snapshoten, ACL). Porten returnerar
  redan-resolvade labels. Vilka kinds: Region/Municipality/OccupationField/OccupationGroup självklara; **Occupation
  (multi-approach→CTO, lutning uteslut).** Handler: union+dedup på `(Kind, ConceptId ?? Label)`+Take(limit).
- **B3 (entydig):** behåll `SuggestPolicy` (30/10s). Validator oförändrad (prefix 2–100, limit 1–20).
- **B4 (multi-approach, låg vikt→CTO):** FE-kontraktsbrott `string[]`→objekt. Lutning: inget shim (transient read-API,
  ingen persistens, ≠ C2 RecentJobSearchDto-shim); flagga i STOPP-rapport.

## C. Leverans & topologi

- **C1:** kontrakts-typer först (tomma/NotImplemented) → test-writer (Testcontainers) → Infra-impl → Application-handler.
  Inga nya Domain-typer (read-model/query-yta).
- **C2 → CTO:** A2, A6, B1, B2-Occupation, B4. ADR-låst (CC kör): A1, A3, A4, A5, B3, B2-lager.
- **C3 (entydig):** **Testcontainers, EJ InMemory** — `EF.Property` GROUP BY mot STORED shadow-columns; InMemory
  saknar Npgsql-translation (memory `feedback_ef_strongly_typed_vo_contains_translation`). `SuggestByPrefixAsync`
  ren in-memory (unit-testbar); union-handler kräver container för titel-grenen.

## Referenser
CLAUDE.md §2.1/§3.3/§3.6/§5.1/§9.6; ADR 0039 Beslut 1; ADR 0062 Beslut 3+4; ADR 0043 Beslut E + ACL;
ADR 0042 Beslut C; ADR 0067 Beslut 4+5a.
