using System.Reflection;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.RecentJobSearches.Queries.ListRecentSearches;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1413 — every term the geo predicate UNIONS must reach the recent-search ort label, or be
/// classified here as something else. The label names what a click will return; a term that
/// widens the result set but never reaches the label makes the label describe a strict subset,
/// which is the defect #1413 fixed twice over (distans, and kommun+län before it).
///
/// <para>
/// Nothing binds the two sides. The predicate lives in Infrastructure
/// (<c>JobAdSearchComposition</c>, #551 PR-B D5: "kommun ∨ län ∨ remote, aldrig ett eget
/// AND-Where"), the label in Application, and the port between them is flat —
/// <see cref="JobAdFilterCriteria"/> lists <c>Municipality</c>, <c>Region</c> and <c>Remote</c>
/// as siblings of the orthogonal axes, so the compiler sees no difference. A fourth geo term
/// would compile, widen the result set, and silently reintroduce the subset label.
/// </para>
///
/// <para>
/// Written in the shape <c>RecentJobSearchProjectionParityTests</c> established (<c>e4d11e73</c>,
/// same lane): SILENCE FAILS. A member added to the criteria and left unclassified fails the
/// first test, so the choice has to be made by a human rather than defaulted into.
/// </para>
/// </summary>
public class GeoUnionLabelParityTests
{
    // Terms the geo predicate ORs together. Each must be readable by the ort label.
    private static readonly HashSet<string> GeoUnion = new(StringComparer.Ordinal)
    {
        "Municipality", "Region", "Remote",
    };

    // Axes AND-ed against the geo predicate. They narrow rather than widen, so a label that
    // omits them still describes a superset of what the click returns — never a subset.
    // ADR 0067 Beslut 6 wired these as separate dimensions; #1418 owns whether a row carrying
    // only one of them deserves a label of its own.
    private static readonly HashSet<string> Orthogonal = new(StringComparer.Ordinal)
    {
        "OccupationGroup", "EmploymentType", "WorktimeExtent", "Employer", "Q",
    };

    [Fact]
    public void EveryFilterCriteriaMember_IsClassifiedAsGeoUnionOrOrthogonal()
    {
        var members = typeof(JobAdFilterCriteria)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToArray();

        var unclassified = members
            .Where(name => !GeoUnion.Contains(name) && !Orthogonal.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        unclassified.ShouldBeEmpty(
            "every JobAdFilterCriteria member must be classified as a geo-union term or an "
            + $"orthogonal axis. Unclassified: {string.Join(", ", unclassified)}. A geo-union "
            + "term that never reaches DeriveOrtLabel makes the recent-search label describe a "
            + "strict subset of what the click returns (#1413).");
    }

    [Fact]
    public void EveryGeoUnionTerm_IsReadableByTheOrtLabel()
    {
        var ortLabel = typeof(ListRecentSearchesQueryHandler)
            .GetMethod("DeriveOrtLabel", BindingFlags.NonPublic | BindingFlags.Static);

        ortLabel.ShouldNotBeNull(
            "DeriveOrtLabel is the ort label's only producer; if it was renamed or inlined this "
            + "guard stops measuring anything and must be re-pointed, not deleted.");

        // Parameters carry the term name, optionally suffixed "Labels" for the resolved forms.
        var covered = ortLabel!
            .GetParameters()
            .Select(p => p.Name!)
            .Select(n => n.EndsWith("Labels", StringComparison.Ordinal)
                ? n[..^"Labels".Length]
                : n)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unreachable = GeoUnion
            .Where(term => !covered.Contains(term))
            .OrderBy(term => term, StringComparer.Ordinal)
            .ToArray();

        unreachable.ShouldBeEmpty(
            "every geo-union term must be a parameter of DeriveOrtLabel, else the label cannot "
            + $"name it. Unreachable: {string.Join(", ", unreachable)}. The predicate would still "
            + "union the term, so the label would silently name a strict subset (#1413).");
    }

    // The classification must FORBID, not merely excuse: without this, moving a genuine geo term
    // into Orthogonal buys a green build for exactly the defect the first test exists to catch.
    [Fact]
    public void OrthogonalAxes_AreNotReadableByTheOrtLabel()
    {
        var ortLabel = typeof(ListRecentSearchesQueryHandler)
            .GetMethod("DeriveOrtLabel", BindingFlags.NonPublic | BindingFlags.Static);

        var covered = ortLabel!
            .GetParameters()
            .Select(p => p.Name!)
            .Select(n => n.EndsWith("Labels", StringComparison.Ordinal)
                ? n[..^"Labels".Length]
                : n)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var leaked = Orthogonal
            .Where(axis => covered.Contains(axis))
            .OrderBy(axis => axis, StringComparer.Ordinal)
            .ToArray();

        leaked.ShouldBeEmpty(
            "an axis classified as orthogonal must not reach the ort label. Leaked: "
            + $"{string.Join(", ", leaked)}. Either the classification is wrong or the axis "
            + "genuinely joined the geo union, and then it belongs in GeoUnion with the "
            + "predicate changed to match.");
    }
}
