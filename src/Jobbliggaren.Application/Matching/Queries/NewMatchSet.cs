using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Queries;

/// <summary>
/// The single definition of "this user's background matches whose ad the product may still present,
/// created after a watermark" (ADR 0080 Beslut 6), shared by the uncapped new-match count
/// (<c>GetMyNewMatchCountQueryHandler</c>) and the notification bell's match source (#1666), so the
/// two cannot run two predicates.
///
/// <para>
/// <b>Lifecycle (#864):</b> the join to <c>JobAds</c> with <c>Status == Active</c> is an ALLOW-list —
/// <c>!= Archived</c> would admit every status added later, and Erased (#842) is an Art. 17
/// tombstone. Query syntax plus <c>j.Status == JobAdStatus.Active</c> is the translation form this
/// repo has proven against Npgsql (the comparison crosses a SmartEnum HasConversion, and a form that
/// dies at runtime is invisible under InMemory); <c>MyMatchesSurfaceTests</c> is the oracle.
/// </para>
///
/// <para>
/// <b>Status-agnostic:</b> no <c>NotificationStatus</c> predicate — match visibility is independent
/// of e-mail delivery, so an e-mail-Failed match still counts.
/// </para>
///
/// <para>
/// Returns the ENTITY, never a shaped row: a constructor projection cannot be composed onto and
/// throws at runtime inside EF's shaper (<see cref="CompanyWatches.Queries.NewFollowedCompanyAdSet"/>
/// carries the measurement). Consumers compose a bound, a count or a column join onto it.
/// </para>
/// </summary>
internal static class NewMatchSet
{
    /// <summary>
    /// The user's matches on Active ads, created after <paramref name="since"/>; a <c>null</c>
    /// watermark (never opened the matches view) means every match.
    /// </summary>
    public static IQueryable<UserJobAdMatch> Matches(IAppDbContext db, Guid userId, DateTimeOffset? since)
    {
        var matches = db.UserJobAdMatches.AsNoTracking().Where(m => m.UserId == userId);
        if (since is { } seen)
            matches = matches.Where(m => m.CreatedAt > seen);

        return from m in matches
               join j in db.JobAds.AsNoTracking() on m.JobAdId equals j.Id
               where j.Status == JobAdStatus.Active
               select m;
    }
}
