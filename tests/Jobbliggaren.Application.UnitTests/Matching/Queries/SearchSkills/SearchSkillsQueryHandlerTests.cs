using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.SearchSkills;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.SearchSkills;

// ADR 0079 STEG 3 PR-C — the skill-typeahead query handler is a thin map over
// ISkillResolver.Search (the shared SkillTaxonomyIndex). These unit tests pin the
// contract (delegation + DTO mapping + graceful empty); the real taxonomy ranking/
// substring behaviour is covered by SkillResolverIntegrationTests.
public class SearchSkillsQueryHandlerTests
{
    private readonly ISkillResolver _resolver = Substitute.For<ISkillResolver>();

    private SearchSkillsQueryHandler Sut() => new(_resolver);

    [Fact]
    public async Task Handle_MapsResolverResults_ToSkillOptionDtos_InOrder()
    {
        _resolver.Search("jav", Arg.Any<CancellationToken>()).Returns(
        [
            new ResolvedSkill("skill_java", "Java"),
            new ResolvedSkill("skill_js", "JavaScript"),
        ]);

        var result = await Sut().Handle(new SearchSkillsQuery("jav"), CancellationToken.None);

        result.Count.ShouldBe(2);
        result[0].ShouldBe(new SkillOptionDto("skill_java", "Java"));
        result[1].ShouldBe(new SkillOptionDto("skill_js", "JavaScript"));
    }

    [Fact]
    public async Task Handle_WhenResolverReturnsEmpty_ReturnsEmpty()
    {
        _resolver.Search(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await Sut().Handle(new SearchSkillsQuery(""), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_PassesTheQueryStringThroughToTheResolver()
    {
        _resolver.Search(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);

        await Sut().Handle(new SearchSkillsQuery("docker"), CancellationToken.None);

        _resolver.Received(1).Search("docker", Arg.Any<CancellationToken>());
    }
}
