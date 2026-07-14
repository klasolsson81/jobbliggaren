using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Queries.GetMyNewMatchCount;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — counts the authenticated user's background matches created since their
/// last-seen watermark. Owner-scoped (reads only the current user's matches + watermark). No
/// authenticated user / no JobSeeker → honest 0. The soft-delete query filter on
/// <c>UserJobAdMatch</c> excludes erased rows automatically. NO AI/LLM.
/// <para>
/// Deliberately status-AGNOSTIC: counts a match regardless of its <c>NotificationStatus</c>
/// (Pending/Queued/Sent/Failed) — match visibility is independent of notification delivery
/// (TD-114). Do NOT add a status filter: a real (e.g. email-Failed) match must still count.
/// </para>
/// <para>
/// <b>Lifecycle-gated (#864):</b> counts only matches whose ad is <c>Active</c> — the same predicate
/// <see cref="GetMyMatches.GetMyMatchesQueryHandler"/> carries. Before #864 this query joined
/// NOTHING, so the badge counted matches for ARCHIVED ads. The two surfaces MUST share this
/// predicate: a gated list under an un-gated badge renders "3 nya matchningar" over zero rows.
/// </para>
/// <para>
/// Deliberately UNCAPPED contract (#273): this is the TRUE total of new matches — it MAY exceed
/// the 50-row cap of <see cref="GetMyMatches.GetMyMatchesQueryHandler"/>. The lifecycle gate does
/// NOT clamp that (the cap divergence is bounded and its remainder is reachable via the /jobb
/// grade-filter; a lifecycle divergence is reachable from no surface). The two surfaces answer
/// different questions: this count is the new-match cardinality (the Översikt "Nya matchningar"-
/// badge); <c>/matchningar</c> is a bounded recent-VIEW. The remainder beyond 50 is reachable via
/// the <c>/jobb</c> grade-filter. Do NOT clamp this to the list cap — the count would then
/// understate a true value rendered as a bare integer (honest-data ethos, CLAUDE.md §5) and would
/// silently amend ADR 0080 PR-5 / Beslut 6, which specify this query verbatim as
/// <c>COUNT WHERE CreatedAt &gt; last_seen_matches_at</c>. Pinned by the divergence oracle
/// (MyMatchesSurfaceTests).
/// </para>
/// </summary>
public sealed class GetMyNewMatchCountQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetMyNewMatchCountQuery, MyNewMatchCountDto>
{
    public async ValueTask<MyNewMatchCountDto> Handle(
        GetMyNewMatchCountQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return MyNewMatchCountDto.Zero;

        // The last-seen watermark lives on the JobSeeker (a first-class column, ADR 0080 Beslut 6).
        // null = never opened the matches view → every match is "new".
        var lastSeen = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.LastSeenMatchesAt)
            .FirstOrDefaultAsync(cancellationToken);

        var matches = db.UserJobAdMatches.Where(m => m.UserId == userId);
        if (lastSeen is { } seen)
            matches = matches.Where(m => m.CreatedAt > seen);

        // LIFECYCLE (#864) — count only matches whose ad the product may still present. Before this,
        // the badge counted rows and joined NOTHING, so it counted matches for ARCHIVED ads. That is
        // the same defect as the list (GetMyMatchesQueryHandler) but sharper: this number is rendered
        // as a bare integer on Översikten, and gating the list WITHOUT gating the badge would produce
        // "3 nya matchningar" over a view that renders zero rows — a count that promises more than its
        // set can deliver, manufactured by the fix for #864 rather than by the bug.
        //
        // This does NOT clamp the #273 contract. The count may still legitimately EXCEED the list's
        // 50-row cap: that divergence is bounded, documented, and the remainder is reachable via the
        // /jobb grade-filter. A lifecycle divergence is neither — those ads are reachable from no
        // surface at all. Only the second kind is removed.
        // Query syntax + `j.Status == JobAdStatus.Active` is the ONE translation form this repo has
        // proven against Npgsql (13 sibling sites; GetMyMatchesQueryHandler writes it identically).
        // Projecting the value-converted Status and comparing it afterwards is not the same thing —
        // that is the shape that dies at runtime and is invisible under InMemory. Testcontainers
        // (MyMatchesSurfaceTests) is the oracle that proves this one translates.
        var count = await (
                from m in matches
                join j in db.JobAds.AsNoTracking() on m.JobAdId equals j.Id
                where j.Status == JobAdStatus.Active
                select m.Id)
            .CountAsync(cancellationToken);
        return new MyNewMatchCountDto(count);
    }
}
