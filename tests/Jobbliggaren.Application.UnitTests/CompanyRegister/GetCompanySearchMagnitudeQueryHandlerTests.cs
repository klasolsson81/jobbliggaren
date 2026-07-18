using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyRegister.Queries.GetCompanySearchMagnitude;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 company-search wave — <see cref="GetCompanySearchMagnitudeQueryHandler"/>. The honest
/// headline count: exact below the ceiling, SATURATED at it (the copy must say "10 000+"). The
/// handler counts through the port with the surface's OWN single-sourced ceiling.
/// </summary>
public class GetCompanySearchMagnitudeQueryHandlerTests
{
    private static GetCompanySearchMagnitudeQuery AllAxesAbsent() => new(null, null, null, null);

    [Fact]
    public async Task Handle_BelowCeiling_IsExact_AndPassesTheSingleSourcedCeilingToThePort()
    {
        var ct = TestContext.Current.CancellationToken;
        int? ceilingSeen = null;
        var port = Substitute.For<ICompanyRegisterSearchQuery>();
        port.CountMatchingAsync(
                Arg.Any<CompanyRegisterSearchCriteria>(),
                Arg.Do<int>(c => ceilingSeen = c),
                Arg.Any<CancellationToken>())
            .Returns(CompanySearchMagnitudeDto.Ceiling - 1); // 9 999

        var result = await new GetCompanySearchMagnitudeQueryHandler(port)
            .Handle(AllAxesAbsent(), ct);

        result.Magnitude.ShouldBe(9_999);
        result.Saturated.ShouldBeFalse();
        // The ceiling is the DTO's own constant, never a hardcoded call-site literal.
        ceilingSeen.ShouldBe(CompanySearchMagnitudeDto.Ceiling);
    }

    [Fact]
    public async Task Handle_AtTheCeiling_IsSaturated()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = Substitute.For<ICompanyRegisterSearchQuery>();
        port.CountMatchingAsync(
                Arg.Any<CompanyRegisterSearchCriteria>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(CompanySearchMagnitudeDto.Ceiling); // 10 000

        var result = await new GetCompanySearchMagnitudeQueryHandler(port)
            .Handle(AllAxesAbsent(), ct);

        result.Magnitude.ShouldBe(CompanySearchMagnitudeDto.Ceiling);
        result.Saturated.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_InvalidInput_ThrowsTheUnreachableGuard_AndNeverCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = Substitute.For<ICompanyRegisterSearchQuery>();

        // Paging is fixed 1/1 by the handler, so the only reachable Create failure is an axis error
        // — here a personnummer-shaped org.nr. In production ValidationBehavior 400s first; reaching
        // the handler is validator/normalizer drift → fail loud (500), never a fabricated count.
        var act = async () =>
        {
            await new GetCompanySearchMagnitudeQueryHandler(port).Handle(
                new GetCompanySearchMagnitudeQuery(null, null, null, "5501012345"), ct);
        };

        await act.ShouldThrowAsync<InvalidOperationException>();
        await port.DidNotReceiveWithAnyArgs()
            .CountMatchingAsync(default!, default, CancellationToken.None);
    }
}
