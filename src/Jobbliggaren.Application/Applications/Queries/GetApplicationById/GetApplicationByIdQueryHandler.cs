using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Queries.GetApplicationById;

/// <summary>
/// TD-13 (ADR 0049 Mekanik-not 4, CTO Approach A): Application-aggregatet
/// MATERIALISERAS (ej SQL-projektion av krypterade fält) så
/// <c>FieldDecryptionMaterializationInterceptor</c> träffar och dekrypterar
/// CoverLetter/Notes.Content/FollowUps.Note. JobAd förblir en projicerad
/// left-join (ADR 0048 cross-aggregat-del oförändrad) — ej krypterad.
/// <c>FieldEncryptionKeyPrefetchBehavior</c> har värmt ägar-DEK före handlern.
/// </summary>
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

        var applicationId = new Jobbliggaren.Domain.Applications.ApplicationId(query.Id);

        // Materialisera aggregatet (interceptorn dekrypterar krypterade fält).
        // IdentityResolution dedupar Notes/FollowUps utan kartesisk dubblering
        // (AsSplitQuery är relational-only, ej tillgänglig via IAppDbContext).
        var app = await db.Applications
            .AsNoTrackingWithIdentityResolution()
            .Include(a => a.FollowUps)
            .Include(a => a.Notes)
            .FirstOrDefaultAsync(
                a => a.Id == applicationId && a.JobSeekerId == jobSeekerId,
                cancellationToken);

        if (app is null)
        {
            // Failed-access-detection (ADR 0031 / TD-67): skilj "okänt id"
            // från "tillhör annan user". Klient ser identisk 404.
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

        // JobAd-summary: projicerad left-join (ADR 0048, ej krypterat).
        // JobAd:s globala query-filter ärvs → soft-deletad → null → fallback.
        JobAdSummaryDto? jobAd = null;
        if (app.JobAdId is { } jobAdId)
        {
            jobAd = await db.JobAds
                .AsNoTracking()
                .Where(j => j.Id == jobAdId)
                .Select(j => new JobAdSummaryDto(
                    j.Id.Value, j.Title, j.Company.Name, j.Url,
                    j.Source.Value, j.PublishedAt, j.ExpiresAt))
                .FirstOrDefaultAsync(cancellationToken);
        }

        jobAd ??= app.ManualPosting is { } manual
            ? new JobAdSummaryDto(
                null, manual.Title, manual.Company, manual.Url, "Manual",
                (DateTimeOffset?)null, manual.ExpiresAt)
            : null;

        return new ApplicationDetailDto(
            app.Id.Value,
            app.JobSeekerId.Value,
            app.JobAdId?.Value,
            app.ResumeVersionId?.Value,
            app.Status.Name,
            app.CoverLetter,
            app.CreatedAt,
            app.UpdatedAt,
            [.. app.FollowUps.Select(f => new FollowUpDto(
                f.Id.Value, f.Channel.Name, f.ScheduledAt, f.Note,
                f.Outcome.Name, f.OutcomeAt, f.CreatedAt))],
            [.. app.Notes.Select(n => new NoteDto(
                n.Id.Value, n.Content, n.CreatedAt))],
            jobAd);
    }
}
