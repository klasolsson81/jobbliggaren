using System.Collections;
using System.Reflection;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.SavedSearches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches;

/// <summary>
/// #2199 PR 2 — <see cref="SearchCriteriaMapping.ToFilterCriteria"/> is the one translation of a
/// stored criteria into the search filter, and every axis of <see cref="SearchCriteria"/> must cross
/// it with its value intact. Written on the value object rather than on a saved search, because no
/// write path stores every axis on a saved search; the criteria below is the shape
/// <c>RecentSearchCaptureGate</c> builds from a committed /jobb search.
/// </summary>
public class SearchCriteriaMappingParityTests
{
    // Members of SearchCriteria that are not a filter axis. Declared rather than derived, so a new
    // member with no decision fails below.
    private static readonly HashSet<string> NotAFilterAxis = new(StringComparer.Ordinal)
    {
        "SortBy",
    };

    [Fact]
    public void ToFilterCriteria_CarriesEverySearchCriteriaAxis_WithItsValue()
    {
        var criteria = SearchCriteria.Create(
            occupationGroup: ["grp_12345"],
            occupationName: ["name_12345"],
            municipality: ["sthlm_kn"],
            region: ["stockholm"],
            employmentType: ["tillsvidare"],
            worktimeExtent: ["heltid"],
            employer: ["5566010101"],
            remote: true,
            q: "backend",
            sortBy: JobAdSortBy.PublishedAtDesc).Value;

        var filter = SearchCriteriaMapping.ToFilterCriteria(criteria);

        var axes = typeof(SearchCriteria)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract" && !NotAFilterAxis.Contains(p.Name))
            .ToArray();
        axes.ShouldNotBeEmpty("the guard measures nothing if SearchCriteria exposes no axes");

        foreach (var axis in axes)
        {
            var target = typeof(JobAdFilterCriteria).GetProperty(axis.Name);
            target.ShouldNotBeNull(
                $"SearchCriteria.{axis.Name} has no JobAdFilterCriteria member of the same name to map onto");

            var stored = axis.GetValue(criteria);
            var mapped = target!.GetValue(filter);
            var same = (stored, mapped) switch
            {
                (IEnumerable a, IEnumerable b) when stored is not string =>
                    a.Cast<object>().SequenceEqual(b.Cast<object>()),
                _ => Equals(stored, mapped),
            };
            same.ShouldBeTrue($"SearchCriteria.{axis.Name} does not reach the filter with its value");
        }
    }
}
