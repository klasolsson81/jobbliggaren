using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobbPilot.Infrastructure.Taxonomy;

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
        IReadOnlyDictionary<string, string> LabelByConceptId);

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
                : $"Okänd kod ({id})";   // graceful degradation, aldrig throw
            result.Add(new TaxonomyLabelDto(id, label));
        }
        return result;
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

        var labelByConceptId = concepts
            .GroupBy(c => c.ConceptId)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);

        return new CacheState(
            new TaxonomyTreeDto(regions, occupationFields),
            labelByConceptId);
    }
}
