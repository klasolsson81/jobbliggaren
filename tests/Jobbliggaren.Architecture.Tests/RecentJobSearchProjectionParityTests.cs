using System.Reflection;
using Jobbliggaren.Application.RecentJobSearches.Queries;
using Jobbliggaren.Domain.RecentJobSearches;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1407 — every search dimension persisted on <see cref="RecentJobSearch"/> must reach the read
/// projection <see cref="RecentJobSearchDto"/>. Nothing bound the two before this guard, and the
/// gap is what shipped the defect: <c>Remote</c> landed on
/// <see cref="Domain.SavedSearches.SearchCriteria"/>, the filter hash, the entity column and the
/// per-row count filter in #551 PR-D, but not on the DTO. The compiler saw nothing — the DTO is an
/// independent declaration — so the replay href had no value to carry and hard-coded <c>false</c>.
/// The row's count was computed WITH the axis and its link replayed WITHOUT it. This test is RED
/// against that state and green now.
///
/// <para>
/// <b>Written in the shape <see cref="MatchPreferencesContractParityTests"/> established</b>, and
/// for the same reason: guard the invariant, not the instance. Written this way it would have
/// failed the day #551 PR-D added the column — before the surface that depended on it existed.
/// </para>
///
/// <para>
/// <b>Name-based on purpose</b>, like its sibling. The property name IS the contract here: the DTO
/// name is the camelCased wire key and the FE Zod key. Collection dimensions carry a <c>List</c>
/// suffix on the projection (<c>OccupationGroup</c> → <c>OccupationGroupList</c>); scalars keep
/// their name. Both spellings are accepted, so the guard measures presence of the DIMENSION rather
/// than of one spelling of it.
/// </para>
///
/// <para>
/// Until #1471 this file also carried a <c>NotSurfaced</c> allow-list with one entry, <c>Employer</c>:
/// the org.nr axis was withheld from the projection (ADR 0087 D8(c), the EXCLUDED arm) at the cost the
/// entry itself named — a captured employer search replayed broader than it was. #1471 moved the axis
/// to D8(c)'s MASKED arm: the value reaches the projection, and <c>EmployerAxisGate</c> withholds a
/// personnummer-shaped one on the write side and the read side alike. With no withheld dimension left,
/// the list went too — an empty allow-list plus the two tests that read it would have been green on
/// nothing. Withholding a dimension again means reinstating that two-way interlock here, visibly, not
/// adding an exception clause to the assertion below.
/// </para>
///
/// <para>
/// This is the ONE home for "every dimension reaches the projection". <c>RecentJobSearchDtoContractTests</c>
/// (Application.UnitTests — named, not cref'd, because this assembly does not reference it) pins the
/// exact property SET, and <c>ListRecentSearchesCountReplayParityTests</c> (same assembly) pins that the
/// projected VALUES are the ones the count ran on — this file can see names, not values.
/// </para>
/// </summary>
public class RecentJobSearchProjectionParityTests
{
    // Entity members that are not user-stated search dimensions: identity, the derived hash, and
    // the visit bookkeeping. An explicit allow-list rather than a filter on shape, so the default
    // — silence — FAILS the test rather than passing it.
    private static readonly HashSet<string> NotSearchDimensions = new(StringComparer.Ordinal)
    {
        "Id", "JobSeekerId", "FilterHash", "LastViewedAt", "LastSeenCount", "CreatedAt",
        "DomainEvents",
    };

    [Fact]
    public void EverySearchDimension_ReachesTheReadProjection()
    {
        var projected = ProjectedProperties();
        var dimensions = SearchDimensions().ToArray();

        // Floor against a broken source set: an inclusion spec can never detect that it is
        // measuring nothing. If SearchDimensions() ever comes back empty — allow-list widened,
        // properties no longer public, the entity restructured — `missing` is empty too and BOTH
        // facts pass green and silent.
        dimensions.ShouldNotBeEmpty(
            "the guard measures nothing if RecentJobSearch exposes no search dimensions");

        var missing = dimensions
            .Where(name => !projected.Contains(name) && !projected.Contains(name + "List"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        missing.ShouldBeEmpty(
            "every RecentJobSearch search dimension must reach RecentJobSearchDto. "
            + $"Missing: {string.Join(", ", missing)}. "
            + "A dimension the projection omits cannot be replayed, so the row's count and the "
            + "list its link produces stop resting on the same criterion (#1407, #1471).");
    }

    private static HashSet<string> ProjectedProperties() =>
        typeof(RecentJobSearchDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> SearchDimensions() =>
        typeof(RecentJobSearch)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !NotSearchDimensions.Contains(name));
}
