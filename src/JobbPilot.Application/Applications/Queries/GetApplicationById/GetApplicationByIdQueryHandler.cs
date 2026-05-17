using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Queries.GetApplicationById;

public sealed class GetApplicationByIdQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetApplicationByIdQuery, ApplicationDetailDto?>
{
    public async ValueTask<ApplicationDetailDto?> Handle(
        GetApplicationByIdQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return null;

        var applicationId = new JobbPilot.Domain.Applications.ApplicationId(query.Id);

        // ADR 0048: EN LEFT JOIN job_ads via GroupJoin/DefaultIfEmpty,
        // projektion till ApplicationDetailDto?. JobAd:s query-filter ärvs →
        // soft-deletad JobAd ger j == null → fallback. IgnoreQueryFilters /
        // manuellt DeletedAt-predikat FÖRBJUDET (ADR 0048 c). FollowUps/Notes
        // = Application-ägda collections, subprojiceras (oförändrat innehåll).
        var dto = await db.Applications
            .AsNoTracking()
            .Where(a => a.Id == applicationId && a.JobSeekerId == jobSeekerId)
            .GroupJoin(db.JobAds, a => a.JobAdId, j => j.Id, (a, ja) => new { a, ja })
            .SelectMany(x => x.ja.DefaultIfEmpty(), (x, j) => new
            {
                x.a,
                j,
                // Väg (D): join-härledd FK-Guid, undviker
                // Nullable<JobAdId>.Value i trädet (InMemory-brott).
                JobAdGuid = j != null ? (Guid?)j.Id.Value : null
            })
            .Select(r => new ApplicationDetailDto(
                r.a.Id.Value,
                r.a.JobSeekerId.Value,
                r.JobAdGuid,
                r.a.Status.Name,
                r.a.CoverLetter,
                r.a.CreatedAt,
                r.a.UpdatedAt,
                r.a.FollowUps.Select(f => new FollowUpDto(
                    f.Id.Value,
                    f.Channel.Name,
                    f.ScheduledAt,
                    f.Note,
                    f.Outcome.Name,
                    f.OutcomeAt,
                    f.CreatedAt)).ToList(),
                r.a.Notes.Select(n => new NoteDto(
                    n.Id.Value,
                    n.Content,
                    n.CreatedAt)).ToList(),
                r.j != null
                    ? new JobAdSummaryDto(
                        r.j.Id.Value, r.j.Title, r.j.Company.Name, r.j.Url,
                        r.j.Source.Value, r.j.PublishedAt, r.j.ExpiresAt)
                    : r.a.ManualPosting != null
                        ? new JobAdSummaryDto(
                            null, r.a.ManualPosting.Title, r.a.ManualPosting.Company,
                            r.a.ManualPosting.Url, "Manual",
                            (DateTimeOffset?)null, r.a.ManualPosting.ExpiresAt)
                        : null))
            .FirstOrDefaultAsync(cancellationToken);

        if (dto is null)
        {
            // Failed-access-detection (ADR 0031 / TD-67): skilj "okänt id" från
            // "tillhör annan user" för anomaly-loggning. Klient ser identisk 404.
            var exists = await db.Applications
                .AsNoTracking()
                .AnyAsync(a => a.Id == applicationId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Application", applicationId.Value, currentUser.UserId.Value,
                    "GetApplicationById");
            }
            return null;
        }

        return dto;
    }
}
