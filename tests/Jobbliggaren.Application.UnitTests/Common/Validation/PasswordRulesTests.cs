using FluentValidation;
using Jobbliggaren.Application.Common.Validation;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Validation;

/// <summary>
/// Pins the shared password-strength rule (the single source of truth applied by every command
/// that accepts a new password). The length must stay in lockstep with Identity's
/// <c>RequiredLength = 12</c>; this test fails loudly if the constant drifts.
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
    public void MinimumLength_mirrors_Identity_RequiredLength_of_12()
        => PasswordRules.MinimumLength.ShouldBe(12);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Password_null_or_empty_fails(string? password)
        => _validator.Validate(new Probe(password)).IsValid.ShouldBeFalse();

    [Fact]
    public void Password_one_below_minimum_fails()
        => _validator.Validate(new Probe(new string('a', PasswordRules.MinimumLength - 1)))
            .IsValid.ShouldBeFalse();

    [Fact]
    public void Password_at_minimum_passes()
        => _validator.Validate(new Probe(new string('a', PasswordRules.MinimumLength)))
            .IsValid.ShouldBeTrue();
}
