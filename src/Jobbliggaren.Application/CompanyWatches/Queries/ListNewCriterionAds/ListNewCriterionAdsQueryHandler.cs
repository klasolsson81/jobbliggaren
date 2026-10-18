using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Notifications;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionIdentity;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListNewCriterionAds;

/// <summary>
/// ADR 0175 D7 — reads the ads one criterion's "N nya" counted, through <see cref="NewCriterionAdSet.NewHits"/>, so
/// the page and the count cannot run two predicates. The criterion is loaded owner-scoped first (404 and the
/// cross-user probe for a foreign id).
///
/// <para>
/// <b>The window is the #1576 rule</b> (<see cref="AcknowledgementWindow"/>): read OLDEST first, at most
/// <see cref="MaxRows"/> + 1 hits, so the acknowledgement leaves everything newer for the next visit; the rows are
/// then shown newest first. One (criterion, ad) is one hit, so no ad appears twice.
/// </para>
///
/// <para>
/// <b>"Matches you" is graded here, in the request, and nowhere else</b> (DPIA §D9 S2 P1): read-time through
/// <see cref="ReadTimeMatching"/>, never persisted and never logged. Lifecycle: Active only, inherited from the set's
/// gate and registered in <c>JobAdLifecycleReadRegistry</c>.
/// </para>
/// </summary>
public sealed class ListNewCriterionAdsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch)
    : IQueryHandler<ListNewCriterionAdsQuery, NewCriterionAdsDto?>
{
    /// <summary>The cap that satisfies AGENTS.md §5 without pagination; parity with the follow rail's page.</summary>
    public const int MaxRows = 100;

    public async ValueTask<NewCriterionAdsDto?> Handle(
        ListNewCriterionAdsQuery query, CancellationToken cancellationToken)
    {
        var criterion = await CriterionOwnerScopedLoader.LoadForCurrentUserAsync(
            db, currentUser, failedAccessLogger, query.CriterionId,
            CriterionReadOperation.ListNewCriterionAds, cancellationToken);
        if (criterion is null)
            return null;

        var identity = new CriterionIdentityDto(
            criterion.Id.Value, criterion.Criteria.SniCodes, criterion.Criteria.MunicipalityCodes, criterion.Label);

        switch (criterion.LastScanOutcome)
        {
            case CompanyWatchCriterionScanOutcome.TooBroad:
                return NewCriterionAdsDto.Refused(identity);
            case CompanyWatchCriterionScanOutcome.Scanned:
                break;
            default:
                return NewCriterionAdsDto.NotScanned(identity);
        }

        var userId = criterion.UserId;
        var criterionId = criterion.Id;

        // OLDEST first and no tiebreak: the read AcknowledgementWindow cuts (its docblock carries why).
        var read = await (
                from h in NewCriterionAdSet.NewHits(db, userId)
                where h.CompanyWatchCriterionId == criterionId
                join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
                orderby h.CreatedAt
                select new Row(
                    h.JobAdId,
                    h.CreatedAt,
                    new JobAdDto(
                        j.Id.Value,
                        j.Title,
                        j.Company.Name,
                        j.Url,
                        j.Source.Value,
                        j.Status.Value,
                        j.PublishedAt,
                        j.ExpiresAt,
                        j.CreatedAt)))
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);

        var cut = AcknowledgementWindow.Apply(read, MaxRows, r => r.HitCreatedAt);
        if (cut.Rows.Count == 0)
            return NewCriterionAdsDto.Nothing(identity);

        var resolution = await ReadTimeMatching.ResolveAsync(
            profileBuilder, perUserSearch, cut.Rows.Select(r => r.JobAdId).ToList(), cancellationToken);

        var rows = cut.Rows
            .OrderByDescending(r => r.HitCreatedAt)
            .ThenBy(r => r.Ad.Id)
            .Select(r => new NewCriterionAdRow(
                r.Ad, resolution.Assessed ? resolution.Matching.Contains(r.JobAdId) : null))
            .ToList();

        return new NewCriterionAdsDto(
            identity, rows, resolution.Assessed, cut.AcknowledgedThrough, cut.Truncated,
            TooBroad: false, NotMaterialised: false);
    }

    /// <summary>One hit joined to its ad. Named because an anonymous type cannot cross the projection.</summary>
    private sealed record Row(JobAdId JobAdId, DateTimeOffset HitCreatedAt, JobAdDto Ad);
}
