using System.Linq.Expressions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Publication;

/// <summary>
/// The floor a public list must clear before a search engine may index it (ADR 0166, the Amendment 2026-11-22; ADR 0190):
/// at least <see cref="MinimumAds"/> listed ads from at least <see cref="MinimumEmployers"/> employers, and no employer
/// holding more than half of them. The numbers and the definition of an employer are security-auditor's and are not
/// configuration: changing either is an ADR 0166 amendment.
/// </summary>
/// <remarks>
/// <see cref="Outcome"/> composes <see cref="SpreadOf"/> over a list and filters it by <see cref="Holds"/>, so the spread
/// is built and compared in SQL and only the outcome leaves the database.
/// </remarks>
public static class PublicListFloor
{
    /// <summary>The fewest listed ads an indexable list holds.</summary>
    internal static readonly int MinimumAds = 100;

    /// <summary>The fewest distinct employers an indexable list holds.</summary>
    internal static readonly int MinimumEmployers = 25;

    /// <summary>The floor, over a list's employer spread.</summary>
    internal static Expression<Func<EmployerSpread, bool>> Holds { get; } = s =>
        s.Ads >= MinimumAds && s.Employers >= MinimumEmployers && s.LargestEmployerAds * 2 <= s.Ads;

    /// <summary>One row when <paramref name="ads"/> clear the floor, and none when they do not.</summary>
    public static IQueryable<bool> Outcome(IQueryable<JobAd> ads) =>
        SpreadOf(ads).Where(Holds).Select(_ => true);

    /// <summary>
    /// The spread of <paramref name="ads"/> over their employers. Two ads are one employer when they share an organisation
    /// number or share a company name, the name compared trimmed and in lower case.
    /// <list type="bullet">
    ///   <item><see cref="EmployerSpread.Employers"/> is the distinct numbers plus the distinct names minus the distinct
    ///   pairs of the two, over the ads that carry a number. An ad without a number adds no employer.</item>
    ///   <item><see cref="EmployerSpread.LargestEmployerAds"/> is the larger of the largest group by number and the largest
    ///   group by name, over the ads that carry a number, plus every ad without a number.</item>
    /// </list>
    /// </summary>
    /// <returns>One spread for a list with at least one ad, and none for an empty list.</returns>
    internal static IQueryable<EmployerSpread> SpreadOf(IQueryable<JobAd> ads)
    {
        ArgumentNullException.ThrowIfNull(ads);

        // CA1304/CA1311: the call is translated to SQL lower(), and never runs under a .NET culture.
#pragma warning disable CA1304, CA1311
        var pairs = ads
            .GroupBy(j => new { j.OrganizationNumber, Name = j.Company.Name.Trim().ToLower() })
            .Select(g => new EmployerPair { OrganizationNumber = g.Key.OrganizationNumber, Name = g.Key.Name, Ads = g.Count() });
#pragma warning restore CA1304, CA1311
        var numbered = pairs.Where(p => p.OrganizationNumber != null);

        // One pass over the list's distinct (number, name) pairs gives the ads, the pairs, the numbers, the names and
        // the ads without a number. The two largest groups are a subquery each, over the same pairs.
        return pairs
            .GroupBy(_ => 1)
            .Select(g => new EmployerSpread
            {
                Ads = g.Sum(p => p.Ads),
                Employers = g.Where(p => p.OrganizationNumber != null).Select(p => p.OrganizationNumber).Distinct().Count()
                    + g.Where(p => p.OrganizationNumber != null).Select(p => p.Name).Distinct().Count()
                    - g.Count(p => p.OrganizationNumber != null),
                LargestEmployerAds = Math.Max(
                        numbered.GroupBy(p => p.OrganizationNumber).Select(n => (int?)n.Sum(p => p.Ads)).Max() ?? 0,
                        numbered.GroupBy(p => p.Name).Select(n => (int?)n.Sum(p => p.Ads)).Max() ?? 0)
                    + g.Where(p => p.OrganizationNumber == null).Sum(p => p.Ads),
            })
            // At most one row either way. Taking it makes the spread a subquery, so a caller's filter compares the
            // computed counts instead of repeating every count in a HAVING clause.
            .Take(1);
    }

    /// <summary>One distinct (organisation number, normalised name) pair of a list, and its ads.</summary>
    private sealed class EmployerPair
    {
        public string? OrganizationNumber { get; init; }

        public string Name { get; init; } = string.Empty;

        public int Ads { get; init; }
    }
}
