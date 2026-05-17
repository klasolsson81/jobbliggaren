# test-writer-rapport — saved-search-namn-berikning (TDD RÖD)

**Datum:** 2026-05-17
**Roll:** test-writer (Red-fas — tester FÖRST, impl saknas)
**CTO-beslut:** `docs/reviews/2026-05-17-savedsearch-namn-cto.md` (Approach A)
**Scope:** Endast `tests/**`. Ingen `src/**`-ändring (gränsen hållen).

---

## DTO-form — val (CTO-spec lät test-writer avgöra)

CTO-specen erbjöd två former: `IReadOnlyList<TaxonomyLabelDto> SsykLabels/RegionLabels`
**eller** parallella `IReadOnlyList<string>`-namn-listor.

**Valt: `IReadOnlyList<TaxonomyLabelDto> SsykLabels` + `RegionLabels`.**

Motiv:

1. **Speglar portkontraktet exakt.** `ITaxonomyReadModel.ResolveLabelsAsync`
   returnerar redan `IReadOnlyList<TaxonomyLabelDto>` (concept-id + label
   parat). Handlern kan projicera rakt igenom utan att splittra till
   parallella listor.
2. **Ingen index-misalignment-risk.** Parallella `IReadOnlyList<string>` kräver
   att `Ssyk[i]` ↔ `SsykLabels[i]` hålls i lås — en bräcklig invariant ingen
   typ skyddar. `TaxonomyLabelDto` bär sitt eget concept-id → självbeskrivande,
   FE kan matcha utan positionsantagande.
3. **Speglar kodbas-stilen.** `TaxonomyTreeDto`/`TaxonomyLabelDto` är redan
   etablerad DTO-form i `GetTaxonomyTree`-ytan; konsekvent.
4. **Additivt.** Läggs sist i den positionella ctor:n → ADR 0039/0043-kontrakt
   (de 10 första fälten + ordning) orört. Verifieras av
   `SavedSearchDtoContractTests`.

Avvikelse mot impl löses CC/CTO vid behov — frågar ej.

---

## Skrivna/ändrade testfiler

| Fil | Status | Innehåll |
|---|---|---|
| `tests/JobbPilot.Application.UnitTests/SavedSearches/Queries/ListSavedSearchesQueryHandlerTests.cs` | Utökad (additiv) | 3 befintliga invariant-tester bevarade + 7 nya namn-berikningstester |
| `tests/JobbPilot.Application.UnitTests/SavedSearches/Queries/SavedSearchDtoContractTests.cs` | Ny | 3 kontraktstester (additiv DTO, fält-ordning, label-projektioner) |
| `tests/JobbPilot.Architecture.Tests/TaxonomyAclLayerTests.cs` | Ändrad allowlist | `Only_query_handlers_consume_ITaxonomyReadModel` utökad additivt med `ListSavedSearchesQueryHandler` |

### ListSavedSearchesQueryHandlerTests — nya tester

- `Handle_ShouldPopulateSsykAndRegionLabels_WhenConceptIdsResolve` — happy
  path: Ssyk+Region concept-id → rätt namn i `SsykLabels`/`RegionLabels`;
  råa `Ssyk`/`Region`-fält orörda (ADR 0039-additivitet).
- `Handle_ShouldPropagateFallbackLabel_WhenConceptIdIsUnknown` — okänt id →
  `"Okänd kod (<id>)"` propageras, ej throw (befintlig ResolveLabelsAsync-
  semantik).
- `Handle_ShouldReturnEmptyLabelLists_WhenCriteriaHasNoSsykOrRegion` — tom
  Ssyk/Region → tomma label-listor.
- `Handle_ShouldNotThrow_WhenSavedSearchHasNoConceptIds` — robusthet.
- `Handle_ShouldInvokeTaxonomyPort_ForEachSavedSearchWithConceptIds` —
  porten anropas via batch-signatur (`IReadOnlyList<string>`), ej
  per-element-fan-out.
- `Handle_ShouldKeepJobSeekerScoping_WhenResolvingLabels` — befintlig
  JobSeeker-scoping-invariant (cross-tenant) ej bruten av berikning.

### Befintliga tester — ej regredierade

`Handle_ReturnsOnlyOwnSavedSearches`, `Handle_WhenUserIdIsNull_ReturnsEmpty`,
`Handle_WhenNoJobSeeker_ReturnsEmpty` bevarade ordagrant i logik; endast
ctor-signaturen uppdaterad till 3-arg (porten injicerad) — vilket ÄR det
RÖDA kravet, ej en regression.

---

## RÖD-status (verifierad)

`dotnet build tests/JobbPilot.Application.UnitTests` → **14 Error(s)**,
samtliga exakt de två avsedda missing-impl-signalerna:

- `CS1729` — `ListSavedSearchesQueryHandler` saknar 3-arg-ctor
  (behöver `ITaxonomyReadModel`-injektion).
- `CS1061` — `SavedSearchDto` saknar `SsykLabels`/`RegionLabels`.

Inga test-författar-fel kvar (xUnit1051-analyzern åtgärdad — mock-verifiering
använder `Arg.Any<CancellationToken>()`, etablerat mönster från
`TaxonomyQueryHandlersTests`).

`SavedSearchDtoContractTests` + arch-test `Only_query_handlers_consume_…`
kompilerar men FALLERAR runtime tills impl finns (RÖD enligt plan).

---

## Förväntade grön-kriterier (för CC-impl)

1. `ListSavedSearchesQueryHandler` får 3-arg-ctor:
   `(IAppDbContext db, ICurrentUser currentUser, ITaxonomyReadModel taxonomy)`.
2. Handlern resolverar per sparad sökning `s.Criteria.Ssyk` resp.
   `s.Criteria.Region` via `taxonomy.ResolveLabelsAsync(...)` IN-PROCESS;
   tom lista → tom label-lista (inget portanrop krävs på tom input).
3. `SavedSearchDto` utökas ADDITIVT, sist i ctor:n, med
   `IReadOnlyList<TaxonomyLabelDto> SsykLabels` + `RegionLabels`.
4. VO/jsonb/ADR 0039-kontrakt OFÖRÄNDRAT — endast read-projektion.
5. Arch-test grön = porten injiceras exakt i de tre listade handlarna
   (inga andra konsumenter).
6. Befintliga 3 invariant-tester gröna utan logikändring.

---

## Nästa steg

Tester är RÖDA — production code saknas (per plan). CC implementerar enligt
grön-kriterierna ovan tills Application.UnitTests + Architecture.Tests är
GRÖNA. Test-writer rör ej `src/**`. Conventional commit (test-scope) lokalt,
ingen push.
