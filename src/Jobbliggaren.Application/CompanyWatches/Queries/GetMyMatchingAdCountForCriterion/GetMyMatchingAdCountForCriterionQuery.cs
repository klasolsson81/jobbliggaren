using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetMyMatchingAdCountForCriterion;

/// <summary>
/// #1656 (b) — "how many of this criterion's active ads match ME", the third of the three numbers
/// Klas asked for in #1559 (the other two — matching companies, active ads — shipped in #1653).
/// The number the criterion's detail page renders, and the number that carries the link to exactly
/// those ads.
///
/// <para>
/// <b>Named <c>My…Count</c>, not <c>…Magnitude</c>, and both halves of that are load-bearing</b>
/// (senior-cto-advisor 2026-09-05). <c>My</c> separates it from
/// <c>GetCriterionMatchMagnitudeQuery</c>, which is about how many COMPANIES match the CRITERION —
/// a different question that the word "match" alone cannot disambiguate. <c>Count</c> rather than
/// <c>Magnitude</c> is the doctrine in the name: a magnitude in this family is capped and may
/// saturate, whereas this number is EXACT or ABSENT and never carries a "+".
/// </para>
///
/// <para>
/// <b>Two different nulls, at two different levels, and ADR 0120 warns explicitly about confusing
/// them.</b> A <c>null</c> RESPONSE is the authorization signal — unknown id and another user's id
/// are the same answer, mapped to 404 so the endpoint is never an existence oracle. A <c>null</c>
/// <see cref="MyMatchingAdCountDto.Count"/> INSIDE a 200 body is the product signal: "we did not
/// ask", because the user has stated no occupation. Read this docblock rather than inferring either
/// from the <c>IQuery&lt;T?&gt;</c> shape, which both sides share.
/// </para>
/// </summary>
/// <param name="AdMagnitude">
/// The criterion's ACTIVE-ad magnitude, measured by the caller and handed in. It is not an
/// optimisation detail: the request already pays for this number to render the headline beside this
/// one, and asking the register a second time to learn the same fact is what made a refusal cost
/// seconds instead of nothing (measured 2026-09-05). The DTO travels, never a bare <c>int</c> — the
/// other number in reach is a pagination total that coincides with the bound at one page size and
/// not at another.
/// <para>
/// It is a MEASUREMENT, never a verdict. Whether the set is too broad is decided inside
/// <c>CriterionMatchingAdSet</c>, so this query cannot carry a second opinion about it. And it
/// grants no access: the handler still loads the criterion owner-scoped and 404s before this number
/// is read, so a magnitude measured for someone else's criterion buys nothing.
/// </para>
/// </param>
public sealed record GetMyMatchingAdCountForCriterionQuery(
    Guid CriterionId, CriterionAdMagnitudeDto AdMagnitude)
    : IQuery<MyMatchingAdCountDto?>, IAuthenticatedRequest;

/// <summary>
/// The honest personal count. <see cref="Count"/> is EXACT when present — there is no saturation
/// arm and no ceiling marker, because the underlying set is refused rather than truncated when it
/// grows too large (ADR 0120 clause 5).
///
/// <para>
/// Three states, and a consumer must not collapse any two of them:
/// <list type="bullet">
/// <item><c>Count = n</c>, <c>TooBroad = false</c> — exactly <c>n</c> ads match. <c>n = 0</c> is a
/// real answer.</item>
/// <item><c>Count = null</c>, <c>TooBroad = false</c> — NOT ASSESSED: the user has stated no
/// occupation, so matching is undefined. Parity <c>CompanyWatchDto.MatchingAdCount</c>, which is
/// the shape Klas asked this surface to mirror; the FE renders the occupation nudge, never a
/// zero.</item>
/// <item><c>Count = null</c>, <c>TooBroad = true</c> — the watch matches more ads than the product
/// will grade, so no honest number exists. Also never a zero.</item>
/// </list>
/// The fourth combination is rejected in the constructor rather than left to reviewers: a count
/// that is simultaneously known and unanswerable is not a state this question has.
/// </para>
/// </summary>
public sealed record MyMatchingAdCountDto(int? Count, bool TooBroad)
{
    public int? Count { get; } = !TooBroad || Count is null
        ? Count
        : throw new ArgumentException(
            "TooBroad utesluter ett Count: en bevakning som är för bred för att graderas har inget "
            + "tal, och ett tal bredvid TooBroad skulle vara ett golv utgivet för en exakt siffra.",
            nameof(Count));

    /// <summary>Exactly <paramref name="count"/> ads match; <c>0</c> is a real answer.</summary>
    public static MyMatchingAdCountDto Counted(int count) => new(count, TooBroad: false);

    /// <summary>No stated occupation — matching is undefined, and that is not a zero.</summary>
    public static MyMatchingAdCountDto NotAssessed { get; } = new(null, TooBroad: false);

    /// <summary>Too many ads to grade — no honest number exists, and that is not a zero either.</summary>
    public static MyMatchingAdCountDto TooBroadToCount { get; } = new(null, TooBroad: true);
}
