using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore.Query;

namespace Jobbliggaren.Infrastructure.JobAds.JuniorIt;

/// <summary>
/// Whether an ad is a junior IT job (ADR 0186): it is in one of the field's occupation groups, and either its
/// Platsbanken id is curated or its title matches the headline pattern. The field holds on both arms, so a curated id
/// outside it is never listed. It says nothing about the ad's lifecycle; every caller composes it with its own gate
/// (the public list with <c>JobAdPublication.IsListed</c>).
/// </summary>
/// <remarks>
/// <c>Regex.IsMatch</c> is translated by the Npgsql provider to <c>~*</c>, which is why this lives in Infrastructure
/// (AGENTS.md §2.1, axis 3). The options must stay a literal inside the lambda: a captured value does not translate.
/// The pattern is written with both cases of every non-ASCII letter, so the database locale's case folding never
/// decides a match (<c>JuniorItHeadlinePatternAgreementTests</c>).
/// </remarks>
internal static class JuniorItPredicate
{
    internal static Expression<Func<JobAd, bool>> Matches(JuniorItSet set)
    {
        var curated = set.CuratedExternalIds;
        var groups = set.OccupationGroups;
        var pattern = set.HeadlinePattern;

        Expression<Func<JobAd, bool>> inField = j => groups.Contains(j.OccupationGroupConceptId);

        var arms = new List<Expression<Func<JobAd, bool>>>(2)
        {
            j => j.External != null
                 && j.External.Source == JobSource.Platsbanken
                 && curated.Contains(j.External.ExternalId),
            j => Regex.IsMatch(j.Title, pattern, RegexOptions.IgnoreCase),
        };

        var anyArm = arms.Aggregate(JobAdSearchComposition.OrElse);
        return Expression.Lambda<Func<JobAd, bool>>(
            Expression.AndAlso(
                inField.Body,
                ReplacingExpressionVisitor.Replace(anyArm.Parameters[0], inField.Parameters[0], anyArm.Body)),
            inField.Parameters);
    }
}
