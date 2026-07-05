using FluentValidation;
using Jobbliggaren.Application.Common.Validation;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Validation;

/// <summary>
/// Pins the shared password-strength rule and its Application-side length constant. This is a
/// regression guard on the Application literal (12) — it does NOT, and cannot, prove lockstep with
/// Identity's <c>RequiredLength</c> (Application may not reference Infrastructure to assert it); the
/// lockstep is maintained by the doc-note on <see cref="PasswordRules"/>. If the Identity option
/// changes, update <see cref="PasswordRules.MinimumLength"/> in the same change.
/// </summary>
public class PasswordRulesTests
{
    private sealed record Probe(string? Password);

    private sealed class ProbeValidator : AbstractValidator<Probe>
    {
        public ProbeValidator() => RuleFor(p => p.Password).Password();
    }

    private readonly ProbeValidator _validator = new();

    [Fact]
    public void MinimumLength_IsTwelve()
        => PasswordRules.MinimumLength.ShouldBe(12);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_NullOrEmpty_Fails(string? password)
        => _validator.Validate(new Probe(password)).IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_OneBelowMinimum_Fails()
        => _validator.Validate(new Probe(new string('a', PasswordRules.MinimumLength - 1)))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_AtMinimum_Passes()
        => _validator.Validate(new Probe(new string('a', PasswordRules.MinimumLength)))
            .IsValid.ShouldBeTrue();
}
