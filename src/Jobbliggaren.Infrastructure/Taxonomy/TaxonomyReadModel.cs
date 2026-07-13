using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Infrastructure.Taxonomy;

/// <summary>
/// ADR 0043 (MAP-2/MAP-3) — <see cref="ITaxonomyReadModel"/>-implementation.
/// Singleton med lat in-memory-cache (<see cref="Lazy{T}"/> av
/// <see cref="Task{TResult}"/>, ExecutionAndPublication): snapshot-tabellen
/// läses EN gång per process via en kortlivad scope; invalideras vid
/// app-restart efter deploy (Variant A — samma livscykel som seedern).
/// Statiskt + identiskt för alla användare → ingen per-request-DB-träff,
/// ingen eviction-policy (ej IMemoryCache — bounded oföränderlig
/// referensdata; undviker nytt paketberoende). ACL lever UTANFÖR
/// sök-/filter-vägen (ADR 0043 Beslut E).
/// </summary>
internal sealed class TaxonomyReadModel(IServiceScopeFactory scopeFactory)
    : ITaxonomyReadModel
{
    private sealed record CacheState(
        TaxonomyTreeDto Tree,
        IReadOnlyDictionary<string, string> LabelByConceptId,
        IReadOnlyList<TaxonomySuggestionDto> Suggestable,
        IReadOnlyDictionary<string, IReadOnlyList<string>> RelatedBySource,
        // #477 Low 1 — kommun→län-containment: kommun-concept-id → förälder-län-concept-id
        // (1:1 via ParentConceptId). municipalitiesByRegion läst baklänges; cachas en gång.
        IReadOnlyDictionary<string, string> RegionByMunicipality,
        // Fas 4b 8b.4a — yrkesgrupp→yrkesområde-containment: ssyk-4-yrkesgrupp-concept-id →
        // förälder-yrkesområde-concept-id (1:1 via ParentConceptId). groupsByField läst
        // BAKLÄNGES; exakt samma form som RegionByMunicipality ovan. Nyckeln branschgrupp-
        // assetet slår upp på (ADR 0107).
        IReadOnlyDictionary<string, string> FieldByOccupationGroup);

    // Cachen fylls en gång och delas av alla läsare. Medvetet INTE
    // Lazy<Task> (security-auditor 2026-05-17 Minor): en faulted Lazy<Task>
    // cachar felet permanent → picker-endpointen vore trasig till
    // process-restart även om DB återhämtar sig. Här cachas endast en
    // *lyckad* laddning; ett fault lämnar _cached null så nästa anrop
    // försöker igen. Semaphore serialiserar samtidiga första-laddningar.
    private Task<CacheState>? _cached;

    public async ValueTask<TaxonomyTreeDto> GetTreeAsync(
        CancellationToken cancellationToken)
        => (await GetStateAsync(cancellationToken)).Tree;

    public async ValueTask<IReadOnlyList<TaxonomyLabelDto>> ResolveLabelsAsync(
        IReadOnlyList<string> conceptIds, CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(cancellationToken);
        var result = new List<TaxonomyLabelDto>(conceptIds.Count);
        foreach (var id in conceptIds)
        {
            var label = state.LabelByConceptId.TryGetValue(id, out var l)
                ? l
                : TaxonomyLabels.Unknown(id);   // graceful degradation, aldrig throw
            result.Add(new TaxonomyLabelDto(id, label));
        }
        return result;
    }

    // ADR 0067 Beslut 5a (Fas D1) — in-memory prefix-scan av snapshot-labels.
    public async ValueTask<IReadOnlyList<TaxonomySuggestionDto>> SuggestByPrefixAsync(
        string prefix, int limit, CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(cancellationToken);

        // Ren in-memory-scan av den redan cachade snapshoten (ingen DB-/extern-
        // hop per tangenttryck — ADR 0043). OrdinalIgnoreCase: konsekvent med
        // snapshotens Ordinal-sortering och täcker åäö-case. Deterministisk
        // ordning (Kind enum → Label) gör union-handlern + testen stabila.
        return state.Suggestable
            .Where(s => s.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Kind)
            .ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    // ADR 0084 — breddning: exakt ssyk-4-mängd → relaterad ssyk-4-mängd
    // (substitutability). Ren dictionary-union mot den cachade snapshoten;
    // resultatet EXKLUDERAR den exakt angivna mängden (exakt-vs-relaterad
    // disjunkt — scorern/SQL splittar dem i PR-2+). Graceful: okänd käll-grupp
    // bidrar inget, tom input → tom output, aldrig null/throw. Deterministisk
    // Ordinal-ordning. v1: endast substitutes-riktningen, any-member-rollup.
    public async ValueTask<IReadOnlyList<string>> GetRelatedOccupationGroupsAsync(
        IReadOnlyList<string> ssyk4ConceptIds, CancellationToken cancellationToken)
    {
        if (ssyk4ConceptIds.Count == 0)
            return [];

        var state = await GetStateAsync(cancellationToken);
        var exact = ssyk4ConceptIds.ToHashSet(StringComparer.Ordinal);
        var related = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in exact)
        {
            if (!state.RelatedBySource.TryGetValue(id, out var targets))
                continue;   // okänd/relations-lös käll-grupp → bidrar inget
            foreach (var target in targets)
            {
                if (!exact.Contains(target))   // håll exakt-vs-relaterad disjunkt
                    related.Add(target);
            }
        }

        return related
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    // #477 Low 1 — kommun→län-containment: kommun-mängd → förälder-län-mängd. Ren
    // dictionary-lookup mot den cachade RegionByMunicipality (kommun→förälder-län via
    // ParentConceptId; ingen per-request-DB-träff, ADR 0043 §1.4). Dedupliserar (flera
    // kommuner i samma län → ETT län) och exkluderar inget. Graceful: okänd/föräldralös
    // kommun bidrar inget, tom input → tom output, aldrig null/throw (paritet
    // GetRelatedOccupationGroupsAsync). Deterministisk Ordinal-ordning.
    public async ValueTask<IReadOnlyList<string>> GetContainingRegionsAsync(
        IReadOnlyList<string> municipalityConceptIds, CancellationToken cancellationToken)
    {
        if (municipalityConceptIds.Count == 0)
            return [];

        var state = await GetStateAsync(cancellationToken);
        var regions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var municipality in municipalityConceptIds)
        {
            if (state.RegionByMunicipality.TryGetValue(municipality, out var region))
                regions.Add(region);   // okänd/föräldralös kommun → bidrar inget
        }

        return regions
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    // Fas 4b 8b.4a — yrkesgrupp→yrkesområde-containment: ssyk-4-grupp-mängd → förälder-område-
    // mängd. Exakt spegling av GetContainingRegionsAsync ovan (ren dictionary-lookup mot cachen,
    // ingen per-request-DB-träff). Dedupliserar (flera grupper i samma område → ETT område).
    // Graceful: okänd/föräldralös grupp bidrar inget, tom input → tom output, aldrig null/throw.
    // Deterministisk Ordinal-ordning. Att flera områden KAN returneras är avsiktligt — läs-slicen
    // (ADR 0107) vägrar gissa branschgrupp när användarens yrkesval spänner två.
    public async ValueTask<IReadOnlyList<string>> GetContainingOccupationFieldsAsync(
        IReadOnlyList<string> occupationGroupConceptIds, CancellationToken cancellationToken)
    {
        if (occupationGroupConceptIds.Count == 0)
            return [];

        var state = await GetStateAsync(cancellationToken);
        var fields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in occupationGroupConceptIds)
        {
            if (state.FieldByOccupationGroup.TryGetValue(group, out var field))
                fields.Add(field);   // okänd/föräldralös yrkesgrupp → bidrar inget
        }

        return fields
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private async ValueTask<CacheState> GetStateAsync(CancellationToken ct)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is { IsCompletedSuccessfully: true })
            return cached.Result;

        // Awaita FÖRE publicering: vid fault kastas här och _cached förblir
        // opublicerad → nästa anrop retry:ar (ingen permanent fail-cache,
        // security-auditor 2026-05-17 Minor). Lås-fritt: vid sällsynt
        // samtidig cold-start kan LoadAsync köra 2 ggr (varje egen scope,
        // idempotent ~2 300-raders läsning) — benignt, sista write vinner.
        // Ingen SemaphoreSlim (undviker disposable-fält/CA1001 på singleton).
        var task = LoadAsync(scopeFactory);
        var state = await task;
        Volatile.Write(ref _cached, task);
        return state;
    }

    private static async Task<CacheState> LoadAsync(IServiceScopeFactory factory)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var concepts = await db.Set<TaxonomyConcept>()
            .AsNoTracking()
            .ToListAsync();

        // ADR 0067 Beslut 1 + ADR 0043-amendment 2026-06-08 (Fas C1) — kommun
        // som barn under län (1:1 via ParentConceptId). Samma GroupBy-mönster
        // som occupationsByField nedan.
        var municipalitiesByRegion = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.Municipality
                        && c.ParentConceptId is not null)
            .GroupBy(c => c.ParentConceptId!)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(c => c.Label, StringComparer.Ordinal)
                      .Select(c => new TaxonomyMunicipalityDto(c.ConceptId, c.Label))
                      .ToList());

        // #477 Low 1 — samma kommun-barn-under-län-relation läst BAKLÄNGES: kommun-
        // concept-id → förälder-län-concept-id (1:1). Driver GetContainingRegionsAsync
        // (kommun→län-containment) utan ny DB-träff. ConceptId är PK på taxonomy_concepts
        // → varje kommun är unik → ingen tie-break behövs (paritet municipalitiesByRegion).
        var regionByMunicipality = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.Municipality
                        && c.ParentConceptId is not null)
            .ToDictionary(c => c.ConceptId, c => c.ParentConceptId!, StringComparer.Ordinal);

        var regions = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.Region)
            .OrderBy(c => c.Label, StringComparer.Ordinal)
            .Select(c => new TaxonomyRegionDto(
                c.ConceptId,
                c.Label,
                municipalitiesByRegion.TryGetValue(c.ConceptId, out var muni)
                    ? muni
                    : []))
            .ToList();

        var occupationsByField = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.Occupation
                        && c.ParentConceptId is not null)
            .GroupBy(c => c.ParentConceptId!)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(c => c.Label, StringComparer.Ordinal)
                      .Select(c => new TaxonomyOccupationDto(c.ConceptId, c.Label))
                      .ToList());

        // ADR 0067 Beslut 1 (Fas C1) — yrkesgrupp (ssyk-level-4) som barn under
        // yrkesområde (1:1). Primärt yrke-filter för Platsbanken-paritet.
        var groupsByField = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.OccupationGroup
                        && c.ParentConceptId is not null)
            .GroupBy(c => c.ParentConceptId!)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(c => c.Label, StringComparer.Ordinal)
                      .Select(c => new TaxonomyOccupationGroupDto(c.ConceptId, c.Label))
                      .ToList());

        // Fas 4b 8b.4a — samma yrkesgrupp-barn-under-yrkesområde-relation läst BAKLÄNGES:
        // yrkesgrupp-concept-id → förälder-yrkesområde-concept-id (1:1). Driver
        // GetContainingOccupationFieldsAsync utan ny DB-träff. ConceptId är PK på
        // taxonomy_concepts → varje yrkesgrupp är unik → ingen tie-break behövs (paritet
        // regionByMunicipality).
        var fieldByOccupationGroup = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.OccupationGroup
                        && c.ParentConceptId is not null)
            .ToDictionary(c => c.ConceptId, c => c.ParentConceptId!, StringComparer.Ordinal);

        var occupationFields = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.OccupationField)
            .OrderBy(c => c.Label, StringComparer.Ordinal)
            .Select(c => new TaxonomyOccupationFieldDto(
                c.ConceptId,
                c.Label,
                occupationsByField.TryGetValue(c.ConceptId, out var occ)
                    ? occ
                    : [],
                groupsByField.TryGetValue(c.ConceptId, out var grp)
                    ? grp
                    : []))
            .ToList();

        // ADR 0043-amendment 2026-06-13 — Klass 2: platta, föräldralösa
        // dimensioner (anställningsform + omfattning). Sorteras på Label Ordinal
        // som övriga dimensioner (konsekvent läs-modell); Platsbanken-paritets-
        // ordning/-kurering är FE-presentation (PR-2).
        var employmentTypes = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.EmploymentType)
            .OrderBy(c => c.Label, StringComparer.Ordinal)
            .Select(c => new TaxonomyOptionDto(c.ConceptId, c.Label))
            .ToList();

        var worktimeExtents = concepts
            .Where(c => c.Kind == TaxonomyConceptKind.WorktimeExtent)
            .OrderBy(c => c.Label, StringComparer.Ordinal)
            .Select(c => new TaxonomyOptionDto(c.ConceptId, c.Label))
            .ToList();

        // Kind-agnostisk reverse-lookup → Klass 2-concept-ids resolveras
        // automatiskt (toolbar-chips + recent-/saved-search-labels) utan
        // resolver-ändring (CTO BESLUT 1).
        var labelByConceptId = BuildLabelByConceptId(concepts);

        // ADR 0067 Beslut 5a — förberäknade typeahead-kandidater. Endast
        // filtrerbara kinds (Län/Kommun/Yrkesområde/Yrkesgrupp); occupation-name
        // utesluts (saknar filter-dimension, VAL 4). Kind översätts till den
        // publika SuggestionKind (ACL — TaxonomyConceptKind är internal).
        var suggestable = concepts
            .Where(c => c.Kind is TaxonomyConceptKind.Region
                            or TaxonomyConceptKind.Municipality
                            or TaxonomyConceptKind.OccupationField
                            or TaxonomyConceptKind.OccupationGroup)
            .Select(c => new TaxonomySuggestionDto(MapKind(c.Kind), c.ConceptId, c.Label))
            .ToList();

        // ADR 0084 — relaterade ssyk-4-grupper (substitutability, rollat upp till
        // ssyk-4 off-repo i generatorn). HELA taxonomy_relations läses en gång in
        // i cachen (paritet taxonomy_concepts ovan) → GetRelatedOccupationGroupsAsync
        // blir en ren dictionary-lookup utan per-request-DB-träff (ADR 0043 §1.4).
        var relations = await db.Set<TaxonomyRelation>()
            .AsNoTracking()
            .ToListAsync();

        var relatedBySource = relations
            .GroupBy(r => r.SourceConceptId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g
                    .Select(r => r.RelatedConceptId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        return new CacheState(
            new TaxonomyTreeDto(regions, occupationFields, employmentTypes, worktimeExtents),
            labelByConceptId,
            suggestable,
            relatedBySource,
            regionByMunicipality,
            fieldByOccupationGroup);
    }

    // #268 audit / #471 (parity with OccupationCodeDeriver's no-silent-First tie-break):
    // reverse-lookup the display label per concept-id. GroupBy(ConceptId) then the
    // Ordinal-MINIMUM label, never an enumeration-order-dependent First() -- this
    // read-model's whole contract is reproducibility, and the sibling
    // OccupationCodeDeriver.BuildAsync guards the identical shape the same way. Today the
    // primary key on taxonomy_concepts.ConceptId makes duplicate concept-ids unreachable
    // through the DB, so every group is a singleton and the tie-break never fires in
    // production; this is a DEFENSIVE determinism pin for parity (and any future non-DB
    // caller or schema change). internal static so the tie-break is unit-testable without
    // a DB (InternalsVisibleTo Api.IntegrationTests), mirroring the deriver's test seam.
    internal static Dictionary<string, string> BuildLabelByConceptId(
        IReadOnlyList<TaxonomyConcept> concepts) =>
        concepts
            .GroupBy(c => c.ConceptId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.Label, StringComparer.Ordinal).First().Label,
                StringComparer.Ordinal);

    // ACL-översättning Infrastructure-intern TaxonomyConceptKind → publik
    // SuggestionKind. Endast suggest-bara kinds mappas (Occupation når aldrig
    // hit — filtreras bort i suggestable-bygget ovan; throw = fail-fast om
    // filtret och switchen divergerar).
    private static SuggestionKind MapKind(TaxonomyConceptKind kind) => kind switch
    {
        TaxonomyConceptKind.Region => SuggestionKind.Region,
        TaxonomyConceptKind.Municipality => SuggestionKind.Municipality,
        TaxonomyConceptKind.OccupationField => SuggestionKind.OccupationField,
        TaxonomyConceptKind.OccupationGroup => SuggestionKind.OccupationGroup,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "Non-suggestable TaxonomyConceptKind reached MapKind."),
    };
}
