using System.Reflection;
using Jobbliggaren.Application.RecentJobSearches.Queries;
using Jobbliggaren.Domain.RecentJobSearches;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1407 — a search dimension persisted on <see cref="RecentJobSearch"/> must either reach the
/// read projection <see cref="RecentJobSearchDto"/> or be classified, by a human, as deliberately
/// not surfaced. Nothing bound the two before this guard, and the gap is what shipped the defect:
/// <c>Remote</c> landed on <see cref="Domain.SavedSearches.SearchCriteria"/>, the filter hash, the
/// entity column and the per-row count filter in #551 PR-D, but not on the DTO. The compiler saw
/// nothing — the DTO is an independent declaration — so the replay href had no value to carry and
/// hard-coded <c>false</c>. The row's count was computed WITH the axis and its link replayed
/// WITHOUT it. This test is RED against that state and green now.
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
/// This is the ONE home for "which dimensions reach the projection, and why one does not".
/// <see cref="RecentJobSearchDtoContractTests"/> pins the exact property SET; this pins what that
/// set must contain. A comment repeating either elsewhere is drift waiting to happen.
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

    // Dimensions deliberately withheld from the projection. Each entry states the ground, because
    // the asymmetry looks like an inconsistency to clean up and that is exactly how the leak gets
    // reintroduced.
    private static readonly Dictionary<string, string> NotSurfaced = new(StringComparer.Ordinal)
    {
        ["Employer"] =
            "org.nr. Data minimisation, Art. 5(1)(c) — the same ground "
            + "EmployerOrgNumberSurfaceGuardTests uses for JobAdDto: filter input is not output. "
            + "The PRIMARY protection against a personnummer in this column is upstream and is "
            + "not this omission: RecentJobSearchCaptureBehavior skips the whole capture when the "
            + "value is personnummer-shaped (#1411). This omission is defence in depth on top of "
            + "it. ADR 0087 D8(c) offers flagged, masked OR excluded; exclusion is what shipped, "
            + "and a masked or name-substituted projection remains open — it would restore "
            + "replayability without revealing an org.nr. Cost as it stands: a captured employer "
            + "search replays broader than it was (buildRecentSearchHref).",
    };

    [Fact]
    public void EverySearchDimension_ReachesTheReadProjection()
    {
        var projected = typeof(RecentJobSearchDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var dimensions = SearchDimensions().ToArray();

        // Floor against a broken source set: an inclusion spec can never detect that it is
        // measuring nothing. If SearchDimensions() ever comes back empty — allow-list widened,
        // properties no longer public, the entity restructured — `missing` is empty too and BOTH
        // facts pass green and silent.
        dimensions.ShouldNotBeEmpty(
            "the guard measures nothing if RecentJobSearch exposes no search dimensions");

        var missing = dimensions
            .Where(name => !NotSurfaced.ContainsKey(name))
            .Where(name => !projected.Contains(name) && !projected.Contains(name + "List"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        missing.ShouldBeEmpty(
            "every RecentJobSearch search dimension must reach RecentJobSearchDto, or be "
            + $"classified in NotSurfaced with its ground. Missing: {string.Join(", ", missing)}. "
            + "A dimension the projection omits cannot be replayed, so the row's count and the "
            + "list its link produces stop resting on the same criterion (#1407).");
    }

    [Fact]
    public void NotSurfaced_OnlyNamesDimensionsThatStillExist()
    {
        // A withheld dimension that has been removed leaves an entry whose ground nobody can
        // check, and which silently excuses a FUTURE property that happens to take the name.
        var dimensions = SearchDimensions().ToHashSet(StringComparer.Ordinal);

        var stale = NotSurfaced.Keys
            .Where(name => !dimensions.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        stale.ShouldBeEmpty(
            $"NotSurfaced names dimensions RecentJobSearch no longer has: {string.Join(", ", stale)}");
    }

    private static IEnumerable<string> SearchDimensions() =>
        typeof(RecentJobSearch)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !NotSearchDimensions.Contains(name));
}
