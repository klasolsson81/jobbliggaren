using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.ResolveSkillLabels;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.ResolveSkillLabels;

// ADR 0079 STEG 3 PR-C — the skill reverse-lookup handler is a thin map over
// ISkillResolver.ResolveLabels (the shared SkillTaxonomyIndex). Unit tests pin the
// delegation + DTO mapping + graceful-null; the real id→label resolution + unknown-drop
// is covered by SkillResolverIntegrationTests.
public class ResolveSkillLabelsQueryHandlerTests
{
    private readonly ISkillResolver _resolver = Substitute.For<ISkillResolver>();

    private ResolveSkillLabelsQueryHandler Sut() => new(_resolver);

    [Fact]
    public async Task Handle_MapsResolvedLabels_ToSkillOptionDtos()
    {
        _resolver.ResolveLabels(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ResolvedSkill("skill_java", "Java")]);

        var result = await Sut().Handle(
            new ResolveSkillLabelsQuery(["skill_java"]), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].ConceptId.ShouldBe("skill_java");
        result[0].Label.ShouldBe("Java");
    }

    [Fact]
    public async Task Handle_WhenResolverReturnsEmpty_ReturnsEmpty()
    {
        _resolver.ResolveLabels(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await Sut().Handle(
            new ResolveSkillLabelsQuery(["skill_unknown"]), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithNullConceptIds_ResolvesEmpty_DoesNotThrow()
    {
        _resolver.ResolveLabels(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await Sut().Handle(
            new ResolveSkillLabelsQuery(null!), CancellationToken.None);

        result.ShouldBeEmpty();
    }
}
