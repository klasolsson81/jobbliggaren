using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B / RF-8=8C) — counts the authenticated user's new followed-company ADS
/// (not hit rows — #1576) since their last-seen watermark, per-watch grade-filtered read-time. Owner-scoped (reads
/// only the current user's hits + watermark + active watches). No authenticated user / no active
/// follows → honest 0. The soft-delete query filter on <c>CompanyWatch</c> excludes
/// unfollowed watches automatically; erased hits are HARD-deleted (#868), not filtered. NO AI/LLM.
/// <b>Lifecycle-gated (#864):</b> counts only hits whose ad is still <c>Active</c> — the same
/// presentable set its destination (<c>/foretag/bevakade/nya</c>) shows.
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

        // Both paths are single-sourced in NewFollowedCompanyAdSet: a SQL COUNT(DISTINCT) when no
        // active watch is OnlyMatched, otherwise the new hits loaded and graded read-time. No bound
        // and no token here, so this is the count Översikt always ran (#1666 shares it).
        var input = await NewFollowedCompanyAdSet.LoadCountInputAsync(
            db, userId, scope, upperBound: null, maxGradeHits: null, cancellationToken);
        if (input.Exact is { } exact)
            return new NewFollowedCompanyAdCountDto(exact);

        var resolution = await NewFollowedCompanyAdSet.ResolveMatchingAsync(
            profileBuilder,
            perUserSearch,
            NewFollowedCompanyAdSet.GradeCandidates(input.GradeHits, scope.GradeWatchIds),
            cancellationToken);

        var count = NewFollowedCompanyAdSet
            .CollapseToAds(input.GradeHits, scope.GradeWatchIds, resolution.Matching)
            .Count;
        return new NewFollowedCompanyAdCountDto(count);
    }
}
