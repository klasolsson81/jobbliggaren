using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionAdMagnitude;

/// <summary>
/// #1559 — the MAGNITUDE of a saved criterion's ACTIVE ad set: "how many active job ads do the
/// companies this criterion matches have right now". The number the criterion's detail headline
/// renders, and the number that carries the link to the ads themselves.
///
/// <para>
/// A SEPARATE query from <c>BrowseCriterionAdsQuery</c> for the same reason
/// <c>GetCriterionMatchMagnitudeQuery</c> is separate from <c>BrowseCompaniesQuery</c>: the browse
/// returns a <c>PagedResult</c> whose <c>TotalCount</c> is a pagination quantity that must never be
/// read as a magnitude, and the Api endpoint COMPOSES the two sends (§2.3) rather than overloading
/// one response. It is also consumed ALONE, by the detail page, which renders the number without
/// reading a single ad.
/// </para>
///
/// <para>
/// Nullable → 404, parity with every sibling on this aggregate: unknown id and cross-user id are the
/// same answer, so the response is never an existence oracle.
/// </para>
/// </summary>
public sealed record GetCriterionAdMagnitudeQuery(Guid CriterionId)
    : IQuery<CriterionAdMagnitudeDto?>, IAuthenticatedRequest;

/// <summary>
/// The honest ad magnitude. THREE states since #1681 part 2, and a consumer must not collapse any
/// two of them:
/// <list type="bullet">
/// <item><see cref="Magnitude"/> = n - exactly n active ads, or "n or more" when
/// <see cref="Saturated"/>. <c>0</c> is a real answer.</item>
/// <item><see cref="TooBroad"/> - the criterion matched more companies than the breadth gate
/// materialises, so no number exists. Never a zero.</item>
/// <item><see cref="NotMaterialised"/> - no membership has been computed for this criterion's
/// CURRENT predicate yet, so the answer is unknown. Never a zero either, and NOT the same as
/// "too broad": one says we refused, the other says we have not looked.</item>
/// </list>
///
/// <para>
/// <b>Three booleans-and-a-nullable rather than a state enum, deliberately.</b> The Api registers no
/// <c>JsonStringEnumConverter</c>, so an enum would cross the wire as a bare integer and the frontend
/// would be matching on <c>0</c>/<c>1</c>/<c>2</c> - a magic number in the one place this family is
/// most careful about meaning. <c>MyMatchingAdCountDto</c> already answers the same problem the same
/// way, and the constructor below rejects the impossible combinations rather than leaving them to
/// reviewers.
/// </para>
/// </summary>
public sealed record CriterionAdMagnitudeDto(
    int? Magnitude, bool Saturated, bool TooBroad, bool NotMaterialised)
{
    public bool TooBroad { get; } = TooBroad;
    public bool NotMaterialised { get; } = NotMaterialised;

    public int? Magnitude { get; } = Guard(Magnitude, TooBroad, NotMaterialised, Saturated);

    private static int? Guard(int? magnitude, bool tooBroad, bool notMaterialised, bool saturated)
    {
        if (tooBroad && notMaterialised)
        {
            throw new ArgumentException(
                "En bevakning kan inte samtidigt vara for bred och omaterialiserad: det ar tva "
                + "olika svar, och ett tal som ar bada ar inget svar alls.",
                nameof(tooBroad));
        }

        var answerable = !tooBroad && !notMaterialised;
        if (answerable != (magnitude is not null))
        {
            throw new ArgumentException(
                "Ett tal finns exakt nar fragan gar att besvara: ett tal bredvid en vagran vore ett "
                + "golv utgivet for en exakt siffra, och en besvarbar fraga utan tal vore en matning "
                + "vi kastade bort.",
                nameof(magnitude));
        }

        if (saturated && magnitude is null)
        {
            throw new ArgumentException(
                "Saturated utan tal: mattnad ar en egenskap hos ett tal, inte hos en vagran.",
                nameof(saturated));
        }

        return magnitude;
    }

    /// <summary>An exact magnitude, or "<paramref name="count"/>+" when saturated.</summary>
    public static CriterionAdMagnitudeDto Counted(int count, bool saturated) =>
        new(count, saturated, TooBroad: false, NotMaterialised: false);

    /// <summary>Refused by the breadth gate - "for bred", never a zero.</summary>
    public static CriterionAdMagnitudeDto TooBroadToCount { get; } =
        new(null, false, TooBroad: true, NotMaterialised: false);

    /// <summary>Not yet materialised for the current predicate - unknown, and not a zero.</summary>
    public static CriterionAdMagnitudeDto NotMaterialisedYet { get; } =
        new(null, false, TooBroad: false, NotMaterialised: true);

    /// <summary>
    /// The PRODUCT ceiling for the AD question — how far the count query counts before declaring
    /// "10 000+".
    ///
    /// <para>
    /// <b>Its own constant, deliberately, even though it currently equals
    /// <c>CriterionMatchMagnitudeDto.Ceiling</c>.</b> That one is Klas's 2026-07-16 answer to "how
    /// many COMPANIES do we render exactly"; this is the answer to "how many ADS". Two questions, two
    /// ceilings is this port's standing doctrine (CTO Fork G3), and one constant serving both would
    /// mean neither could be moved without moving the other. They are equal today because the same
    /// product judgement applies, not because they are the same number.
    /// </para>
    ///
    /// <para>
    /// <b>Measured non-vacuous, and #1681 part 2 had to re-establish that rather than inherit it.</b>
    /// The old evidence was that the broadest bound-legal criterion matched 39 909 active ads — but
    /// that criterion was resolved LIVE against the register, and part 2's read only ever sees a
    /// criterion the breadth gate admitted. So the question became whether a criterion under that gate
    /// can still saturate. It can: measured on the real register 2026-09-06, the 1 000 companies with
    /// the most active ads carry <b>28 971</b> between them, ~3x this ceiling
    /// (<c>docs/reviews/2026-09-06-1681-part2-read-form-measurement.md</c>). The "10 000+" arm is live
    /// copy, not a branch no data can enter. Re-measure rather than quoting these numbers forward.
    /// </para>
    /// </summary>
    public const int Ceiling = 10_000;
}
