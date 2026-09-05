using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.RecentJobSearches;

/// <summary>
/// The one predicate deciding which values of the employer axis may leave the request that
/// carried them — into <c>recent_job_searches.employer_list</c> on the way in
/// (<c>RecentJobSearchCaptureBehavior</c>, A2) and into count, label and projection on the way
/// out (<c>ListRecentSearchesQueryHandler</c>). ADR 0087 D8(c)'s masked arm, #1471: an org.nr is
/// public legal-entity data, but for an enskild firma it IS the holder's personnummer (#841), so
/// a personnummer-shaped value is withheld on both sides by the same rule.
/// </summary>
/// <remarks>
/// One home, because a rule with two normalisers is two rules (#844) — and the write gate and the
/// read mask drifting apart is exactly how a value refused on the way in would find a second
/// route to the wire. Rows written before A2 (2026-08-19) can still carry such a value; the read
/// side is what keeps it off the wire for them.
/// </remarks>
internal static class EmployerAxisGate
{
    /// <summary>
    /// #841 / ADR 0087 D8(c) - true when the value is a personnummer, OR cannot be parsed at all.
    /// Both are in the name because they are two facts: the house keeps them apart where it counts
    /// them (ScbLegalEntityFilter buckets invalid and pnr-shaped separately), and fusing them
    /// silently is what makes a call site read as narrower than it is. Delegates to the domain's
    /// own detector rather than re-deriving the rule, so this axis and every other org.nr surface
    /// refuse on exactly the same predicate (#844: a rule with two normalisers is two rules).
    /// </summary>
    public static bool IsWithheld(string employer)
    {
        var orgNr = OrganizationNumber.Create(employer);
        return orgNr.IsFailure || orgNr.Value.IsPersonnummerShaped();
    }

    /// <summary>
    /// The values of a persisted employer axis that may reach the count, the label and the wire:
    /// every value <see cref="IsWithheld"/> does not refuse, in their stored order.
    /// </summary>
    public static IReadOnlyList<string> Surfaceable(IReadOnlyList<string> employers) =>
        [.. employers.Where(employer => !IsWithheld(employer))];
}
