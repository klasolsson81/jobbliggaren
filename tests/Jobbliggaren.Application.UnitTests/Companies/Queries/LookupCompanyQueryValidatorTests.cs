using Jobbliggaren.Application.Companies.Queries.LookupCompany;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Companies.Queries;

/// <summary>
/// #454 — the FORMAT gate only (exactly 10 digits, `\z`-anchored). The personnummer-shape POLICY is
/// handler-owned (ADR 0088 D4) and tested in <see cref="LookupCompanyQueryHandlerTests"/>.
/// </summary>
public class LookupCompanyQueryValidatorTests
{
    private readonly LookupCompanyQueryValidator _validator = new();

    [Theory]
    [InlineData("5592804784")] // legal entity
    [InlineData("1901012384")] // pnr-shaped — FORMAT-valid; the policy refusal is the handler's job
    public void Validate_TenDigits_Passes(string orgNr)
    {
        _validator.Validate(new LookupCompanyQuery(orgNr)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("559280478")] // 9 digits
    [InlineData("55928047841")] // 11 digits
    [InlineData("559280-4784")] // hyphen (FE normalises before submit)
    [InlineData("559280478a")] // non-digit
    [InlineData("5592804784\n")] // trailing newline — \z rejects (newline injection)
    public void Validate_NonTenDigitForms_Fail(string orgNr)
    {
        _validator.Validate(new LookupCompanyQuery(orgNr)).IsValid.ShouldBeFalse();
    }

    [Theory]
    // #865 — this validator re-literalises the org.nr pattern, so it needs its OWN Unicode pin:
    // fixing OrganizationNumber alone leaves this copy on \d (=\p{Nd}), silently accepting
    // fullwidth (U+FF10) / Arabic-Indic (U+0660) digits. Look-alike built by codepoint arithmetic.
    [InlineData(0xFF10)]
    [InlineData(0x0660)]
    public void Validate_UnicodeDigitLookalike_Fails(int digitZero)
    {
        var lookalike = new string("5592804784"
            .Select(c => (char)(digitZero + (c - '0')))
            .ToArray());

        _validator.Validate(new LookupCompanyQuery(lookalike)).IsValid.ShouldBeFalse(
            $"'{lookalike}' (U+{digitZero:X4}-siffror) måste avvisas — \\d hade släppt igenom den.");
    }
}
