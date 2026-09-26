using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionIdentity;

/// <summary>
/// #1681 part 2 — one criterion's IDENTITY: the codes and the user's optional label, and nothing
/// else. It exists to give the two detail surfaces a cheap way to render their heading.
///
/// <para>
/// <b>Why it exists is a cost finding, not a tidiness one</b> (senior-cto-advisor, 2026-09-06,
/// reversing its own earlier "follow-up issue" ruling once the fan-in was measured). Both detail
/// pages resolved their heading by calling <c>GET /me/company-watch-criteria</c> — the LIST route.
/// That was cheap before part 2. Part 2 gave every row of that list a materialised ad count and a
/// per-user graded matching count, so the detail page came to fetch <b>twenty criteria's graded ad
/// counts in order to render one string</b>: up to ~380 ms measured, for a label. That is a design
/// defect independent of any budget — a page paying for twenty answers to get one — and part 2 is
/// what created it, so part 2 owns the fix.
/// </para>
///
/// <para>
/// <b>Composed into the routes the pages already call, never a fifth route.</b>
/// <c>CompanyWatchCriteriaEndpoints</c> is explicit that its four routes share ONE rate-limit bucket
/// and that an additional call on these pages spends the detail page's own allowance; the margin is
/// bought back by removing a call, not by adding one. So this rides the existing
/// <c>/{id}/companies</c> and <c>/{id}/ads</c> responses as a fourth composed member (§2.3) and costs
/// no extra token, no extra round trip from the browser, and no new policy.
/// </para>
///
/// <para>
/// Nullable → 404, parity with every sibling on this aggregate: unknown id and cross-user id are the
/// same answer, so the response is never an existence oracle.
/// </para>
/// </summary>
public sealed record GetCriterionIdentityQuery(Guid CriterionId)
    : IQuery<CriterionIdentityDto?>, IAuthenticatedRequest;

/// <summary>
/// The criterion as a HEADING needs it: raw codes + the optional label. Deliberately carries no
/// numbers — the whole point is that a caller wanting the label does not pay for the counts. The
/// human display-label is still derived FE-side from the reference tree, exactly as
/// <c>CompanyWatchCriterionDto</c> documents; a second label authority could only drift.
/// </summary>
public sealed record CriterionIdentityDto(
    Guid Id,
    IReadOnlyList<string> SniCodes,
    IReadOnlyList<string> MunicipalityCodes,
    string? Label);
