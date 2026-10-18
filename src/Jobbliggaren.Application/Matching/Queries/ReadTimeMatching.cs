using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.Matching.Queries;

/// <summary>
/// Read-time at-least-Good membership for a set of ads, shared by the request-scoped surfaces that grade
/// the ads they show or count, so they cannot resolve it two ways.
/// </summary>
internal static class ReadTimeMatching
{
    /// <summary>
    /// Read-time at-least-Good membership of <paramref name="idsToGrade"/>. Api-side, so
    /// <c>BuildFullForSortAsync</c> (ICurrentUser-scoped), NOT the Worker's
    /// <c>BuildFullForUserIdAsync</c>. Branches on assessability BEFORE the call —
    /// <c>FilterToMatchingAsync</c> fail-fasts on an empty-SSYK profile.
    /// </summary>
    public static async Task<ReadTimeMatchResolution> ResolveAsync(
        IMatchProfileBuilder profileBuilder,
        IPerUserJobAdSearchQuery perUserSearch,
        IReadOnlyCollection<JobAdId> idsToGrade,
        CancellationToken cancellationToken)
    {
        if (idsToGrade.Count == 0)
            return new ReadTimeMatchResolution(new HashSet<JobAdId>(), Assessed: true);

        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            return new ReadTimeMatchResolution(idsToGrade.ToHashSet(), Assessed: false);

        var matching = await perUserSearch.FilterToMatchingAsync(profile, idsToGrade, cancellationToken);
        return new ReadTimeMatchResolution(matching, Assessed: true);
    }
}

/// <summary>
/// The read-time grade answer. <c>Assessed == false</c> means the user stated no occupation, so an
/// OnlyMatched filter is INERT and <see cref="Matching"/> admits every candidate. A consumer that RENDERS
/// matching (rather than only counting it) must read <c>Assessed</c> and say "not assessed" instead of
/// claiming every row matches.
/// </summary>
internal sealed record ReadTimeMatchResolution(IReadOnlySet<JobAdId> Matching, bool Assessed);
