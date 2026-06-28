using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Applications.Queries.GetApplications;

public sealed class GetApplicationsQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock,
    IOptions<ApplicationAttentionOptions> attentionOptions)
    : IQueryHandler<GetApplicationsQuery, PagedResult<ApplicationDto>>
{
    public async ValueTask<PagedResult<ApplicationDto>> Handle(
        GetApplicationsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Empty(query);

        // Captured once so EF parameterises the overdue-follow-up EXISTS below.
        var now = clock.UtcNow;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Empty(query);

        var baseQuery = db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId);

        if (query.Status is not null &&
            ApplicationStatus.TryFromName(query.Status, out var status))
        {
            baseQuery = baseQuery.Where(a => a.Status == status);
        }

        // Separat count-query per CLAUDE.md §3.6 — projection-fri count är effektivare
        // än materialisering + Count() och låter EF generera SELECT COUNT(*).
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // ADR 0048: EN LEFT JOIN job_ads via GroupJoin/DefaultIfEmpty FÖRE
        // materialisering. JobAd:s globala query-filter (DeletedAt == null)
        // ärvs automatiskt → soft-deletad JobAd ger j == null → fallback.
        // IgnoreQueryFilters / manuellt DeletedAt-predikat FÖRBJUDET (ADR 0048 c).
        var items = await baseQuery
            .OrderByDescending(a => a.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
            .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new
            {
                x.a,
                j,
                // Väg (D): härled FK-Guid ur joinade JobAd (j) — undviker
                // Nullable<JobAdId>.Value-unwrap i uttrycksträdet (InMemory-
                // brott). Soft-deletad JobAd → j == null → JobAdGuid null
                // (önskat, ADR 0048 — FK ej mot rad användaren ej får se).
                JobAdGuid = j != null ? (Guid?)j.Id.Value : null
            })
            .Select(r => new ApplicationDto(
                r.a.Id.Value,
                r.a.JobSeekerId.Value,
                r.JobAdGuid,
                r.a.Status.Name,
                r.a.CreatedAt,
                r.a.UpdatedAt,
                r.j != null
                    ? new JobAdSummaryDto(
                        r.j.Id.Value, r.j.Title, r.j.Company.Name, r.j.Url,
                        r.j.Source.Value, r.j.PublishedAt, r.j.ExpiresAt)
                    : r.a.ManualPosting != null
                        ? new JobAdSummaryDto(
                            null, r.a.ManualPosting.Title, r.a.ManualPosting.Company,
                            r.a.ManualPosting.Url, "Manual",
                            (DateTimeOffset?)null, r.a.ManualPosting.ExpiresAt)
                        : null,
                r.a.AppliedAt,
                // #342 (ADR 0085 §3): attention envelope projected atomically in
                // BOTH read handlers (slice-1 AppliedAt precedent) so the shared
                // ApplicationDto never carries a lie on either read path. Soft-delete
                // exclusion via the FollowUp global query filter (ADR 0048 c — one SPOT).
                // Index shape + ADR 0045 re-validation tracked in #348.
                r.a.LastStatusChangeAt,
                r.a.FollowUps.Any(f =>
                    f.Outcome == FollowUpOutcome.Pending && f.ScheduledAt < now),
                r.a.GhostedThresholdDays))
            .ToListAsync(cancellationToken);

        // #343 (ADR 0085 §3, CTO Option a): stamp the attention signal in-memory over
        // the materialised page (Evaluate = SSOT, not SQL-translatable). Lockstep with
        // GetPipelineQueryHandler so the shared ApplicationDto is truthful on both read
        // paths (#342 invariant); pure CPU over the bounded page, no extra DB cost.
        var options = attentionOptions.Value;
        var withAttention = items
            .Select(dto => dto with
            {
                AttentionSignal = ApplicationAttentionEvaluator.Evaluate(dto, options, now),
            })
            .ToList();
        return new PagedResult<ApplicationDto>(withAttention, totalCount, query.Page, query.PageSize);
    }

    private static PagedResult<ApplicationDto> Empty(GetApplicationsQuery query) =>
        new(Array.Empty<ApplicationDto>(), 0, query.Page, query.PageSize);
}
