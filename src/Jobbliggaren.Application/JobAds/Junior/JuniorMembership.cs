using System.Linq.Expressions;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore.Query;

namespace Jobbliggaren.Application.JobAds.Junior;

/// <summary>
/// #2264 (ADR 0187) — the one definition of "a junior job" over stored state: the ad is in one of the enabled fields'
/// occupation groups, and its assessment says so — an admin decided it is a junior job, or nobody decided and the rules
/// said so. A correlated EXISTS on the assessment's primary key, so it composes into any query over ads — the public
/// list's, the admin queue's "Visas" tab and the <c>/jobb</c> filter's (#2261) alike. It says nothing about the ad's
/// lifecycle: every caller composes it under its own gate (<c>JobAdPublication.IsListed(now)</c> for the public list), so
/// a closed or erased ad leaves at once.
/// </summary>
/// <remarks>
/// It does not read the rule-set version: a verdict from the previous rules keeps showing until the sweep has
/// re-classified the ad, so deploying new rules never empties the list.
/// </remarks>
public static class JuniorMembership
{
    /// <summary>The ad is in one of the enabled fields' occupation groups.</summary>
    public static Expression<Func<JobAd, bool>> InFields(IReadOnlyCollection<string> occupationGroups)
    {
        ArgumentNullException.ThrowIfNull(occupationGroups);
        var groups = occupationGroups.ToList();

        return j => j.OccupationGroupConceptId != null && groups.Contains(j.OccupationGroupConceptId);
    }

    /// <summary>The assessment says junior: an admin decided so, or nobody decided and the rules said so.</summary>
    public static Expression<Func<JobAdJuniorAssessment, bool>> SaysJunior { get; } =
        a => a.Decision == JuniorDecision.Junior || (a.Decision == null && a.Verdict == JuniorVerdict.Junior);

    /// <summary>The ad is in an enabled field and its assessment says junior.</summary>
    public static Expression<Func<JobAd, bool>> Of(
        IQueryable<JobAdJuniorAssessment> assessments, IReadOnlyCollection<string> occupationGroups)
    {
        ArgumentNullException.ThrowIfNull(assessments);

        var inFields = InFields(occupationGroups);
        var junior = assessments.Where(SaysJunior);
        Expression<Func<JobAd, bool>> assessedJunior = j => junior.Any(a => a.Id == j.Id);

        return Expression.Lambda<Func<JobAd, bool>>(
            Expression.AndAlso(
                inFields.Body,
                ReplacingExpressionVisitor.Replace(assessedJunior.Parameters[0], inFields.Parameters[0], assessedJunior.Body)),
            inFields.Parameters);
    }

    /// <summary>The groups of every enabled field, for <see cref="InFields"/> and <see cref="Of"/>.</summary>
    public static IReadOnlyCollection<string> GroupsOf(IReadOnlyList<JuniorField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.SelectMany(f => f.OccupationGroups).ToHashSet(StringComparer.Ordinal);
    }
}
