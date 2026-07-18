using Jobbliggaren.Application.CompanyRegister.Queries.GetCompanySearchMagnitude;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 company-search wave — <see cref="GetCompanySearchMagnitudeQueryValidator"/>, the same
/// TRANSPORT contract as the page validator (one authority, two call sites, zero duplicated rules).
/// Paging is fixed 1/1 inside the transport, so the only reachable failures are axis errors, each
/// forwarded verbatim (Code as PropertyName, Swedish message as text).
/// </summary>
public class GetCompanySearchMagnitudeQueryValidatorTests
{
    private static readonly GetCompanySearchMagnitudeQueryValidator Validator = new();

    [Fact]
    public void Validate_ValidQuery_IsValid()
    {
        var result = Validator.Validate(
            new GetCompanySearchMagnitudeQuery(["62010"], ["0180"], "Volvo", "556012-5790"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_PersonnummerShapedOrgNr_FailsWithThePersonnummerShapedKey_AndANonEmptyMessage()
    {
        var result = Validator.Validate(
            new GetCompanySearchMagnitudeQuery(null, null, null, "5501012345"));

        result.IsValid.ShouldBeFalse();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.PropertyName.ShouldBe("CompanyRegisterSearch.PersonnummerShaped");
        failure.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_MalformedSniCode_FailsWithTheInvalidSniCodeKey()
    {
        var result = Validator.Validate(
            new GetCompanySearchMagnitudeQuery(["6201"], null, null, null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe("CompanyRegisterSearch.InvalidSniCode");
    }
}
