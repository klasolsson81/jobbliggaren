using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;

/// <summary>
/// #1576 — the ads behind the Översikt "N nya annonser från bevakade företag" number: the
/// destination that number links to. Runs the SAME definition as
/// <c>GetNewFollowedCompanyAdCountQueryHandler</c> via <c>NewFollowedCompanyAdSet</c>, so the count
/// and the set it promises cannot disagree (ADR 0120 — a rendered count is true or it is absent).
///
/// <para>
/// <b>Parameterless, deliberately.</b> The "bara de som matchar"-arm is a VIEW filter over a set the
/// page fetched whole (senior-cto-advisor 2026-08-31), never a second query: two queries can see two
/// different sets, which is the defect class this route exists to close. Each row carries its own
/// <c>MatchesYou</c> so the client can narrow without asking again.
/// </para>
///
/// <para>
/// <b>No <c>Page</c>/<c>PageSize</c>, deliberately.</b> They would trip
/// <c>PagedResultContractTests.HasPagedSemantics</c>, which then requires the response to be exactly
/// <c>PagedResult&lt;&gt;</c> — a sealed type with nowhere to carry the acknowledgement window. The
/// unbounded-fetch rule (§5) is satisfied by a hard cap instead, the delivered
/// <c>DisambiguateEmployersQuery.MaxResults</c> posture.
/// </para>
/// </summary>
public sealed record ListNewFollowedCompanyAdsQuery : IQuery<NewFollowedCompanyAdsDto>;
