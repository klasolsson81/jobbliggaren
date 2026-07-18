using System.Globalization;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 company-search wave — <see cref="CompanyRegisterSearchCriteria.Create"/>, THE single
/// normalizer for the general register search. Every axis is optional (browse-all is legal), a
/// blank/null ELEMENT is malformed input (reject 400, never silently drop), and the org.nr axis
/// folds written forms + refuses personnummer-shaped terms. There is deliberately no second
/// FluentValidation authority, so this factory is the whole contract — hence the exhaustive suite.
/// </summary>
public class CompanyRegisterSearchCriteriaTests
{
    // ---- happy path: all axes absent is a legal browse-all -------------------------------------

    [Fact]
    public void Create_AllAxesAbsent_IsBrowseAllWithEmptyListsNullMembersAndPagingKept()
    {
        var result = CompanyRegisterSearchCriteria.Create(
            sniCodes: null, municipalityCodes: null, name: null, organizationNumber: null,
            page: 3, pageSize: 50);

        result.IsSuccess.ShouldBeTrue();
        var criteria = result.Value;
        criteria.SniCodes.ShouldBeEmpty();
        criteria.MunicipalityCodes.ShouldBeEmpty();
        criteria.NamePrefix.ShouldBeNull();
        criteria.OrganizationNumber.ShouldBeNull();
        criteria.Page.ShouldBe(3);
        criteria.PageSize.ShouldBe(50);
    }

    [Fact]
    public void Create_AtTheMaxBounds_Succeeds()
    {
        var result = CompanyRegisterSearchCriteria.Create(
            null, null, null, null,
            page: CompanyRegisterSearchCriteria.MaxPage,
            pageSize: CompanyRegisterSearchCriteria.MaxPageSize);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Page.ShouldBe(CompanyRegisterSearchCriteria.MaxPage);
        result.Value.PageSize.ShouldBe(CompanyRegisterSearchCriteria.MaxPageSize);
    }

