using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionIdentity;
using Jobbliggaren.Application.JobAds.Queries;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ListNewCriterionAds;

/// <summary>
/// ADR 0175 D7 — the ads behind one Branschbevakning's "N nya": <c>/foretag/branschbevakningar/[id]/nya</c>. Null for
/// an id the caller does not own (404, the ADR 0031 probe logged). Deliberately no page parameters: the surface is a
/// capped window the visit acknowledges, not a paginated list (the #1576 rule, <c>AcknowledgementWindow</c>).
/// </summary>
public sealed record ListNewCriterionAdsQuery(Guid CriterionId)
    : IQuery<NewCriterionAdsDto?>, IAuthenticatedRequest;

/// <summary>
/// One ad on the page. <see cref="MatchesYou"/> is read at request time and never stored; <c>null</c> means not
/// assessed (no stated occupation), never a "no".
/// </summary>
public sealed record NewCriterionAdRow(JobAdDto Ad, bool? MatchesYou);

/// <summary>
/// ADR 0175 D7 — the window of new ads, newest first, or the reason there is none.
///
/// <para>
/// <b><see cref="AcknowledgedThrough"/> is the scan clock's, from the window the server cut</b>, and the client hands
/// it back verbatim; a client-side value would sit below the hits it read (see <c>NewFollowedCompanyAdsDto</c>).
/// </para>
/// </summary>
public sealed record NewCriterionAdsDto(
    CriterionIdentityDto Criterion,
    IReadOnlyList<NewCriterionAdRow> Rows,
    bool MatchingAssessed,
    DateTimeOffset? AcknowledgedThrough,
    bool Truncated,
    bool TooBroad,
    bool NotMaterialised)
{
    public IReadOnlyList<NewCriterionAdRow> Rows { get; } =
        !(TooBroad && NotMaterialised) && (!(TooBroad || NotMaterialised)
            || (Rows.Count == 0 && AcknowledgedThrough is null && !Truncated))
            ? Rows
            : throw new ArgumentException(
                "En bevakning som inte är genomsökt har inga nya annonser att visa och inget att kvittera.",
                nameof(Rows));

    public static NewCriterionAdsDto Refused(CriterionIdentityDto criterion) =>
        new(criterion, [], MatchingAssessed: false, AcknowledgedThrough: null, Truncated: false,
            TooBroad: true, NotMaterialised: false);

    public static NewCriterionAdsDto NotScanned(CriterionIdentityDto criterion) =>
        new(criterion, [], MatchingAssessed: false, AcknowledgedThrough: null, Truncated: false,
            TooBroad: false, NotMaterialised: true);

    public static NewCriterionAdsDto Nothing(CriterionIdentityDto criterion) =>
        new(criterion, [], MatchingAssessed: false, AcknowledgedThrough: null, Truncated: false,
            TooBroad: false, NotMaterialised: false);
}
