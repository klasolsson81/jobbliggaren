using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands;
using Jobbliggaren.Domain.CompanyWatches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Commands;

/// <summary>
/// #560 PR-3 — the shared predicate-input validator (C-D12 + existence, CTO Fork G2). The rule
/// ORDER is the load-bearing property: raw cap → existence, existence-fails echo the offending
/// codes per axis, blanks are skipped (Domain drops them).
/// </summary>
public class CompanyWatchCriteriaInputValidatorTests
{
    // A tiny real catalog — the validator's existence question is a set lookup, so a handful of
    // known codes is the whole universe the tests need.
    private static readonly SniReferenceCatalog Sni = new(
        "test.v1",
        [new SniSection("K", "IT och telekommunikation")],
        [new SniDivision("62", "K", "IT-tjänster")],
        [new SniLeaf("62100", "62", "Datorprogrammering"), new SniLeaf("62201", "62", "IT-konsult")]);

    private static readonly KommunReferenceCatalog Kommuner = new(
        "test.v1",
        [new LanEntry("01", "Stockholms län"), new LanEntry("14", "Västra Götalands län")],
        [new KommunEntry("0180", "Stockholm", "01"), new KommunEntry("1480", "Göteborg", "14")]);

    private static CompanyWatchCriteriaInputValidator ValidatorFor(
        ICriterionReferenceProvider? provider = null)
    {
        if (provider is null)
        {
            provider = Substitute.For<ICriterionReferenceProvider>();
            provider.Sni.Returns(Sni);
            provider.Kommuner.Returns(Kommuner);
        }

        return new CompanyWatchCriteriaInputValidator(provider);
    }

    [Fact]
    public void Validate_KnownCodesOnBothAxes_Passes()
    {
        // The positive control — without it every negative test could pass against a validator
        // that rejects everything.
        var result = ValidatorFor().Validate(
            new CompanyWatchCriteriaInput(["62100", "62201"], ["0180", "1480"]));

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData(null, "0180")]
    [InlineData("62100", null)]
    public void Validate_NullAxis_Fails(string? sni, string? kommun)
    {
        var result = ValidatorFor().Validate(new CompanyWatchCriteriaInput(
            sni is null ? null : [sni],
            kommun is null ? null : [kommun]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_UnknownSniCode_FailsWithTheAxisSpecificCode_AndEchoesTheCode()
    {
        var result = ValidatorFor().Validate(
            new CompanyWatchCriteriaInput(["62100", "99999"], ["0180"]));

        result.IsValid.ShouldBeFalse();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.ErrorCode.ShouldBe("CompanyWatchCriterion.UnknownSniCodes");
        // Echoing the offending code is what lets the picker point at it — public SCB reference
        // data, no PII. The KNOWN code must not be echoed.
        failure.ErrorMessage.ShouldContain("99999");
        failure.ErrorMessage.ShouldNotContain("62100");
    }

    [Fact]
    public void Validate_UnknownKommunCode_FailsWithTheAxisSpecificCode()
    {
        var result = ValidatorFor().Validate(
            new CompanyWatchCriteriaInput(["62100"], ["0180", "9999"]));

        result.IsValid.ShouldBeFalse();
        var failure = result.Errors.ShouldHaveSingleItem();
        failure.ErrorCode.ShouldBe("CompanyWatchCriterion.UnknownMunicipalityCodes");
        failure.ErrorMessage.ShouldContain("9999");
    }

    [Fact]
    public void Validate_ManyUnknownCodes_EchoIsBounded()
    {
        // 25 unknown codes → the 400 body must not mirror the payload (bounded echo: 10 + "och
        // N till"). 15 = 25 − 10.
        var unknown = Enumerable.Range(10000, 25)
            .Select(static n => n.ToString("D5", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        var result = ValidatorFor().Validate(new CompanyWatchCriteriaInput(unknown, ["0180"]));

        result.IsValid.ShouldBeFalse();
        var failure = result.Errors.Single(
            static e => e.ErrorCode == "CompanyWatchCriterion.UnknownSniCodes");
        failure.ErrorMessage.ShouldContain("och 15 till");
        failure.ErrorMessage.ShouldNotContain(unknown[12]);
    }

    [Fact]
    public void Validate_OverRawCap_FailsOnTheCap_WithoutEverTouchingTheCatalogs()
    {
        // THE C-D12 ORDER PIN. A provider whose catalogs THROW on access proves the existence
        // walk never ran: an oversized list must die on arithmetic (Count), not after a walk.
        // If this test starts failing with TheCapWasBypassedException, someone reordered the
        // rules — the raw cap is the request-CPU defense and MUST run first.
        var throwing = Substitute.For<ICriterionReferenceProvider>();
        throwing.Sni.Returns(_ => throw new InvalidOperationException("TheCapWasBypassed: SNI"));
        throwing.Kommuner.Returns(_ => throw new InvalidOperationException("TheCapWasBypassed: kommun"));

        var oversizedSni = Enumerable.Repeat("62100", CompanyWatchCriteriaSpec.MaxSniCodes + 1).ToArray();
        var oversizedKommun = Enumerable.Repeat(
            "0180", CompanyWatchCriteriaSpec.MaxMunicipalityCodes + 1).ToArray();

        var result = ValidatorFor(throwing).Validate(
            new CompanyWatchCriteriaInput(oversizedSni, oversizedKommun));

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(static e => e.ErrorMessage.StartsWith("Max"));
    }

    [Fact]
    public void Validate_BlankElements_AreSkipped_NotFailedAsUnknown()
    {
        // The Domain's NormalizeList drops blanks — failing them as "unknown" here would reject a
        // request the Domain accepts (two normalisers = two rules; this pin keeps them ONE).
        var result = ValidatorFor().Validate(
            new CompanyWatchCriteriaInput(["62100", "  ", ""], ["0180", " "]));

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Validate_UntrimmedKnownCode_Passes()
    {
        // Trim-then-check mirrors NormalizeList: " 62100 " IS the stored "62100".
        var result = ValidatorFor().Validate(
            new CompanyWatchCriteriaInput([" 62100 "], ["0180"]));

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Validate_MalformedCode_IsSimplyUnknown()
    {
        // Format stays Domain-owned; a malformed code is unknown to the catalog and fails the
        // existence rule — no duplicated format regex in this validator (one rule, one owner).
        var result = ValidatorFor().Validate(
            new CompanyWatchCriteriaInput(["6210"], ["0180"]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode
            .ShouldBe("CompanyWatchCriterion.UnknownSniCodes");
    }
}
