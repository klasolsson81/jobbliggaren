using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Applications.Queries.GetPipeline;

public sealed class GetPipelineQueryHandler(
    IAppDbContext db, ICurrentUser currentUser, IDateTimeProvider clock,
    IOptions<ApplicationAttentionOptions> attentionOptions)
    : IQueryHandler<GetPipelineQuery, IReadOnlyList<PipelineGroupDto>>
{
    public async ValueTask<IReadOnlyList<PipelineGroupDto>> Handle(
        GetPipelineQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        // Captured once so EF parameterises the overdue-follow-up EXISTS below.
        var now = clock.UtcNow;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return [];

        // ADR 0048: EN LEFT JOIN job_ads FÖRE materialisering; GroupBy
        // in-memory EFTER (pipeline = kanban-vy, ej DB-aggregering — N+1-fri
        // eftersom all data hämtas i den ENA queryn). IgnoreQueryFilters /
        // hand-rullade soft-delete-predikat FÖRBJUDET (ADR 0048 c).
        //
        // j == null betyder att ansökan saknar ANNONSRAD (manuell eller enbart
        // personligt brev) — ALDRIG att annonsen är borta. JobAd har ingen
        // soft-delete-axel och inget query-filter (#821): Status är hela
        // livscykel-axeln. En annons som inte längre är aktiv bär
        // Status == "Archived" och joinar fortfarande — därav Status på DTO:n.
        var rows = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId)
            .OrderByDescending(a => a.UpdatedAt)
            .Take(500) // TD-8: pipeline är kanban-vy, inte paginerad lista — 500 som skyddsventil
            .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
            .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new
            {
                x.a,
                j,
                // Väg (D): se GetApplicationsQueryHandler — join-härledd
                // FK-Guid, undviker Nullable<JobAdId>.Value i trädet.
                JobAdGuid = j != null ? (Guid?)j.Id.Value : null
            })
            .Select(r => new ApplicationDto(
                r.a.Id.Value,
                r.a.JobSeekerId.Value,
                r.JobAdGuid,
                r.a.Status.Name,
                r.a.CreatedAt,
                r.a.UpdatedAt,
                // #805-3: Status projiceras (value-converter → string, samma idiom
                // som Source). Manuell → null: ingen JobAd-rad ⇒ ingen arkivering
                // ⇒ ingen livs-utsaga (aldrig defaultad till "Active").
                //
                // #892 (CTO R1/R5): raderad annons → identitet ur ansökans AdSnapshot
                // (Status förblir "Erased"); utan snapshot → TOM identitet, aldrig
                // domän-sentinelen "[raderad]" över gränsen (§2.3). Lockstep med
                // GetApplicationsQueryHandler — samma treväga fallback, samma skäl.
                r.j != null
                    ? (r.j.Status == JobAdStatus.Erased
                        ? (r.a.AdSnapshot != null
                            ? new JobAdSummaryDto(
                                r.j.Id.Value, r.a.AdSnapshot.Title, r.a.AdSnapshot.Company,
                                r.a.AdSnapshot.Url, r.a.AdSnapshot.Source,
                                r.a.AdSnapshot.PublishedAt, r.a.AdSnapshot.ExpiresAt,
                                r.j.Status.Value)
                            : new JobAdSummaryDto(
                                r.j.Id.Value, string.Empty, string.Empty, null,
                                r.j.Source.Value, r.j.PublishedAt, r.j.ExpiresAt,
                                r.j.Status.Value))
                        : new JobAdSummaryDto(
                            r.j.Id.Value, r.j.Title, r.j.Company.Name, r.j.Url,
                            r.j.Source.Value, r.j.PublishedAt, r.j.ExpiresAt, r.j.Status.Value))
                    : r.a.ManualPosting != null
                        ? new JobAdSummaryDto(
                            null, r.a.ManualPosting.Title, r.a.ManualPosting.Company,
                            r.a.ManualPosting.Url, "Manual",
                            (DateTimeOffset?)null, r.a.ManualPosting.ExpiresAt, null)
                        : null,
                r.a.AppliedAt,
                // #342 (ADR 0085 §3): attention envelope, projected at the read
                // boundary. LastStatusChangeAt anchors the effective-wait signals;
                // HasOverdueFollowUp is signal 2. The former GhostedThresholdDays
                // projection was dropped in PR 4 — ghost-suggest now keys on the
                // operator option GhostSuggestDays, not the per-aggregate field.
                r.a.LastStatusChangeAt,
                // Correlated EXISTS — no followUps[] hydration (CQRS list ≠ detail,
                // ADR 0048 Alt C rejected). Soft-delete exclusion is carried by the
                // FollowUp global query filter (FollowUpConfiguration.cs:43, ADR 0048 c —
                // one SPOT, no duplicate DeletedAt predicate in the handler), pinned by
                // Handle_ProjectsHasOverdueFollowUp_FalseForSoftDeletedFollowUp.
                // #348 RESOLVED (measured 2026-06-30, EXPLAIN ANALYZE @ 60k apps /
                // 181k follow_ups): the application_id FK index already index-serves
                // this EXISTS (Bitmap Index Scan on ix_follow_ups_application_id, NO
                // seqscan); the 500-row worst case runs ~1.6-2.5 ms warm — ≈2 orders
                // of magnitude under the ADR 0045 p95 budget, which stays observe-only
                // (§2.5). No index added. Pre-selected remedy if a ratchet fires
                // (follow-ups-per-application grows materially / Klas ratchets): a
                // partial (application_id, scheduled_at) WHERE deleted_at IS NULL AND
                // outcome = 'Pending' (Index-Only-Scan, ~2x, 2.3 MB). A composite
                // (application_id, scheduled_at) was measured and rejected (no warm
                // gain, 2x storage). See ADR 0085 §3.
                r.a.FollowUps.Any(f =>
                    f.Outcome == FollowUpOutcome.Pending && f.ScheduledAt < now),
                // ADR 0092 D5: denormalised last-follow-up scalar (lockstep with
                // GetApplications) — drives effectiveWaitDays in the evaluator.
                r.a.LastFollowUpAt))
            .ToListAsync(cancellationToken);

        // #343 (ADR 0085 §3, CTO Option a): stamp the attention signal in-memory over
        // the already-materialised rows. ApplicationAttentionEvaluator.Evaluate is the
        // SSOT and is not SQL-translatable, so it runs after ToListAsync — pure CPU over
        // the bounded (<=500) set: no extra DB round-trip, no query-plan change (the
        // in-memory note folds into the #348 ADR 0045 re-validation thread). Lockstep
        // with GetApplicationsQueryHandler so the shared ApplicationDto never carries a
        // lie on either read path (#342 invariant). Priority order is already encoded —
        // Evaluate returns the single highest-priority signal.
        var options = attentionOptions.Value;
        return rows
            .Select(dto => dto with
            {
                AttentionSignal = ApplicationAttentionEvaluator.Evaluate(dto, options, now),
            })
            .GroupBy(r => r.Status)
            .Select(g => new PipelineGroupDto(g.Key, g.Count(), g.ToList()))
            .ToList();
    }
}
