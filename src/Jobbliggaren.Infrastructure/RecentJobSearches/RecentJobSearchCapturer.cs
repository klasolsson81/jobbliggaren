using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.SavedSearches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.RecentJobSearches;

/// <summary>
/// ADR 0060 — Capturer-implementation. Egen UoW per CTO-dom (anropar
/// SaveChangesAsync direkt — capture är side-effect, ej en del av huvud-querys
/// UoW). UNIQUE(job_seeker_id, filter_hash) bär identitets-invarianten;
/// race-säkerhet via try/catch DbUpdateException + IDbExceptionInspector
/// (paritet med UpsertExternalJobAd, ADR 0032 §5).
///
/// <para>Cap=<see cref="RecentJobSearch.MaxPerSeeker"/> enforce:as här:
/// vid INSERT mot full lista evictas äldsta LastViewedAt-rad i samma
/// transaktion (best-effort — om race-evict tappar invarianten temporärt
/// är det acceptabelt, cap är inte säkerhets-invariant utan affärsregel).</para>
/// </summary>
public sealed partial class RecentJobSearchCapturer(
    IAppDbContext db,
    IDateTimeProvider clock,
    IDbExceptionInspector dbExceptionInspector,
    ILogger<RecentJobSearchCapturer> logger)
    : IRecentJobSearchCapturer
{
    public async Task CaptureAsync(
        Guid userId,
        SearchCriteria criteria,
        int currentCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == userId)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (jobSeekerId == default)
            return;

        var hash = FilterHashCalculator.Compute(criteria);

        var existing = await db.RecentJobSearches
            .Where(r => r.JobSeekerId == jobSeekerId && r.FilterHash == hash)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Bump(currentCount, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Evict äldsta innan INSERT om cap nåtts. Räknar nu så vi inte tappar
        // raden vi just bumpade ovan (men existing-grenen returnerade redan).
        await EnforceCapAsync(jobSeekerId, cancellationToken).ConfigureAwait(false);

        var aggregate = RecentJobSearch.Capture(jobSeekerId, criteria, currentCount, clock.UtcNow);
        db.RecentJobSearches.Add(aggregate);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Race: en parallell capture lyckades före oss. Detacha vår
            // konstruerade aggregat och bumpa den befintliga raden.
            db.Detach(aggregate);
            LogRaceFallback(logger, jobSeekerId.Value);

            var raced = await db.RecentJobSearches
                .Where(r => r.JobSeekerId == jobSeekerId && r.FilterHash == hash)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (raced is not null)
            {
                raced.Bump(currentCount, clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task EnforceCapAsync(JobSeekerId jobSeekerId, CancellationToken cancellationToken)
    {
        var currentRowCount = await db.RecentJobSearches
            .Where(r => r.JobSeekerId == jobSeekerId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        if (currentRowCount < RecentJobSearch.MaxPerSeeker)
            return;

        // Evict äldsta för att lämna plats. Vid race där två parallella
        // captures båda evictar är överskott temporärt cap-1 — acceptabelt
        // (cap är affärsregel, inte säkerhetsinvariant; nästa capture
        // återställer).
        var oldest = await db.RecentJobSearches
            .Where(r => r.JobSeekerId == jobSeekerId)
            .OrderBy(r => r.LastViewedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (oldest is null)
            return;

        db.RecentJobSearches.Remove(oldest);
    }

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Debug,
        Message = "RecentJobSearch capture race-fallback (JobSeekerId={JobSeekerId}) — bumpar racande rad.")]
    private static partial void LogRaceFallback(ILogger logger, Guid jobSeekerId);
}
