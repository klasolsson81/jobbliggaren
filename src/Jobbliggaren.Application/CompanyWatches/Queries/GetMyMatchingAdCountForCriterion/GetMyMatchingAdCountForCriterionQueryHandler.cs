using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetMyMatchingAdCountForCriterion;

/// <summary>
/// #1656 (b) — owner-scoped load (<see cref="CriterionOwnerScopedLoader"/> carries the ADR 0031
/// posture and its cross-user probe), then <see cref="CriterionMatchingAdSet"/>.
///
/// <para>
/// <b>This handler computes nothing itself, and that is the point.</b> The set, the grade and the
/// refusal all live in <see cref="CriterionMatchingAdSet"/>, which the filtered ad browse consumes
/// too — so the number here and the list the user lands on cannot run different predicates. That
/// divergence is the defect class this surface has already shipped twice (#1407, #1471), and Klas's
/// condition for #1656 is that the number links to exactly those ads.
/// </para>
///
/// <para>
/// <b>Counts-only, like its sibling.</b> What crosses this handler is a list of ad ids — opaque
/// Guids over public Platsbanken data — and what leaves it is an <c>int?</c>. No org.nr and no
/// company name is read on this path at all, so the personnummer guard (ADR 0087 D8(c)) has nothing
/// to mask; the register only ever answers through the port, and only with ids.
/// </para>
/// </summary>
public sealed class GetMyMatchingAdCountForCriterionQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    ICompanyWatchBrowseQuery browse,
    IPerUserJobAdSearchQuery perUserSearch,
    IMatchProfileBuilder profileBuilder)
    : IQueryHandler<GetMyMatchingAdCountForCriterionQuery, MyMatchingAdCountDto?>
{
    public async ValueTask<MyMatchingAdCountDto?> Handle(
        GetMyMatchingAdCountForCriterionQuery query, CancellationToken cancellationToken)
    {
        var criterion = await CriterionOwnerScopedLoader.LoadForCurrentUserAsync(
            db, currentUser, failedAccessLogger,
            query.CriterionId, CriterionReadOperation.GetMyMatchingAdCountForCriterion,
            cancellationToken);

        if (criterion is null)
            return null;

        var resolved = await CriterionMatchingAdSet.ResolveAsync(
            profileBuilder, perUserSearch, browse, criterion.Criteria, query.AdMagnitude,
            cancellationToken);

        // The switch is exhaustive over a CLOSED hierarchy, so the discard arm is unreachable rather
        // than a default: a fourth kind cannot be declared outside CriterionMatchingAds. It throws
        // instead of returning a number, because every wrong answer here is a lie about how many
        // jobs match the user.
        return resolved switch
        {
            CriterionMatchingAds.Resolved r => MyMatchingAdCountDto.Counted(r.Matching.Count),
            CriterionMatchingAds.NotAssessed => MyMatchingAdCountDto.NotAssessed,
            CriterionMatchingAds.SetTooLarge => MyMatchingAdCountDto.TooBroadToCount,
            _ => throw new InvalidOperationException(
                $"Okänt CriterionMatchingAds-utfall: {resolved.GetType().Name}."),
        };
    }
}
