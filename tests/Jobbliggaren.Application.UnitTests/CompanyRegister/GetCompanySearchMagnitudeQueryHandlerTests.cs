using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyRegister.Queries.GetCompanySearchMagnitude;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 company-search wave — <see cref="GetCompanySearchMagnitudeQueryHandler"/>. The honest
/// headline count: exact below the ceiling, SATURATED at it (the copy must say "10 000+"). The
/// handler counts through the port with the surface's OWN single-sourced ceiling.
///
/// <para>
/// #1149 — and NO count at all for an unfiltered browse-all, which is why every counting case
/// below names an axis. The fixture used to be axis-free, so the two counting tests were in fact
/// exercising the browse-all path; once that path stopped counting they could no longer be read
/// as "the handler counts". A test whose premise is the case it is not about is a test that
/// changes meaning silently.
/// </para>
/// </summary>
public class GetCompanySearchMagnitudeQueryHandlerTests
{
    /// <summary>A no-axis query — the legal browse-all the FE sends for an unfiltered search.</summary>
    private static GetCompanySearchMagnitudeQuery Unfiltered() => new(null, null, null, null);

    /// <summary>One axis present — an ordinary filtered search (the SNI leaf for IT consultancy).</summary>
    private static GetCompanySearchMagnitudeQuery Filtered() => new(["62010"], null, null, null);

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
            .Handle(Filtered(), ct);

        result.ShouldNotBeNull();
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
            .Handle(Filtered(), ct);

        result.ShouldNotBeNull();
        result.Magnitude.ShouldBe(CompanySearchMagnitudeDto.Ceiling);
        result.Saturated.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_Unfiltered_ReturnsNoMagnitude_AndNeverCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = Substitute.For<ICompanyRegisterSearchQuery>();

        var result = await new GetCompanySearchMagnitudeQueryHandler(port)
            .Handle(Unfiltered(), ct);

        // NULL is the browse-all contract, not a degradation: the honest number for the whole
        // active register is one the product ceiling can only render as "10 000+", and Klas ruled
        // (2026-08-01) that no number beats a saturated one. NULL and zero are different
        // statements — zero would mean "nothing matches".
        result.ShouldBeNull();

        // And the count is not merely discarded — it is never asked for. Asserting only the null
        // would be satisfied by a handler that still pays for the query, which is the cost the
        // ruling exists to avoid.
        await port.DidNotReceiveWithAnyArgs()
            .CountMatchingAsync(default!, default, CancellationToken.None);
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
