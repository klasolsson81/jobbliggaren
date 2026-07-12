using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — the count of new ads from followed employers NEW since the user's
/// last visit to the follows surface (<c>FollowedCompanyAdHit.CreatedAt &gt;
/// JobSeeker.LastSeenFollowedAdsAt</c>, per-watch grade-filtered read-time). Drives the Översikt
/// "nya annonser från bevakade företag"-row. Parameterless — the count is the authenticated user's
/// own. NOT <c>ICapturesRecentSearch</c>.
/// </summary>
public sealed record GetNewFollowedCompanyAdCountQuery : IQuery<NewFollowedCompanyAdCountDto>;
