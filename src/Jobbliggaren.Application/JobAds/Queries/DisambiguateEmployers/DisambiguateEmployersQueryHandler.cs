using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.DisambiguateEmployers;

/// <summary>
/// ADR 0087 D6/D8(c) (#311 PR-2b C2) — runs the employer-disambiguation projection
/// (<see cref="IEmployerDisambiguationQuery"/>, Infrastructure) and applies the sole-prop
/// personnummer guard at the surfacing boundary. The Infra projection returns the RAW org.nr grouped
/// by legal entity; this handler routes each through the Domain VO's canonical shape detector
/// (<see cref="OrganizationNumber.IsPersonnummerShaped"/>) and masks a personnummer-shaped value to
/// null + flag BEFORE it becomes a wire DTO — mirrors <c>ListCompanyWatchesQueryHandler</c> (DRY: one
/// source for the shape heuristic, pinned by <c>OrganizationNumberSurfacingGuardTests</c>). No AI/LLM.
/// </summary>
public sealed class DisambiguateEmployersQueryHandler(IEmployerDisambiguationQuery disambiguation)
    : IQueryHandler<DisambiguateEmployersQuery, IReadOnlyList<EmployerDisambiguationDto>>
{
    public async ValueTask<IReadOnlyList<EmployerDisambiguationDto>> Handle(
        DisambiguateEmployersQuery query, CancellationToken cancellationToken)
    {
        var groups = await disambiguation.SearchAsync(
            query.Query.Trim(), DisambiguateEmployersQuery.MaxResults, cancellationToken);

        var results = new List<EmployerDisambiguationDto>(groups.Count);
        foreach (var g in groups)
        {
            // Personnummer guard (ADR 0087 D8(c), CLAUDE.md §5 highest-priority): a sole-prop
            // (enskild firma) org.nr can equal the owner's personnummer — never surface it un-flagged.
            // Reuse the Domain VO's canonical detector (DRY with CompanyWatch masking + the guard
            // fitness). FromTrusted: the org.nr came from the validated STORED column, not user input.
            var isProtected = OrganizationNumber.FromTrusted(g.OrganizationNumber).IsPersonnummerShaped();
            results.Add(new EmployerDisambiguationDto(
                OrganizationNumber: isProtected ? null : g.OrganizationNumber,
                IsProtectedIdentity: isProtected,
                CompanyName: g.CompanyName,
                AdCount: g.AdCount));
        }

        return results;
    }
}
