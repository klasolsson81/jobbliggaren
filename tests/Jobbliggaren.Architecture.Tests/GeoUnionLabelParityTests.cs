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

    // Terms the Yrke predicate ORs together (#2199). Each must be readable by the Yrke label.
    private static readonly HashSet<string> YrkeUnion = new(StringComparer.Ordinal)
    {
        "OccupationGroup", "OccupationName",
    };

    // Axes AND-ed against both unions. They narrow rather than widen, so a label that
    // omits them still describes a superset of what the click returns — never a subset.
    // #1418 owns whether a row carrying only one of them deserves a label of its own.
    private static readonly HashSet<string> Orthogonal = new(StringComparer.Ordinal)
    {
        "EmploymentType", "WorktimeExtent", "Employer", "Q",
    };

    [Fact]
    public void EveryFilterCriteriaMember_IsClassifiedExactlyOnce()
    {
        var members = typeof(JobAdFilterCriteria)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToArray();

        // Floor against a broken source set: an inclusion spec cannot detect that it is measuring
        // nothing. If the reflection ever comes back empty — record restructured, members no longer
        // public — `unclassified` is empty too and this passes green and silent.
        members.ShouldNotBeEmpty(
            "the guard measures nothing if JobAdFilterCriteria exposes no members");

        var misclassified = members
            .Where(name => (GeoUnion.Contains(name) ? 1 : 0) + (YrkeUnion.Contains(name) ? 1 : 0)
                + (Orthogonal.Contains(name) ? 1 : 0) != 1)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        misclassified.ShouldBeEmpty(
            "every JobAdFilterCriteria member must be classified as exactly one of a geo-union "
            + "term, a Yrke-union term or an orthogonal axis. Misclassified: "
            + $"{string.Join(", ", misclassified)}. A union term that never reaches its label "
            + "makes the recent-search label describe a strict subset of what the click returns "
            + "(#1413).");
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

        ortLabel.ShouldNotBeNull(
            "DeriveOrtLabel is the ort label's only producer; if it was renamed or inlined this "
            + "guard stops measuring anything and must be re-pointed, not deleted.");

        var covered = ortLabel!
            .GetParameters()
            .Select(p => p.Name!)
            .Select(n => n.EndsWith("Labels", StringComparison.Ordinal)
                ? n[..^"Labels".Length]
                : n)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var leaked = Orthogonal.Concat(YrkeUnion)
            .Where(axis => covered.Contains(axis))
            .OrderBy(axis => axis, StringComparer.Ordinal)
            .ToArray();

        leaked.ShouldBeEmpty(
            "an axis classified as orthogonal must not reach the ort label. Leaked: "
            + $"{string.Join(", ", leaked)}. Either the classification is wrong or the axis "
            + "genuinely joined the geo union, and then it belongs in GeoUnion with the "
            + "predicate changed to match.");
    }

    // #2199 PR 2 — the same defect class on the Yrke axis. The search port ORs OccupationName with
    // OccupationGroup (JobAdSearchComposition.ApplyFilter), so a name pick widens a group
    // selection; a Yrke label that never reads the names would name a strict subset of what the
    // click returns. Both terms must reach DeriveYrkeLabel, the Yrke label's only producer.
    [Fact]
    public void EveryYrkeUnionTerm_IsReadableByTheYrkeLabel()
    {
        var yrkeLabel = typeof(ListRecentSearchesQueryHandler)
            .GetMethod("DeriveYrkeLabel", BindingFlags.NonPublic | BindingFlags.Static);

        yrkeLabel.ShouldNotBeNull(
            "DeriveYrkeLabel is the Yrke label's only producer; if it was renamed or inlined this "
            + "guard stops measuring anything and must be re-pointed, not deleted.");

        var covered = yrkeLabel!
            .GetParameters()
            .Select(p => p.Name!)
            .Select(n => n.EndsWith("Labels", StringComparison.Ordinal)
                ? n[..^"Labels".Length]
                : n)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unreachable = YrkeUnion
            .Where(term => !covered.Contains(term))
            .OrderBy(term => term, StringComparer.Ordinal)
            .ToArray();

        unreachable.ShouldBeEmpty(
            "every Yrke-union term must be a parameter of DeriveYrkeLabel, else the label cannot "
            + $"name it. Unreachable: {string.Join(", ", unreachable)}. The predicate would still "
            + "OR the term, so the label would silently name a strict subset (#1413, #2199).");
    }

    [Fact]
    public void NonYrkeAxes_AreNotReadableByTheYrkeLabel()
    {
        var yrkeLabel = typeof(ListRecentSearchesQueryHandler)
            .GetMethod("DeriveYrkeLabel", BindingFlags.NonPublic | BindingFlags.Static);

        yrkeLabel.ShouldNotBeNull(
            "DeriveYrkeLabel is the Yrke label's only producer; if it was renamed or inlined this "
            + "guard stops measuring anything and must be re-pointed, not deleted.");

        var covered = yrkeLabel!
            .GetParameters()
            .Select(p => p.Name!)
            .Select(n => n.EndsWith("Labels", StringComparison.Ordinal)
                ? n[..^"Labels".Length]
                : n)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var leaked = GeoUnion.Concat(Orthogonal)
            .Where(axis => covered.Contains(axis))
            .OrderBy(axis => axis, StringComparer.Ordinal)
            .ToArray();

        leaked.ShouldBeEmpty(
            "an axis classified outside the Yrke union must not reach the Yrke label. Leaked: "
            + $"{string.Join(", ", leaked)}. Either the classification is wrong or the axis "
            + "genuinely joined the Yrke union, and then it belongs in YrkeUnion with the "
            + "predicate changed to match.");
    }
}
