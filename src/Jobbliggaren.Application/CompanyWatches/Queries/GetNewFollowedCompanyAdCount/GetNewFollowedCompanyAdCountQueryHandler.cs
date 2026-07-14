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
/// <c>select</c> — the seal restated inline below), and no company name is resolved (count-only).
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

        // The user-read watermark (null = never looked → every hit is new). Sibling of
        // GetMyNewMatchCount reading LastSeenMatchesAt.
        var lastSeen = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.LastSeenFollowedAdsAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Active watches with their per-watch filters. The global soft-delete query filter restricts
        // this to the user's ACTIVE follows. The Filter is a property-converted opaque jsonb blob, so
        // OnlyMatched can only be inspected in memory (never a server-side predicate) — parity
        // DigestDispatchJob's filterByWatchId.
        var activeWatches = await db.CompanyWatches
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => new { w.Id, w.Filter })
            .ToListAsync(cancellationToken);

        if (activeWatches.Count == 0)
            return NewFollowedCompanyAdCountDto.Zero;

        var gradeWatchIds = activeWatches
            .Where(w => w.Filter is { OnlyMatched: true })
            .Select(w => w.Id)
            .ToHashSet();

        // New hits since the watermark, restricted to the user's ACTIVE watches by an equijoin on the
        // (opaque) CompanyWatchId — NOT an org.nr read (D8). The global soft-delete filters on both
        // sides exclude deleted hits + unfollowed watches. Joining (not an id-set Contains) also
        // sidesteps the strongly-typed-VO Contains translation trap. STATUS-AGNOSTIC with respect to
        // NOTIFICATION delivery (no NotificationStatus / SeenAt predicate) — that is a different axis
        // from the ad's LIFECYCLE, gated below.
        //
        // LIFECYCLE (#864) — the join to JobAds with `Status == Active` is why this rail is honest.
        // Before it, this handler joined JobAds NOT AT ALL: it counted hit rows, so an ARCHIVED ad was
        // counted. Its own destination is gated (ListCompanyWatchesQueryHandler:100), so the rail said
        // "3 nya annonser från företag du bevakar", the user clicked, and /foretag showed zero — a count
        // that promises more than its set can deliver. CompanyWatchScanJob's Active gate (:156) only
        // proves the ad was live when the HIT WAS RECORDED; archiving is every ad's normal end of life.
        // Structurally the same defect as the match badge (GetMyNewMatchCountQueryHandler), which also
        // joined nothing — the two rails sit on the same page.
        //
        // D8 SEAL, RESTATED because it must not rot: the JobAds join reads `j.Status` and PROJECTS
        // NOTHING from JobAds. The count-only DTO is unchanged, so the rail still reads ZERO org.nr —
        // the strongest D8 property in the bevakning lane (ADR 0087 D8). A future edit that projects an
        // ad column here breaks that seal; the seal is the reason the join contributes a `where`, never
        // a `select`.
        //
        // ALLOW-list (`== Active`), never `!= Archived`: the two are indistinguishable on every row that
        // exists today (Expired has no writer, #886), but a deny-list admits every status added later —
        // and Erased (#842) is an Art. 17 tombstone. Gating at the SOURCE serves both branches: the
        // common path COUNTs this query directly, and the grade path materialises it. The grade path
        // also happens to be gated downstream (FilterToMatchingAsync → PerUserJobAdSearchQuery:370) —
        // that accident is now redundant, and redundancy is the right posture for a control nobody meant
        // to rely on.
        //
        // Query syntax + `j.Status == JobAdStatus.Active` is the ONE translation form this repo has
        // proven against Npgsql (the match badge writes it identically; the comparison crosses a
        // SmartEnum HasConversion, and a form that dies at runtime is invisible under InMemory).
        // Testcontainers (FollowedCompanyAdRailTests) is the oracle that proves this one translates
        // AND counts the presentable set — the role MyMatchesSurfaceTests plays for the match rail.
        var newHitsBase =
            from h in db.FollowedCompanyAdHits
            where h.UserId == userId && (lastSeen == null || h.CreatedAt > lastSeen)
            join w in db.CompanyWatches on h.CompanyWatchId equals w.Id
            where w.UserId == userId
            join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
            where j.Status == JobAdStatus.Active
            select new { h.JobAdId, h.CompanyWatchId };

        if (gradeWatchIds.Count == 0)
        {
            // Common path (no OnlyMatched watch):
            // a pure SQL COUNT over the join, no row materialization.
            var commonCount = await newHitsBase.CountAsync(cancellationToken);
            return new NewFollowedCompanyAdCountDto(commonCount);
        }

        // Grade path: at least one active watch has an "endast matchade" filter. Materialize the
        // (per-user-bounded) new hits and read-time-filter the OnlyMatched watches' hits to ≥Good.
        var newHits = await newHitsBase.ToListAsync(cancellationToken);

        var idsToGrade = newHits
            .Where(h => gradeWatchIds.Contains(h.CompanyWatchId))
            .Select(h => h.JobAdId)
            .Distinct()
            .ToList();

        // Read-time ≥Good membership via the shared SSOT. Api-side → BuildFullForSortAsync
        // (ICurrentUser-scoped), NOT the Worker's BuildFullForUserIdAsync. A PROFILE-LESS user makes
        // the OnlyMatched filter INERT (branch on assessability BEFORE the call — FilterToMatchingAsync
        // fail-fasts on an empty-SSYK profile).
        IReadOnlySet<JobAdId> matching;
        if (idsToGrade.Count == 0)
        {
            matching = new HashSet<JobAdId>();
        }
        else
        {
            var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
            matching = profile.Fast.SsykGroupConceptIds.Count > 0
                ? await perUserSearch.FilterToMatchingAsync(profile, idsToGrade, cancellationToken)
                : idsToGrade.ToHashSet(); // INERT: profile-less → every candidate passes
        }

        var count = newHits.Count(h =>
            !gradeWatchIds.Contains(h.CompanyWatchId) || matching.Contains(h.JobAdId));

        return new NewFollowedCompanyAdCountDto(count);
    }
}
