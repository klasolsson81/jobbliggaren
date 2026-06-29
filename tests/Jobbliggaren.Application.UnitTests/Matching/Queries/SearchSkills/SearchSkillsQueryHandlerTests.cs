using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.SearchSkills;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.SearchSkills;

// ADR 0079 STEG 3 PR-C / #277 — the skill-typeahead query handler resolves ranked hits via
// ISkillResolver.Search then GROUPS them by shared exact-label surface via
// ISkillResolver.GroupConceptIds (the shared SkillTaxonomyIndex). These unit tests pin the
// contract (delegation + rank-preserving group feed + DTO mapping + graceful empty); the real
// taxonomy ranking/substring + surface grouping is covered by SkillSurfaceGroupingTests +
// SkillResolverIntegrationTests.
public class SearchSkillsQueryHandlerTests
{
    private readonly ISkillResolver _resolver = Substitute.For<ISkillResolver>();

    // CA1861 — hoisted matcher arrays (Arg.Is is evaluated repeatedly).
    private static readonly string[] JavRankedIds = ["skill_java", "skill_js"];
    private static readonly string[] CSharpTwinIds = ["esco_csharp", "af_csharp"];

    private SearchSkillsQueryHandler Sut() => new(_resolver);

    // The default grouping fake echoes each input id as a one-member group (canonical == label == id).
    private void EchoGroups() =>
        _resolver
            .GroupConceptIds(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IEnumerable<string>)ci[0])
                .Select(id => new ResolvedSkillGroup(id, id, [id]))
                .ToList());

    [Fact]
    public async Task Handle_MapsGroupedResults_ToSkillOptionGroupDtos_InRankOrder()
    {
        _resolver.Search("jav", Arg.Any<CancellationToken>()).Returns(
        [
            new ResolvedSkill("skill_java", "Java"),
            new ResolvedSkill("skill_js", "JavaScript"),
        ]);
        // Grouping is fed the ranked ids in order → groups preserve the rank.
        _resolver
            .GroupConceptIds(Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(JavRankedIds)),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new ResolvedSkillGroup("skill_java", "Java", ["skill_java"]),
                new ResolvedSkillGroup("skill_js", "JavaScript", ["skill_js"]),
            ]);

        var result = await Sut().Handle(new SearchSkillsQuery("jav"), CancellationToken.None);

        // Compare by field — record equality over the IReadOnlyList member is reference-based.
        result.Count.ShouldBe(2);
        result[0].CanonicalConceptId.ShouldBe("skill_java");
        result[0].Label.ShouldBe("Java");
        result[0].MemberConceptIds.ShouldBe(["skill_java"]);
        result[1].CanonicalConceptId.ShouldBe("skill_js");
        result[1].Label.ShouldBe("JavaScript");
        result[1].MemberConceptIds.ShouldBe(["skill_js"]);
    }

    [Fact]
    public async Task Handle_CollapsesTwinHits_IntoOneOption_CarryingBothMemberIds()
    {
        // #277 — a search for "C#" returns both ESCO + AF twin hits; grouping collapses them to ONE
        // addable option carrying both member ids (the canonical preferred-first label shows).
        _resolver.Search("C#", Arg.Any<CancellationToken>()).Returns(
        [
            new ResolvedSkill("esco_csharp", "C#"),
            new ResolvedSkill("af_csharp", "C#"),
        ]);
        _resolver
            .GroupConceptIds(Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(CSharpTwinIds)),
                Arg.Any<CancellationToken>())
            .Returns([new ResolvedSkillGroup("esco_csharp", "C#", ["esco_csharp", "af_csharp"])]);

        var result = await Sut().Handle(new SearchSkillsQuery("C#"), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].CanonicalConceptId.ShouldBe("esco_csharp");
        result[0].Label.ShouldBe("C#");
        result[0].MemberConceptIds.ShouldBe(["esco_csharp", "af_csharp"]);
    }

    [Fact]
    public async Task Handle_WhenResolverReturnsEmpty_ReturnsEmpty()
    {
        _resolver.Search(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        EchoGroups();

        var result = await Sut().Handle(new SearchSkillsQuery(""), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_PassesTheQueryStringThroughToTheResolver()
    {
        _resolver.Search(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        EchoGroups();

        await Sut().Handle(new SearchSkillsQuery("docker"), CancellationToken.None);

        _resolver.Received(1).Search("docker", Arg.Any<CancellationToken>());
    }
}
