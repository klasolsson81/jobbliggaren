# dotnet-architect — Platsbanken sök-paritet Fas C1 (design-detalj)

**Datum:** 2026-06-09
**Agent:** dotnet-architect (advisor — design inom CTO:s låsta beslut)
**Scope:** Clean Arch/DDD-detaljering av C1 query/filter-layer + yrke-nivåbyte. CTO-beslut (a Variant C, b 400, c defer B2, d additiv kaskad) låsta; Klas-GO givet.

---

## Fråga 1 — Filter-SPOT-form (Variant C konkret)

**Designdom: lägg till `OccupationGroup` + `Municipality`, BEHÅLL `Ssyk`-fältet men ta bort dess equality-gren i `ApplyCriteria`.** Döp inte om.

Skäl: `JobAdFilterCriteria.Ssyk` matas på tre ställen, två persistens-bundna (C2-låsta):
- `RunSavedSearchQueryHandler:63` → `criteria.Ssyk` (jsonb-persisterad `SearchCriteria.Ssyk`-VO)
- `ListRecentSearchesQueryHandler:68` → `r.Ssyk` (persisterad `RecentJobSearch`-kolumn + `FilterHashCalculator`-kontrakt)
- `ListJobAdsQueryHandler:22` → `query.Ssyk` (runtime query-param)

Om `Ssyk`-fältet tas bort tvingas persistens-källornas Ssyk antingen i tomma intet (tyst datförlust) eller felaktigt in i OccupationGroup-grenen (occupation-name-id mot occupation_group-kolumn → 0 träffar, aktivt brutet). Behåll `Ssyk` som separat namngivet fält → no-op-fönstret blir explicit och självdokumenterande.

```csharp
public sealed record JobAdFilterCriteria(
    IReadOnlyList<string> OccupationGroup,  // C1: primärt yrke-filter (→ occupation_group_concept_id)
    IReadOnlyList<string> Municipality,     // C1: → municipality_concept_id
    IReadOnlyList<string> Region,           // oförändrat → region_concept_id
    IReadOnlyList<string> Ssyk,             // BEVARAS, EJ equality-gren (C2-bunden)
    string? Q);
```

CTO:s "ersätt Ssyk med OccupationGroup" = ersätt vad som *driver yrke-filtret* (equality-grenen tas bort = bokstavligt CTO-beslut). Fält-tuppeln behåller Ssyk-namnet pga C2-bindning. Fält-rename uppskjuts till C2 med VO-expansionen.

**Tre konsumenters mappning (named args — defense-in-depth mot positionell drift med 4 listor):**
```csharp
// ListJobAds: query.OccupationGroup ?? [], query.Municipality ?? [], query.Region ?? [], Ssyk: [], query.Q
// RunSavedSearch: OccupationGroup: [], Municipality: [], criteria.Region, Ssyk: criteria.Ssyk, criteria.Q
// ListRecentSearches: OccupationGroup: [], Municipality: [], r.Region, Ssyk: r.Ssyk, r.Q
```

q-vägen (`synonymExpander.Expand(q)` mot `SsykConceptId`) BEVARAS orörd → gammal sparad sökning med Q satt ger fortf. recall; no-op-fönstret träffar bara rena yrke-utan-fritext-sökningar.

**`ListJobAdsQuery.Ssyk` — behåll som deprecerad no-op i C1, ta bort i C2 (§9.6 fas-fynd).** `ICapturesRecentSearch.Ssyk` (shape-matchad av ListJobAdsQuery) läses i `RecentJobSearchCaptureBehavior`. Borttagning drar in C2:s VO-yta. Behåll no-op; dokumentera i PR-body.

---

## Fråga 2 — Municipality + OccupationGroup-grenar i ApplyCriteria

Sträng-lista `Contains` mot shadow-prop, ingen VO (`feedback_ef_strongly_typed_vo_contains`):
```csharp
if (criteria.OccupationGroup.Count > 0) {
    var groupValues = criteria.OccupationGroup;
    source = source.Where(j => groupValues.Contains(EF.Property<string?>(j, "OccupationGroupConceptId")));
}
if (criteria.Municipality.Count > 0) {
    var municipalityValues = criteria.Municipality;
    source = source.Where(j => municipalityValues.Contains(EF.Property<string?>(j, "MunicipalityConceptId")));
}
```
Båda kolumner STORED + B-tree-indexerade (populerade 34843/33935). Ingen migration i C1. Den explicita Ssyk-equality-grenen tas bort.

---

## Fråga 3 — DTO-kaskad-form (additiv)

```csharp
public sealed record TaxonomyRegionDto(string ConceptId, string Label,
    IReadOnlyList<TaxonomyMunicipalityDto> Municipalities);
public sealed record TaxonomyOccupationFieldDto(string ConceptId, string Label,
    IReadOnlyList<TaxonomyOccupationDto> Occupations,         // BEHÅLLS (beslut d)
    IReadOnlyList<TaxonomyOccupationGroupDto> OccupationGroups);
public sealed record TaxonomyMunicipalityDto(string ConceptId, string Label);      // ny
public sealed record TaxonomyOccupationGroupDto(string ConceptId, string Label);   // ny
```
Egna record-typer (ej återbruk) — samma form, olika betydelse, fri framtida divergens.

