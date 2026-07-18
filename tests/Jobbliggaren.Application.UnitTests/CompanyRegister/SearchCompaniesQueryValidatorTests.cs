using Jobbliggaren.Application.CompanyRegister.Queries.SearchCompanies;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 company-search wave — <see cref="SearchCompaniesQueryValidator"/> is the pipeline TRANSPORT
/// for the single normalizer, not a second rule-set. It runs
/// <c>CompanyRegisterSearchCriteria.Create</c> and forwards its <c>DomainError</c> verbatim: the
/// Code becomes the failure's PropertyName (so <c>ValidationBehavior</c> surfaces it as a 400), the
/// Swedish message becomes the text. These tests pin that faithful forwarding.
/// </summary>
public class SearchCompaniesQueryValidatorTests
{
    private static readonly SearchCompaniesQueryValidator Validator = new();

    [Fact]
    public void Validate_ValidQuery_IsValid()
    {
        var result = Validator.Validate(
            new SearchCompaniesQuery(["62010"], ["0180"], "Volvo", "556012-5790", 1, 20));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_PageZero_FailsWithTheDomainErrorCodeAsKey_AndANonEmptySwedishMessage()
    {
        var result = Validator.Validate(
            new SearchCompaniesQuery(null, null, null, null, Page: 0, PageSize: 20));

        result.IsValid.ShouldBeFalse();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe("CompanyRegisterSearch.InvalidPage");
        failure.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_PersonnummerShapedOrgNr_FailsWithThePersonnummerShapedKey()
    {
        var result = Validator.Validate(
            new SearchCompaniesQuery(null, null, null, "5501012345", 1, 20));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("CompanyRegisterSearch.PersonnummerShaped");
    }

    [Fact]
    public void Validate_MultipleProblems_ReportsOnlyTheFirst_PageBeforeAxes()
    {
        // Create short-circuits on the FIRST failure (page is checked before the axes), so even a
        // query with several problems yields exactly ONE FluentValidation failure — the transport
        // is faithful to the single normalizer's ordering.
        var result = Validator.Validate(new SearchCompaniesQuery(
            SniCodes: ["not-a-code"], MunicipalityCodes: null, Name: null, OrganizationNumber: null,
            Page: 0, PageSize: 20));

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("CompanyRegisterSearch.InvalidPage");
    }
}
