using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// F4-14 (ADR 0076 Decision 4/5) — <see cref="IMatchSortedJobAdSearchQuery"/>:
/// global "Sortera efter matchning". SEPARAT från <see cref="JobAdSearchQuery"/>
/// (Decision 5 — den delade <see cref="IJobAdSearchQuery"/> förblir match-ren);
/// återanvänder dock EXAKT samma filter-SPOT (<see cref="JobAdSearchComposition.ApplyFilter"/>)
/// + items-projektion (<see cref="JobAdSearchComposition.ToDto"/>) så match-sorten
/// aldrig träffar en annan annons-mängd än default-sorten.
/// <para>
/// <b>Sort-nyckeln (grad-ranken) lever ENBART i <c>ORDER BY</c></b> (Goodhart,
/// Decision 4): den projiceras aldrig in i <see cref="JobAdDto"/>, persisteras
/// aldrig. Ranken är en kompilerad spegel av <c>MatchGradeCalculator</c>
/// (ordnings-SSOT) + <c>MatchScorer.ScoreMembership</c>:
/// <list type="bullet">
/// <item>0 = otaggad (SSYK ej Match) → sorteras sist;</item>
/// <item>1 = Basic (SSYK Match, men en angiven region/anställningsform
/// motsäger — <c>NoMatch</c>, golvar);</item>
/// <item>1 + antal bekräftade (region/anställningsform <c>Match</c>) = 1/2/3
/// (Basic/Good/Strong). En <c>NotAssessed</c>-dimension (tom preferens ELLER
/// NULL-shadow) varken bekräftar eller golvar.</item>
/// </list>
/// Tie-break: <c>publishedAt</c> fallande, sedan <c>Id</c> (determinism).
/// Ett Testcontainers-orakel pinnar SQL-rank ≡ <c>MatchGradeCalculator</c> över
/// hela verdict-tuple-rymden (InMemory döljer translationen — samma
/// <c>ef_strongly_typed_vo_contains</c>-lärdom; <c>= ANY</c>-translationen av
/// <c>list.Contains(EF.Property)</c> är samma som körs i ApplyFilter i prod).
/// </para>
/// </summary>
internal sealed class MatchSortedJobAdSearchQuery(
    AppDbContext db,
    IOccupationSynonymExpander synonymExpander,
    IJobAdSearchQuery searchQuery) : IMatchSortedJobAdSearchQuery
{
    // STORED shadow-kolumner (EF.Property-nycklar) — parity MatchScorer; kolumn-
    // namnen är en Infrastructure-hemlighet som aldrig läcker till Application.
    private const string OccupationGroupColumn = "OccupationGroupConceptId";
    private const string RegionColumn = "RegionConceptId";
    private const string EmploymentTypeColumn = "EmploymentTypeConceptId";

    public async ValueTask<PagedResult<JobAdDto>> SearchByMatchAsync(
        JobAdFilterCriteria filter,
        CandidateMatchProfile profile,
        int page,
        int pageSize,
        DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(profile);

        // Count är sort-oberoende → återanvänd den rena port-counten (TD-94
        // bitmap-plan ingår). Ingen duplicerad count-väg (DRY, ADR 0039 Beslut 1).
        var totalCount = await searchQuery.CountAsync(filter, cancellationToken);

        // Profil-listorna fångas lokalt → EF binder dem som parametrar (= ANY).
        // SSYK är icke-tom (handlerns gate). regions/employment kan vara tomma
        // (NotAssessed — varken bekräftar eller golvar, MatchScorer.ScoreMembership).
        var ssyk = profile.SsykGroupConceptIds;
        var regions = profile.PreferredRegionConceptIds;
        var employment = profile.PreferredEmploymentTypeConceptIds;
        var regionsStated = regions.Count > 0;
        var employmentStated = employment.Count > 0;

        var baseQuery = JobAdSearchComposition.ApplyFilter(
            db.JobAds.AsNoTracking(), filter, synonymExpander);

        var items = await baseQuery
            // Grad-rank fallande (3=Strong … 0=otaggad sist). NotAssessed≠NoMatch:
            // "motsäger" kräver en ANGIVEN preferens (regionsStated) OCH ett
            // icke-NULL shadow-värde som INTE finns i preferens-mängden. Ett tomt
            // preferens-set ger list.Contains == false (= NotAssessed → bidrar 0,
            // golvar ej). Speglar MatchGradeCalculator exakt (oracle-pinnad).
            .OrderByDescending(j =>
                !ssyk.Contains(EF.Property<string?>(j, OccupationGroupColumn))
                    ? 0
                    : ((regionsStated
                            && EF.Property<string?>(j, RegionColumn) != null
                            && !regions.Contains(EF.Property<string?>(j, RegionColumn)))
                        || (employmentStated
                            && EF.Property<string?>(j, EmploymentTypeColumn) != null
                            && !employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn))))
                        ? 1
                        : 1
                            + (regions.Contains(EF.Property<string?>(j, RegionColumn)) ? 1 : 0)
                            + (employment.Contains(EF.Property<string?>(j, EmploymentTypeColumn)) ? 1 : 0))
            .ThenByDescending(j => j.PublishedAt)
            .ThenBy(j => j.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(JobAdSearchComposition.ToDto(since))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, totalCount, page, pageSize);
    }
}