`LoadAsync` — två nya `GroupBy(ParentConceptId)` (samma mönster som `occupationsByField`):
```csharp
var municipalitiesByRegion = concepts.Where(c => c.Kind == Municipality && c.ParentConceptId is not null)
    .GroupBy(c => c.ParentConceptId!).ToDictionary(...TaxonomyMunicipalityDto...);
var groupsByField = concepts.Where(c => c.Kind == OccupationGroup && c.ParentConceptId is not null)
    .GroupBy(c => c.ParentConceptId!).ToDictionary(...TaxonomyOccupationGroupDto...);
```
`TryGetValue(..., out var x) ? x : []`-fallback. `labelByConceptId` redan över alla Kinds → kommun/yrkesgrupp-labels auto-resolvbara via `ResolveLabelsAsync`. ETag (hash över tree) ändras automatiskt = korrekt invalidering.

---

## Fråga 4 — SPOT-disciplin (ADR 0039 Beslut 1 / ADR 0062 Beslut 3)

Filer (alla i samma commit — ctor-arity-ändring = kompilator-bruten yta):
| Fil | Ändring |
|---|---|
| `JobAdFilterCriteria.cs` | Ny ctor (4 listor + Q) |
| `ListJobAdsQueryHandler.cs` | Mappning + Ssyk:[] |
| `RunSavedSearchQueryHandler.cs:62-67` | Ssyk: criteria.Ssyk no-op-position |
| `ListRecentSearchesQueryHandler.cs:67-68` | Analogt |
| `JobAdSearchQuery.cs` ApplyCriteria | Ssyk-gren→OccupationGroup + ny Municipality; q-väg orörd |

SPOT intakt: SearchAsync+CountAsync konsumerar samma typ; bytet sker inuti SPOT:en. **Named arguments obligatoriskt** (4 listor i rad = tyst-fel-fälla).

---

## Fråga 5 — Validator + endpoint

Endpoint: nya `string[]? occupationGroup`, `string[]? municipality`, behåll `string[]? ssyk` (deprecerad no-op).
Validator: spegla ConceptIdPattern + MaxConceptIds-cap för OccupationGroup + Municipality (analogt Ssyk/Region).
MaxConceptIds 10→400 i SearchCriteria.cs:39 (single source; validatorn följer med).

**Viktigt fynd → ResolveTaxonomyLabelsQueryValidator (se CTO-multiplikator-dom):** `MaxConceptIdsPerCall = MaxConceptIds × 2` antar 2 dims; C1 har upp till 4 → underdimensionerat. CTO dömde ×4.

---

## Fynd

- **[Viktigt]** `ResolveTaxonomyLabelsQueryValidator.cs:17` — ×2-multiplikator underdimensionerar för 4 dims → CTO dömde ×4 (=1600). DRY-referens till MaxConceptIds bevaras.
- **[Nice-to-have]** `JobAdFilterCriteria` call-sites — named arguments (4 listor positionellt = tyst-fel).
- **[Fas-fynd §9.6]** `ListJobAdsQuery.Ssyk` + `ICapturesRecentSearch.Ssyk` full borttagning = C2 (VO-expansion). Behåll no-op i C1, dokumentera.

---

## Filer som ändras i C1 (komplett)

**Application:** JobAdFilterCriteria, ListJobAdsQuery, ListJobAdsQueryHandler, ListJobAdsQueryValidator, RunSavedSearchQueryHandler, ListRecentSearchesQueryHandler, TaxonomyTreeDto, ResolveTaxonomyLabelsQueryValidator.
**Domain:** SearchCriteria.cs:39 (MaxConceptIds 10→400).
**Infrastructure:** JobAdSearchQuery.cs (ApplyCriteria), TaxonomyReadModel.cs (2 GroupBy).
**Api:** JobAdsEndpoints.cs (query-params).
**Tester:** GetTaxonomyEndpointTests, TaxonomyReadModelIntegrationTests, ListJobAdsFilterTests, ListJobAdsMultiFilterTests, ResolveTaxonomyLabelsQueryValidatorTests, RunSavedSearchQueryHandlerTests, ListRecentSearchesQueryHandlerTests.

**Inte rört i C1:** SearchCriteria-VO-fält (C2), RecentJobSearch-entity + FilterHashCalculator (C2), B2-dims (c), SsykConceptId-kolumn + synonym-q-gren (a — bevaras), TaxonomyConceptKind (redan B1).

## Clean Arch-verifiering: Domain (bara konstant-bump), Application (rena DTOs/SPOT, EF.Property i Infra bakom port), Infrastructure (all Npgsql bakom IJobAdSearchQuery/ITaxonomyReadModel), Api (endpoint→Mediator). OK.

## Referenser
CLAUDE.md §2.1/§2.2/§3.6/§5.1/§9.6; ADR 0039 Beslut 1; ADR 0062 Beslut 3/4; ADR 0042 Beslut B/D/E; ADR 0043 Beslut D/E + amendment; ADR 0067; `feedback_ef_strongly_typed_vo_contains_translation`; `feedback_adr_mechanism_vs_env_phase_triage`.
