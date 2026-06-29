using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.ResolveSkillLabels;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.ResolveSkillLabels;

// ADR 0079 STEG 3 PR-C / #277 — the skill reverse-lookup handler drops unknown ids via
// ISkillResolver.ResolveLabels (graceful) then GROUPS the KNOWN ids by shared exact-label surface
// via ISkillResolver.GroupConceptIds (the shared SkillTaxonomyIndex). Unit tests pin the
// delegation + unknown-drop pre-filter + grouped DTO mapping; the real id→label resolution +
// surface grouping is covered by SkillSurfaceGroupingTests + SkillResolverIntegrationTests.
public class ResolveSkillLabelsQueryHandlerTests
{
    private readonly ISkillResolver _resolver = Substitute.For<ISkillResolver>();

    // CA1861 — hoisted matcher arrays (Arg.Is is evaluated repeatedly).
    private static readonly string[] JavaKnownId = ["skill_java"];
    private static readonly string[] CSharpTwinIds = ["esco_csharp", "af_csharp"];

    private ResolveSkillLabelsQueryHandler Sut() => new(_resolver);

    // The default grouping fake echoes each input id as a one-member group (canonical == label == id).
    private void EchoGroups() =>
        _resolver
            .GroupConceptIds(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ((IEnumerable<string>)ci[0])
                .Select(id => new ResolvedSkillGroup(id, id, [id]))
                .ToList());

    [Fact]
    public async Task Handle_DropsUnknown_ThenGroupsKnown_ToSkillOptionGroupDtos()
    {
        // ResolveLabels keeps only the known id; grouping then maps it to a one-member group.
        _resolver.ResolveLabels(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ResolvedSkill("skill_java", "Java")]);
        _resolver
            .GroupConceptIds(Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(JavaKnownId)),
                Arg.Any<CancellationToken>())
            .Returns([new ResolvedSkillGroup("skill_java", "Java", ["skill_java"])]);

        var result = await Sut().Handle(
            new ResolveSkillLabelsQuery(["skill_java"]), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].CanonicalConceptId.ShouldBe("skill_java");
        result[0].Label.ShouldBe("Java");
        result[0].MemberConceptIds.ShouldBe(["skill_java"]);
    }

    [Fact]
    public async Task Handle_CollapsesSavedTwinPair_IntoOneChip_CarryingBothMemberIds()
    {
        // #277 — a saved ESCO + AF twin-pair (both known) renders as ONE chip carrying both ids.
        _resolver.ResolveLabels(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new ResolvedSkill("esco_csharp", "C#"),
                new ResolvedSkill("af_csharp", "C#"),
            ]);
        _resolver
            .GroupConceptIds(Arg.Is<IEnumerable<string>>(ids => ids.SequenceEqual(CSharpTwinIds)),
                Arg.Any<CancellationToken>())
            .Returns([new ResolvedSkillGroup("esco_csharp", "C#", ["esco_csharp", "af_csharp"])]);

        var result = await Sut().Handle(
            new ResolveSkillLabelsQuery(["esco_csharp", "af_csharp"]), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].CanonicalConceptId.ShouldBe("esco_csharp");
        result[0].MemberConceptIds.ShouldBe(["esco_csharp", "af_csharp"]);
    }

    [Fact]
    public async Task Handle_WhenResolverReturnsEmpty_ReturnsEmpty()
    {
        _resolver.ResolveLabels(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        EchoGroups();

        var result = await Sut().Handle(
            new ResolveSkillLabelsQuery(["skill_unknown"]), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithNullConceptIds_ResolvesEmpty_DoesNotThrow()
    {
        _resolver.ResolveLabels(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        EchoGroups();

        var result = await Sut().Handle(
            new ResolveSkillLabelsQuery(null!), CancellationToken.None);

        result.ShouldBeEmpty();
    }
}
