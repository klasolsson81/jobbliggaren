using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyRegister.Queries.SearchCompanies;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 company-search wave — <see cref="SearchCompaniesQueryHandler"/>. Thin by design:
/// <c>Create</c> (the single normalizer) → the faked port → the DTO mask. The register is not on
/// <c>IAppDbContext</c> (DPIA C-D4), so the handler is fully unit-testable with only the port
/// substituted; the port's real SQL semantics are proven against real Postgres elsewhere.
/// </summary>
public class SearchCompaniesQueryHandlerTests
{
    // "5560125790": third digit 6 >= 2 → a legal AB, kept verbatim.
    // "5501012345": third digit 0 < 2 → personnummer-shaped, masked + flagged.
    private const string LegalAbOrgNr = "5560125790";
    private const string PnrShapedOrgNr = "5501012345";

    [Fact]
    public async Task Handle_ValidQuery_MapsRows_MaskingOnlyThePersonnummerShapedOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = Substitute.For<ICompanyRegisterSearchQuery>();
        port.SearchAsync(Arg.Any<CompanyRegisterSearchCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<CompanyBrowseResult>(
                [
                    new CompanyBrowseResult(LegalAbOrgNr, "Volvo AB", "0180", "Stockholm", ["62010"]),
                    new CompanyBrowseResult(PnrShapedOrgNr, "Enskild Firma", "1480", "Göteborg", ["62020"]),
                ],
                totalCount: 2, page: 1, pageSize: 20));

        var result = await new SearchCompaniesQueryHandler(port)
            .Handle(new SearchCompaniesQuery(null, null, null, null, Page: 1, PageSize: 20), ct);

        // Pagination envelope passes straight through.
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(2);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
        result.Items.Count.ShouldBe(2);

        // The legal AB keeps its identity.
        var ab = result.Items[0];
        ab.OrganizationNumber.ShouldBe(LegalAbOrgNr);
        ab.IsProtectedIdentity.ShouldBeFalse();
        ab.Name.ShouldBe("Volvo AB");

        // The personnummer-shaped row is masked (null org.nr) + flagged — the rest still renders.
        var pnr = result.Items[1];
        pnr.OrganizationNumber.ShouldBeNull();
        pnr.IsProtectedIdentity.ShouldBeTrue();
        pnr.Name.ShouldBe("Enskild Firma");
    }

    [Fact]
    public async Task Handle_PassesTheNormalizedCriteriaToThePort_NotTheRawQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        CompanyRegisterSearchCriteria? captured = null;
        var port = Substitute.For<ICompanyRegisterSearchQuery>();
        port.SearchAsync(
                Arg.Do<CompanyRegisterSearchCriteria>(c => captured = c),
                Arg.Any<CancellationToken>())
            .Returns(new PagedResult<CompanyBrowseResult>([], totalCount: 0, page: 1, pageSize: 20));

        await new SearchCompaniesQueryHandler(port).Handle(
            new SearchCompaniesQuery(
                SniCodes: ["  62010  "], MunicipalityCodes: null,
                Name: " Volvo ", OrganizationNumber: "556012-5790", Page: 1, PageSize: 20),
            ct);

        // The port sees the NORMALIZED axes: trimmed name, trimmed SNI, folded org.nr.
        captured.ShouldNotBeNull();
        captured.NamePrefix.ShouldBe("Volvo");
        captured.SniCodes.ShouldBe(["62010"]);
        captured.OrganizationNumber.ShouldBe(LegalAbOrgNr);
    }

    [Fact]
    public async Task Handle_InvalidInput_ThrowsTheUnreachableGuard_AndNeverCallsThePort()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = Substitute.For<ICompanyRegisterSearchQuery>();

        // Page 0 fails Create. In production ValidationBehavior 400s first; reaching the handler
        // with invalid input is validator/normalizer drift — fail loud (500), never guess a page.
        // Block body → Func<Task> (the handler returns ValueTask<T>, so an expression-bodied
        // lambda would infer Func<Task<T>>, which ShouldThrowAsync does not extend).
        var act = async () =>
        {
            await new SearchCompaniesQueryHandler(port).Handle(
                new SearchCompaniesQuery(null, null, null, null, Page: 0, PageSize: 20), ct);
        };

        await act.ShouldThrowAsync<InvalidOperationException>();
        await port.DidNotReceiveWithAnyArgs().SearchAsync(default!, CancellationToken.None);
    }
}
