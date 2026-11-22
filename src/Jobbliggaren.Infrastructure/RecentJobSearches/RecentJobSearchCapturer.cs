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
/// <para>Cap=<see cref="RecentJobSearch.MaxPerSeeker"/> enforce:as här. Before an INSERT against a full list, the
/// unmarked rows with the oldest LastViewedAt are deleted, the whole surplus and not one row, in a statement that
/// re-checks the quick-link slot (ADR 0188): a row marked as a quick link is never evicted, and the INSERT happens only
/// once every row picked was deleted, so the cap holds whatever the slots are. A row picked for eviction that becomes a
/// quick link in between makes this search go unstored, never a 21st row.</para>
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

        // Make room before the INSERT (ADR 0060 Beslut 5, ADR 0188); without room this search is not stored.
        if (!await EnforceCapAsync(jobSeekerId, cancellationToken).ConfigureAwait(false))
            return;

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

    /// <summary>
    /// Deletes the unmarked rows with the oldest <c>LastViewedAt</c> until the job seeker has fewer than
    /// <see cref="RecentJobSearch.MaxPerSeeker"/>, and says whether it could. The delete re-checks the slot in its own
    /// statement, so a row marked after the read survives and the capture is skipped instead.
    /// </summary>
    private async Task<bool> EnforceCapAsync(JobSeekerId jobSeekerId, CancellationToken cancellationToken)
    {
        var currentRowCount = await db.RecentJobSearches
            .Where(r => r.JobSeekerId == jobSeekerId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var surplus = currentRowCount - RecentJobSearch.MaxPerSeeker + 1;
        if (surplus <= 0)
            return true;

        var oldest = await db.RecentJobSearches
            .Where(r => r.JobSeekerId == jobSeekerId && r.QuickLinkSlot == null)
            .OrderBy(r => r.LastViewedAt)
            .Take(surplus)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (oldest.Count < surplus)
            return false;

        var deleted = await db.RecentJobSearches
            .Where(r => oldest.Contains(r.Id) && r.QuickLinkSlot == null)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted == surplus;
    }

    [LoggerMessage(
        EventId = 6002,
        Level = LogLevel.Debug,
        Message = "RecentJobSearch capture race-fallback (JobSeekerId={JobSeekerId}) — bumpar racande rad.")]
    private static partial void LogRaceFallback(ILogger logger, Guid jobSeekerId);
}
