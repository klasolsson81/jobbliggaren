using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B / RF-8=8C) — counts the authenticated user's new followed-company ad
/// hits since their last-seen watermark, per-watch grade-filtered read-time. Owner-scoped (reads
/// only the current user's hits + watermark + active watches). No authenticated user / no active
/// follows → honest 0. The soft-delete query filters on <c>FollowedCompanyAdHit</c> and
/// <c>CompanyWatch</c> exclude erased hits and unfollowed watches automatically. NO AI/LLM.
/// <b>Lifecycle-gated (#864):</b> counts only hits whose ad is still <c>Active</c> — the same
/// presentable set its destination (/foretag, <c>ListCompanyWatchesQueryHandler</c>) shows.
///
/// <para>
/// <b>Status-AGNOSTIC (parity <c>GetMyNewMatchCountQueryHandler</c>):</b> counts a hit regardless of
/// its <c>NotificationStatus</c> and <c>SeenAt</c> — the in-app rail answers "new since I last
/// looked", which is INDEPENDENT of email delivery (<c>SeenAt</c> remains the EMAIL-suppression
/// authority, #453/RF-6). Do NOT add the dispatch's <c>Pending AND SeenAt == null</c> due-set
/// predicate here — that would silently redefine the rail.
/// </para>
/// <para>
/// <b>Read-time grade filter (8C) — MIRRORS <c>DigestDispatchJob</c>'s grade mechanic EXACTLY, NOT
/// its due-set:</b> a per-watch "endast matchade" (<c>OnlyMatched</c>) filter narrows the count to
/// ≥Good ads via the shared <c>GradeRankExpression</c> SSOT (read-time; the grade is NEVER persisted
/// — Goodhart, C-E2). The ort filter was already applied SCAN-time (8A, F1), so only the grade axis
/// needs a read-time check. A profile-less user makes the filter INERT (RF-5 under-fork i: count
/// unfiltered rather than a dishonest empty set). A hit under an OnlyMatched watch below ≥Good simply
/// does not count ("SAMMA filter som dispatch, annars fantom-visning").
/// </para>
/// <para>
/// <b>Hot-path shape (branch-on-need, ADR 0045):</b> when NO active watch has an OnlyMatched filter
/// (the common path — reachable since F4a shipped the filter-set write path), the count is a pure SQL
/// <c>COUNT</c> over the hit↔active-watch join — no row materialization, no unpaginated fetch (§5).
/// Only when an OnlyMatched watch contributes do we materialize the (per-user-bounded) new hits to
/// grade-filter them (parity the dispatch loading its pending set). An unfollowed watch's hit is
/// excluded (no active watch on the join) — deliberate, "företag du bevakar" is present-tense
/// (diverges from the dispatch, which treats an absent watch as no-filter-passes).
/// </para>
/// <para>
/// <b>D8:</b> this query reads NO org.nr — the hit↔watch join is on the opaque
/// <c>CompanyWatchId</c>, the #864 <c>JobAds</c> join contributes a <c>where</c> only (never a
/// <c>select</c>), and no company name is resolved (count-only).
/// </para>
/// </summary>
public sealed class GetNewFollowedCompanyAdCountQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch)
    : IQueryHandler<GetNewFollowedCompanyAdCountQuery, NewFollowedCompanyAdCountDto>
{
    public async ValueTask<NewFollowedCompanyAdCountDto> Handle(
        GetNewFollowedCompanyAdCountQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return NewFollowedCompanyAdCountDto.Zero;

        // The set definition — watermark, active watches, the per-watch OnlyMatched fork and the
        // inclusion rule — is single-sourced in NewFollowedCompanyAdSet (#1576) so this count and the
        // destination it links to cannot run two predicates. The D8 seal, the #864 lifecycle gate and
        // the proven translation form live there now, beside the query they describe.
        var scope = await NewFollowedCompanyAdSet.LoadScopeAsync(db, userId, cancellationToken);
        if (scope is null)
            return NewFollowedCompanyAdCountDto.Zero;

        var newHitsBase = NewFollowedCompanyAdSet.NewHits(db, userId, scope.LastSeen);

        if (scope.GradeWatchIds.Count == 0)
        {
            // Common path (no OnlyMatched watch): a pure SQL COUNT over the join, no row
            // materialization. DISTINCT on the ad id because the user-facing unit is the AD, not the
            // hit row (NewFollowedCompanyAdSet.CollapseToAds carries the why) — and with no
            // OnlyMatched watch every hit is included, so distinct ad ids IS that collapse.
            var commonCount = await newHitsBase
                .Select(h => h.JobAdId)
                .Distinct()
                .CountAsync(cancellationToken);
            return new NewFollowedCompanyAdCountDto(commonCount);
        }

        // Grade path: at least one active watch has an "endast matchade" filter. Materialize the
        // (per-user-bounded) new hits and read-time-filter the OnlyMatched watches' hits to ≥Good.
        var newHits = await newHitsBase.ToListAsync(cancellationToken);

        var idsToGrade = newHits
            .Where(h => scope.GradeWatchIds.Contains(h.CompanyWatchId))
            .Select(h => h.JobAdId)
            .Distinct()
            .ToList();

        var resolution = await NewFollowedCompanyAdSet.ResolveMatchingAsync(
            profileBuilder, perUserSearch, idsToGrade, cancellationToken);

        var count = NewFollowedCompanyAdSet
            .CollapseToAds(newHits, scope.GradeWatchIds, resolution.Matching)
            .Count;

        return new NewFollowedCompanyAdCountDto(count);
    }
}
