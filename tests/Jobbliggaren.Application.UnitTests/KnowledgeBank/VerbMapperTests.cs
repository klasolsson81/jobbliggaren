using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4 STEG 7 (F4-7) — the committed verb mapping (verb-mapping.sv.v1.json) loads
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
    private static VerbMapping LoadMapping() => new VerbMapper().GetVerbMapping(ResumeLanguage.Sv);

    private static VerbMapping LoadMapping(string language) =>
        new VerbMapper().GetVerbMapping(ResumeLanguage.FromName(language));

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var language in ResumeLanguage.List)
        {
            data.Add(language.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldLoadVersionedEmbeddedResource_WhenCalled(string language)
    {
        var mapping = LoadMapping(language);

        mapping.ShouldNotBeNull();
        mapping.Version.ShouldNotBeNullOrWhiteSpace();
        mapping.Version.ShouldNotBe("unknown");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldContainSevenStrongGroups_WhenCalled(string language)
    {
        // Architect: exactly 7 strong-verb groups (a curated, stable taxonomy of verb
        // families). Pinned exact — the group taxonomy is a deliberate fixed structure,
        // not a growing corpus.
        var mapping = LoadMapping(language);

        mapping.StrongVerbGroups.Count.ShouldBe(7);
        mapping.StrongVerbGroups.ShouldAllBe(g =>
            !string.IsNullOrWhiteSpace(g.Group) && g.Verbs.Count > 0);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldHaveAtLeastFortyFiveStrongVerbs_WhenCalled(string language)
    {
        // Drift-robust floor (architect: >=45 strong verbs total across the groups).
        var mapping = LoadMapping(language);

        var totalStrong = mapping.StrongVerbGroups.Sum(g => g.Verbs.Count);
        totalStrong.ShouldBeGreaterThanOrEqualTo(45);
    }

    [Theory]
    [InlineData("Sv", 60, 8)]
    [InlineData("En", 332, 12)]
    public void GetVerbMapping_ShouldHoldItsCuratedCount_PerLanguage(string language, int strong, int weak)
    {
        var mapping = LoadMapping(language);

        mapping.StrongVerbGroups.Sum(g => g.Verbs.Count).ShouldBe(strong);
        mapping.WeakVerbs.Count.ShouldBe(weak);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldHaveUniqueStrongVerbs_WhenCalled(string language)
    {
        var strong = LoadMapping(language).StrongVerbGroups
            .SelectMany(g => g.Verbs)
            .Select(v => v.Trim().ToLowerInvariant())
            .ToList();

        strong.Distinct().Count().ShouldBe(strong.Count);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldHaveNonEmptySuggestedStrongOnEveryWeakVerb_WhenCalled(string language)
    {
        var mapping = LoadMapping(language);

        mapping.WeakVerbs.ShouldAllBe(w =>
            !string.IsNullOrWhiteSpace(w.Weak)
            && !string.IsNullOrWhiteSpace(w.SuggestedStrong));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldHaveEveryWeakVerbSuggestionExistAmongStrongVerbs_WhenCalled(string language)
    {
        // CLOSURE invariant (load-bearing): each weak verb's SuggestedStrong must be a
        // verb the knowledge bank actually lists as strong — otherwise the propose step
        // would recommend an unendorsed verb. Case-insensitive membership across all
        // groups' verbs.
        var mapping = LoadMapping(language);

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

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldHaveWeakVerbGroupReferenceAValidGroupWhenSet_WhenCalled(string language)
    {
        // Optional Group on a weak verb (nullable). When set, it must reference one of
        // the declared strong-verb group names — a dangling group reference would be a
        // data bug. Null is allowed (ungrouped weak verb).
        var mapping = LoadMapping(language);

        var groupNames = mapping.StrongVerbGroups
            .Select(g => g.Group.Trim().ToLowerInvariant())
            .ToHashSet();

        mapping.WeakVerbs
            .Where(w => w.Group != null)
            .ShouldAllBe(w => groupNames.Contains(w.Group!.Trim().ToLowerInvariant()));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldHaveUniqueWeakVerbs_WhenCalled(string language)
    {
        var mapping = LoadMapping(language);

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

    [Theory]
    [MemberData(nameof(Languages))]
    public void GetVerbMapping_ShouldKeepEveryDropInSafeSuggestionAmongTheStrongVerbs_WhenCalled(string language)
    {
        // A drop-in-safe pair still obeys the closure invariant (its suggestion is an endorsed
        // strong verb) — belt-and-suspenders over the general closure test above.
        var mapping = LoadMapping(language);
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
    // ── #2341: one mapping per CV language ───────────────────────────────

    private const string SwedishResource = "Jobbliggaren.Infrastructure.KnowledgeBank.verb-mapping.sv.v1.json";

    [Fact]
    public void GetVerbMapping_ShouldServeEveryResumeLanguage_EachFromItsOwnAsset()
    {
        var mapper = new VerbMapper();

        foreach (var language in ResumeLanguage.List)
        {
            mapper.GetVerbMapping(language).Version.ShouldNotBe("unknown");
        }

        mapper.GetVerbMapping(ResumeLanguage.Sv).Source.ShouldBe("verb-mapping");
        mapper.GetVerbMapping(ResumeLanguage.Sv).Version.ShouldBe("1.1");
        mapper.GetVerbMapping(ResumeLanguage.Sv).StrongVerbMatch.ShouldBe(StrongVerbMatch.Stem);
        mapper.GetVerbMapping(ResumeLanguage.En).Source.ShouldBe("verb-mapping.en");
        mapper.GetVerbMapping(ResumeLanguage.En).Version.ShouldBe("1.0");
        mapper.GetVerbMapping(ResumeLanguage.En).StrongVerbMatch.ShouldBe(StrongVerbMatch.ListedForm);
    }

    [Fact]
    public void GetVerbMapping_English_ShouldOfferOnlyTheSameValencyPairAsADropIn()
    {
        // The drop-in is written into the CV verbatim, and the closure test accepts any listed form,
        // so the suggested form itself is pinned.
        var english = new VerbMapper().GetVerbMapping(ResumeLanguage.En);

        var dropIn = english.WeakVerbs.Where(w => w.DropInSafe).ToList().ShouldHaveSingleItem();
        dropIn.Weak.ShouldBe("was responsible for");
        dropIn.SuggestedStrong.ShouldBe("managed");
    }

    [Fact]
    public void VerbMatch_ShouldDefaultToStem_WhenTheAssetOmitsTheKey()
    {
        // House rule: a JSON key mapping to a CLR default carries a back-compat test. The Swedish
        // asset predates the key.
        KnowledgeBankTokens.VerbMatch(null).ShouldBe(StrongVerbMatch.Stem);

        var legacy = System.Text.Json.JsonSerializer.Deserialize<VerbMappingFile>(
            """{ "verbMappingVersion": "1.1", "strongVerbGroups": [], "weakVerbs": [] }""",
            KnowledgeBankJson.Options);
        legacy.ShouldNotBeNull();
        legacy!.StrongVerbMatch.ShouldBeNull();
    }

    [Fact]
    public void VerbMatch_ShouldFailLoud_WhenTheTokenIsUnknown()
    {
        Should.Throw<InvalidOperationException>(() => KnowledgeBankTokens.VerbMatch("forms"));
    }

    [Fact]
    public void Construction_ShouldFail_WhenAResumeLanguageHasNoMapping()
    {
        var onlySwedish = new Dictionary<ResumeLanguage, KnowledgeBankAsset>
        {
            [ResumeLanguage.Sv] = new("verb-mapping", SwedishResource),
        };

        Should.Throw<InvalidOperationException>(() => new VerbMapper(onlySwedish))
            .Message.ShouldContain(ResumeLanguage.En.Name);
    }

    [Fact]
    public void Construction_ShouldFail_WhenALanguagesListIsNotEmbedded()
    {
        var unembedded = new Dictionary<ResumeLanguage, KnowledgeBankAsset>
        {
            [ResumeLanguage.Sv] = new("verb-mapping", SwedishResource),
            [ResumeLanguage.En] = new("verb-mapping.en", "Jobbliggaren.Infrastructure.KnowledgeBank.verb-mapping.xx.v1.json"),
        };

        Should.Throw<InvalidOperationException>(() => new VerbMapper(unembedded));
    }
}
