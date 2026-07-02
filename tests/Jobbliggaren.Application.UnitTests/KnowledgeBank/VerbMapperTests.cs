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

    [Fact]
    public void GetVerbMapping_ShouldMarkExactlyTheTwoSameValencyPairsAsDropInSafe_WhenCalled()
    {
        // #494 drift-guard: only pairs with the SAME valency/rection may be a literal drop-in the
        // improve engine proposes — {"var ansvarig för", "hade hand om"} → "ansvarade för". Every
        // other pair is a double finite verb or a role-overreach (ADR 0071 no-invented-
        // qualifications) and must stay dropInSafe=false (flagged by A2, never rewritten). Pinned
        // by SET so a future asset edit that opts an overreach pair in fails CI.
        var mapping = LoadMapping();

        mapping.WeakVerbs
            .Where(w => w.DropInSafe)
            .Select(w => w.Weak)
            .ShouldBe(["var ansvarig för", "hade hand om"], ignoreOrder: true);
    }

    [Fact]
    public void GetVerbMapping_ShouldKeepEveryDropInSafeSuggestionAmongTheStrongVerbs_WhenCalled()
    {
        // A drop-in-safe pair still obeys the closure invariant (its suggestion is an endorsed
        // strong verb) — belt-and-suspenders over the general closure test above.
        var mapping = LoadMapping();
        var strong = mapping.StrongVerbGroups
            .SelectMany(g => g.Verbs)
            .Select(v => v.Trim().ToLowerInvariant())
            .ToHashSet();

        mapping.WeakVerbs
            .Where(w => w.DropInSafe)
            .ShouldAllBe(w => strong.Contains(w.SuggestedStrong.Trim().ToLowerInvariant()));
    }

    [Fact]
    public void WeakVerbFile_ShouldDefaultDropInSafeToFalse_WhenTheAssetOmitsTheField()
    {
        // #494 N-1 back-compat (house rule: a missing JSON key mapping to a CLR default MUST carry
        // a back-compat test). An older asset without dropInSafe deserialises to DropInSafe=false —
        // never a drop-in until an asset explicitly opts a pair in (fail-closed, privacy/no-synthesis
        // by default).
        var legacy = System.Text.Json.JsonSerializer.Deserialize<VerbMappingFile.WeakVerbFile>(
            """{ "weak": "var ansvarig för", "suggestedStrong": "ansvarade för" }""",
            KnowledgeBankJson.Options);

        legacy.ShouldNotBeNull();
        legacy!.DropInSafe.ShouldBeFalse();
    }
}