    // ---- paging bounds -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Create_PageOutOfBounds_FailsWithInvalidPage(int page)
    {
        var result = CompanyRegisterSearchCriteria.Create(null, null, null, null, page, pageSize: 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.InvalidPage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Create_PageSizeOutOfBounds_FailsWithInvalidPageSize(int pageSize)
    {
        var result = CompanyRegisterSearchCriteria.Create(null, null, null, null, page: 1, pageSize);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.InvalidPageSize");
    }

    // ---- SNI axis ------------------------------------------------------------------------------

    [Fact]
    public void Create_ValidSniCodes_AreTrimmedAndOrdinallyDeduped()
    {
        var result = CompanyRegisterSearchCriteria.Create(
            sniCodes: ["62010", "  62020  ", "62010"],
            municipalityCodes: null, name: null, organizationNumber: null, page: 1, pageSize: 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.ShouldBe(["62010", "62020"]); // trimmed, deduped, first-seen order
    }

    [Fact]
    public void Create_DuplicateSniCodes_CollapseToOne()
    {
        var result = CompanyRegisterSearchCriteria.Create(
            ["62010", "62010"], null, null, null, 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.Count.ShouldBe(1);
        result.Value.SniCodes.ShouldContain("62010");
    }

    [Theory]
    [InlineData("6201")]    // four digits
    [InlineData("620100")]  // six digits
    [InlineData("6201O")]   // a letter O, not a zero
    public void Create_MalformedSniCode_FailsWithInvalidSniCode(string code)
    {
        var result = CompanyRegisterSearchCriteria.Create([code], null, null, null, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.InvalidSniCode");
    }

    [Fact]
    public void Create_FullwidthDigitSniCode_IsRejected_NotFoldedToValid()
    {
        // A five-glyph SNI code in FULLWIDTH digits, built from ASCII "62010" by offsetting each
        // ASCII digit to its fullwidth counterpart (U+FF10..U+FF19) — so the source stays pure
        // ASCII with no literal Unicode. The [0-9]-explicit rule (#865) MUST reject it:
        // char.IsDigit / \p{Nd} would fold it into a "valid" code that then matches nothing in the
        // ASCII register — a silent zero-row search.
        const int fullwidthZero = 0xFF10;
        var fullwidth = new string("62010".Select(c => (char)(c - '0' + fullwidthZero)).ToArray());

        var result = CompanyRegisterSearchCriteria.Create([fullwidth], null, null, null, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.InvalidSniCode");
    }

    [Fact]
    public void Create_NullSniElement_FailsWithInvalidSniCode_NeverSilentlyDropped()
    {
        // JSON `[null]` reaches Create as a single null element — malformed input (400), NOT an
        // empty axis. Skipping it would be a second, silent normalizer (the #167 lesson).
        var result = CompanyRegisterSearchCriteria.Create([null], null, null, null, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.InvalidSniCode");
    }

    [Fact]
    public void Create_MoreThanMaxSniCodes_FailsWithTooManySniCodes()
    {
        // 1001 DISTINCT valid five-digit codes (10000..11000) — the cap bites after dedupe.
        var codes = Enumerable.Range(10000, CompanyRegisterSearchCriteria.MaxSniCodes + 1)
            .Select(n => n.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        var result = CompanyRegisterSearchCriteria.Create(codes, null, null, null, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.TooManySniCodes");
    }

    [Fact]
    public void Create_ExactlyMaxSniCodes_Succeeds()
    {
        var codes = Enumerable.Range(10000, CompanyRegisterSearchCriteria.MaxSniCodes)
            .Select(n => n.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        var result = CompanyRegisterSearchCriteria.Create(codes, null, null, null, 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.Count.ShouldBe(CompanyRegisterSearchCriteria.MaxSniCodes);
    }

    // ---- kommun axis (mirrors SNI; leading zero is load-bearing) -------------------------------

    [Fact]
    public void Create_ValidMunicipalityCode_PreservesLeadingZero()
    {
        var result = CompanyRegisterSearchCriteria.Create(null, ["0180"], null, null, 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MunicipalityCodes.ShouldBe(["0180"]); // "0180" != "180" — never int-parsed
    }

    [Theory]
    [InlineData("180")]    // three digits — the leading zero was stripped upstream
    [InlineData("01800")]  // five digits
    [InlineData("018O")]   // a letter O
    public void Create_MalformedMunicipalityCode_FailsWithInvalidMunicipalityCode(string code)
    {
        var result = CompanyRegisterSearchCriteria.Create(null, [code], null, null, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.InvalidMunicipalityCode");
    }

    [Fact]
    public void Create_MoreThanMaxMunicipalityCodes_FailsWithTooManyMunicipalityCodes()
    {
        // 291 DISTINCT valid four-digit codes (1000..1290).
        var codes = Enumerable.Range(1000, CompanyRegisterSearchCriteria.MaxMunicipalityCodes + 1)
            .Select(n => n.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        var result = CompanyRegisterSearchCriteria.Create(null, codes, null, null, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.TooManyMunicipalityCodes");
    }

    [Fact]
    public void Create_ExactlyMaxMunicipalityCodes_Succeeds()
    {
        var codes = Enumerable.Range(1000, CompanyRegisterSearchCriteria.MaxMunicipalityCodes)
            .Select(n => n.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        var result = CompanyRegisterSearchCriteria.Create(null, codes, null, null, 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MunicipalityCodes.Count.ShouldBe(CompanyRegisterSearchCriteria.MaxMunicipalityCodes);
    }

    // ---- name axis -----------------------------------------------------------------------------

    [Fact]
    public void Create_NameWithSurroundingWhitespace_IsTrimmed()
    {
        var result = CompanyRegisterSearchCriteria.Create(null, null, "  Volvo  ", null, 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NamePrefix.ShouldBe("Volvo");
    }

    [Fact]
    public void Create_WhitespaceOnlyName_BecomesNullAxis()
    {
        var result = CompanyRegisterSearchCriteria.Create(null, null, "   ", null, 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NamePrefix.ShouldBeNull();
    }

    [Fact]
    public void Create_NameLongerThanMax_FailsWithNameTooLong()
    {
        var name = new string('a', CompanyRegisterSearchCriteria.MaxNamePrefixLength + 1);

        var result = CompanyRegisterSearchCriteria.Create(null, null, name, null, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.NameTooLong");
    }

    [Fact]
    public void Create_NameAtMaxLength_Succeeds()
    {
        var name = new string('a', CompanyRegisterSearchCriteria.MaxNamePrefixLength);

        var result = CompanyRegisterSearchCriteria.Create(null, null, name, null, 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NamePrefix.ShouldBe(name);
    }

    // ---- org.nr axis: fold written forms, refuse personnummer-shaped ---------------------------

    [Fact]
    public void Create_HyphenatedLegalOrgNr_IsFoldedToStoredTenDigitForm()
    {
        var result = CompanyRegisterSearchCriteria.Create(null, null, null, "556012-5790", 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OrganizationNumber.ShouldBe("5560125790"); // hyphen folded away
    }

    [Theory]
    [InlineData("5501012345")]   // stored form; third digit 0 < 2 → personnummer-shaped
    [InlineData("550101-2345")]  // hyphenated form of the same value — folds, then still refused
    public void Create_PersonnummerShapedOrgNr_IsRefused(string term)
    {
        var result = CompanyRegisterSearchCriteria.Create(null, null, null, term, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.PersonnummerShaped");
    }

    [Theory]
    [InlineData("12345")]        // too short
    [InlineData("55601257901")]  // eleven digits — not a recognized written form
    [InlineData("ABC0125790")]   // letters
    public void Create_UnrecognizedOrgNr_FailsWithInvalidOrganizationNumber(string term)
    {
        var result = CompanyRegisterSearchCriteria.Create(null, null, null, term, 1, 20);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyRegisterSearch.InvalidOrganizationNumber");
    }

    [Fact]
    public void Create_WhitespaceOnlyOrgNr_BecomesNullAxis_AndSucceeds()
    {
        var result = CompanyRegisterSearchCriteria.Create(null, null, null, "   ", 1, 20);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OrganizationNumber.ShouldBeNull();
    }

    // ---- derived caps + redaction --------------------------------------------------------------

    [Fact]
    public void MaxServableRows_IsDerivedFromMaxPageTimesPageSize()
    {
        // The page cap and the count cap are ONE knowledge piece — derived, never hand-picked.
        CompanyRegisterSearchCriteria.MaxServableRows(20)
            .ShouldBe(CompanyRegisterSearchCriteria.MaxPage * 20);
        CompanyRegisterSearchCriteria.MaxServableRows(20).ShouldBe(2000);
    }

    [Fact]
    public void ToString_DoesNotContainTheOrgNr_SoAMelPlaceholderCannotLeakIt()
    {
        // #883 — the record carries a raw org.nr (possibly personnummer-adjacent before refusal);
        // the compiler-generated ToString would print it for a plain {Criteria} MEL placeholder.
        var criteria = CompanyRegisterSearchCriteria.FromTrusted(
            sniCodes: [], municipalityCodes: [], namePrefix: null,
            organizationNumber: "5560125790", page: 1, pageSize: 20);

        criteria.ToString().ShouldNotContain("5560125790");
    }
}
