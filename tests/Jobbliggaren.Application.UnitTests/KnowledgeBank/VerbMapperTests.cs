using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4 STEG 7 (F4-7) — the committed verb mapping (verb-mapping.v1.json) loads
/// through the real <see cref="VerbMapper"/>. Strong/weak action-verb data is
/// VERSIONED DATA (CLAUDE.md §5: "action-verb lists ... versioned data/config ... not
/// inline strings"). The closure invariant is the load-bearing one: every weak verb's
/// suggested replacement must EXIST among the strong verbs — otherwise the propose
/// step would point at a verb the knowledge bank does not actually endorse.
///
/// RED until VerbMapper ships internal sealed in Jobbliggaren.Infrastructure.KnowledgeBank.
/// </summary>
public class VerbMapperTests
{
    private static VerbMapping LoadMapping() => new VerbMapper().GetVerbMapping();

    [Fact]
    public void GetVerbMapping_ShouldLoadVersionedEmbeddedResource_WhenCalled()
    {
        var mapping = LoadMapping();

        mapping.ShouldNotBeNull();
        mapping.Version.ShouldNotBeNullOrWhiteSpace();
        mapping.Version.ShouldNotBe("unknown");
    }

    [Fact]
    public void GetVerbMapping_ShouldContainSevenStrongGroups_WhenCalled()
    {
        // Architect: exactly 7 strong-verb groups (a curated, stable taxonomy of verb
        // families). Pinned exact — the group taxonomy is a deliberate fixed structure,
        // not a growing corpus.
        var mapping = LoadMapping();

        mapping.StrongVerbGroups.Count.ShouldBe(7);
        mapping.StrongVerbGroups.ShouldAllBe(g =>
            !string.IsNullOrWhiteSpace(g.Group) && g.Verbs.Count > 0);
    }

    [Fact]
    public void GetVerbMapping_ShouldHaveAtLeastFortyFiveStrongVerbs_WhenCalled()
    {
        // Drift-robust floor (architect: >=45 strong verbs total across the groups).
        var mapping = LoadMapping();

        var totalStrong = mapping.StrongVerbGroups.Sum(g => g.Verbs.Count);
        totalStrong.ShouldBeGreaterThanOrEqualTo(45);
    }

    [Fact]
    public void GetVerbMapping_ShouldHaveExactlyEightWeakVerbs_WhenCalled()
    {
        // Architect: exactly 8 weak verbs (a small, deliberate "avoid these" set).
        var mapping = LoadMapping();

        mapping.WeakVerbs.Count.ShouldBe(8);
    }

    [Fact]
    public void GetVerbMapping_ShouldHaveNonEmptySuggestedStrongOnEveryWeakVerb_WhenCalled()
    {
        var mapping = LoadMapping();

        mapping.WeakVerbs.ShouldAllBe(w =>
            !string.IsNullOrWhiteSpace(w.Weak)
            && !string.IsNullOrWhiteSpace(w.SuggestedStrong));
    }

    [Fact]
    public void GetVerbMapping_ShouldHaveEveryWeakVerbSuggestionExistAmongStrongVerbs_WhenCalled()
    {
        // CLOSURE invariant (load-bearing): each weak verb's SuggestedStrong must be a
        // verb the knowledge bank actually lists as strong — otherwise the propose step
        // would recommend an unendorsed verb. Case-insensitive membership across all
        // groups' verbs.
        var mapping = LoadMapping();

        var strongVerbs = mapping.StrongVerbGroups
            .SelectMany(g => g.Verbs)
            .Select(v => v.Trim().ToLowerInvariant())
            .ToHashSet();

        foreach (var weak in mapping.WeakVerbs)
        {
            strongVerbs.ShouldContain(
                weak.SuggestedStrong.Trim().ToLowerInvariant(),
                $"Svaga verbet '{weak.Weak}' föreslår '{weak.SuggestedStrong}' som inte " +
                "finns bland de starka verben (closure-invariant bruten).");
        }
    }

    [Fact]
    public void GetVerbMapping_ShouldHaveWeakVerbGroupReferenceAValidGroupWhenSet_WhenCalled()
    {
        // Optional Group on a weak verb (nullable). When set, it must reference one of
        // the declared strong-verb group names — a dangling group reference would be a
        // data bug. Null is allowed (ungrouped weak verb).
        var mapping = LoadMapping();

        var groupNames = mapping.StrongVerbGroups
            .Select(g => g.Group.Trim().ToLowerInvariant())
            .ToHashSet();

        mapping.WeakVerbs
            .Where(w => w.Group != null)
            .ShouldAllBe(w => groupNames.Contains(w.Group!.Trim().ToLowerInvariant()));
    }

    [Fact]
    public void GetVerbMapping_ShouldHaveUniqueWeakVerbs_WhenCalled()
    {
        var mapping = LoadMapping();

        var weak = mapping.WeakVerbs
            .Select(w => w.Weak.Trim().ToLowerInvariant())
            .ToList();
        weak.Distinct().Count().ShouldBe(weak.Count);
    }
}
