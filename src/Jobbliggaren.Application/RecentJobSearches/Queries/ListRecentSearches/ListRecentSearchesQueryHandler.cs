using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.RecentJobSearches.Queries.ListRecentSearches;

/// <summary>
/// ADR 0060 — list-projektion för auto-fångade RecentJobSearches per JobSeeker.
/// Avsiktlig N+1 i CurrentCount-loopen (CTO 2026-05-20 Variant A): cap=20
/// (<c>RecentJobSearch.MaxPerSeeker</c>) håller fan-out hanterbart; varje
/// träffräkning går via <see cref="IJobAdSearchQuery.CountAsync"/> (ADR 0062 —
/// samma filter-SPOT som ListJobAds, q-FTS-accelererad). Fitness function
/// (ADR 0045) övervakar p95 och triggar Hangfire-cache-evolution om budget bryts.
///
/// <para>Label server-härleds (Q → yrkesgrupp med hel-områdes-kollaps /
/// "+N till" → kommun → region → fallback; E2g 2026-06-11) så FE inte
/// behöver konstruera presentation. Defensive fallback "Alla annonser" är dead
/// code så länge SearchCriteria.Empty-invarianten håller, men behålls för
/// robusthet.</para>
///
/// <para><b>Fas C2 (ADR 0067):</b> entiteten bär OccupationGroup + Municipality
/// (occupation-name/Ssyk utgick) — mappas in i filter-SPOT:en (täpper C1:s
/// tomma listor). <b>Fas E2b:</b> C2-shimmets deprecated SsykList/SsykLabels
/// borttagna (FE-zod frikopplad sedan E2a — architect F5-planen utförd).</para>
/// </summary>
public sealed class ListRecentSearchesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ITaxonomyReadModel taxonomy,
    IJobAdSearchQuery search)
    : IQueryHandler<ListRecentSearchesQuery, IReadOnlyList<RecentJobSearchDto>>
{
    public async ValueTask<IReadOnlyList<RecentJobSearchDto>> Handle(
        ListRecentSearchesQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return [];

        var items = await db.RecentJobSearches
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId)
            .OrderByDescending(r => r.LastViewedAt)
            .ToListAsync(cancellationToken);

        // E2g (Klas-direktiv 2026-06-11): hel-områdes-kollaps i labeln kräver
        // fält→grupp-trädet. In-memory-snapshot (ADR 0043 — ingen extern hop);
        // hämtas EN gång per Handle (CTO-krav), och bara när någon rad har >1
        // yrkesgrupp (enda fallet kollapsen kan behövas — q-rader når aldrig
        // grupp-grenen men extra-hämtningen är gratis mot in-memory-cachen).
        IReadOnlyList<TaxonomyOccupationFieldDto>? occupationFields = null;
        if (items.Any(r => r.OccupationGroup.Count > 1))
        {
            occupationFields =
                (await taxonomy.GetTreeAsync(cancellationToken))?.OccupationFields;
        }

        var dtos = new List<RecentJobSearchDto>(items.Count);
        foreach (var r in items)
        {
            var occupationGroupLabels = await taxonomy.ResolveLabelsAsync(
                r.OccupationGroup, cancellationToken);
            var municipalityLabels = await taxonomy.ResolveLabelsAsync(
                r.Municipality, cancellationToken);
            var regionLabels = await taxonomy.ResolveLabelsAsync(
                r.Region, cancellationToken);

            // F6 P5 P4 svans-PR4 (2026-05-24, Klas perf-feedback /oversikt 7-10s):
            // Per-row COUNT är sekventiell (CTO Variant A 2026-05-20 — cap=20
            // N+1). När `IncludeCount=false` skippar vi COUNT.
            //
            // 2026-06-13: ALLA FE-konsumenter (/oversikt, /sokningar, /jobb
            // hero-chip) hämtar i praktiken med IncludeCount=false — den slow
            // N+1-COUNT:en (TD-94, ej löst utan stängd-som-obsolet vid AWS-
            // teardown; empiriskt återöppnad) återskapar annars 8s-timeouten
            // (Npgsql 57014). currentCount/newCount är därför 0 och den synliga
            // per-sökning-träffräknaren är TILLFÄLLIGT borttagen i UI:t (CTO-
            // beslut 2026-06-13: hellre ingen siffra än falsk "(0)"). Återinförs
            // via lat klient-hämtning (B, useFacetCounts-mönstret). Rotorsaken
            // (slow ListJobAds COUNT) fixas i TD-94.
            int currentCount = 0;
            if (query.IncludeCount)
            {
                // ADR 0067 Fas C2: radens egna dimensioner in i filter-SPOT:en
                // (C1:s tomma-listor-läge täppt).
                currentCount = await search.CountAsync(
                    new JobAdFilterCriteria(
                        OccupationGroup: r.OccupationGroup,
                        Municipality: r.Municipality,
                        Region: r.Region,
                        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 i count-filtret.
                        EmploymentType: r.EmploymentType,
                        WorktimeExtent: r.WorktimeExtent,
                        // #311 D6 CONTAINED-seam (CTO-bind 2026-06-30): RecentJobSearch
                        // bär INTE employer i PR-2 (ingen employer_list-kolumn) → en
                        // återbesökt sökning räknar (ännu) inte arbetsgivar-filtrerat.
                        // Sekvenserat till PR-2b (ADR 0087 D6/D7).
                        Employer: [],
                        Q: r.Q),
                    cancellationToken);
            }

            var newCount = Math.Max(0, currentCount - r.LastSeenCount);
            var label = DeriveLabel(
                r.Q, r.OccupationGroup, occupationGroupLabels,
                municipalityLabels, regionLabels, occupationFields);

            dtos.Add(new RecentJobSearchDto(
                r.Id.Value,
                r.Q,
                OccupationGroupList: r.OccupationGroup,
                MunicipalityList: r.Municipality,
                RegionList: r.Region,
                // ADR 0067 Beslut 6 (Fas B2) — råa Klass 2-listor (inga labels, Fas E).
                EmploymentTypeList: r.EmploymentType,
                WorktimeExtentList: r.WorktimeExtent,
                OccupationGroupLabels: occupationGroupLabels,
                MunicipalityLabels: municipalityLabels,
                RegionLabels: regionLabels,
                r.SortBy,
                label,
                currentCount,
                newCount,
                r.LastViewedAt));
        }

        return dtos;
    }

    // Fallback-kedja per architect F6: q → yrkesgrupp → kommun → region →
    // "Alla annonser" (defensive dead code så länge Empty-invarianten håller).
    //
    // E2g (Klas-direktiv 2026-06-11, CTO-bekräftad mekanik): "första labeln"
    // var missvisande vid multi-val ("Drifttekniker, IT" när hela Data/IT
    // valts). Ny regel per dimension: (i) selektion = EXAKT alla grupper i
    // ETT yrkesområde (mängd-likhet mot trädet) → områdets namn; (ii) ett
    // val → namnet; (iii) annars → "{första} +{N−1} till". Blandfall (helt
    // område + extra grupper) → (iii) räknat på grupper. Taxonomi-drift →
    // (i)-matchen faller gracefully till (iii). "{första}" är deterministisk
    // (resolvad label-ordning = persisterad sorterad id-ordning).
    private static string DeriveLabel(
        string? q,
        IReadOnlyList<string> occupationGroupIds,
        IReadOnlyList<TaxonomyLabelDto> occupationGroupLabels,
        IReadOnlyList<TaxonomyLabelDto> municipalityLabels,
        IReadOnlyList<TaxonomyLabelDto> regionLabels,
        IReadOnlyList<TaxonomyOccupationFieldDto>? occupationFields)
    {
        if (!string.IsNullOrWhiteSpace(q))
            return q;
        if (occupationGroupLabels.Count > 0)
        {
            if (occupationGroupIds.Count > 1 && occupationFields is not null)
            {
                var selected = occupationGroupIds.ToHashSet(StringComparer.Ordinal);
                var wholeField = occupationFields.FirstOrDefault(f =>
                    f.OccupationGroups.Count == selected.Count
                    && f.OccupationGroups.All(g => selected.Contains(g.ConceptId)));
                if (wholeField is not null)
                    return wholeField.Label;
            }
            return WithMoreSuffix(occupationGroupLabels);
        }
        if (municipalityLabels.Count > 0)
            return WithMoreSuffix(municipalityLabels);
        if (regionLabels.Count > 0)
            return WithMoreSuffix(regionLabels);
        return "Alla annonser";
    }

    // "{första} +{N−1} till" — +N räknar samma enhet som första namnet anger.
    private static string WithMoreSuffix(IReadOnlyList<TaxonomyLabelDto> labels) =>
        labels.Count == 1
            ? labels[0].Label
            : $"{labels[0].Label} +{labels.Count - 1} till";
}
