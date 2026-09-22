using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Common.Validation;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The change-email confirm step's validator (#1739): a malformed request is a clean 400 before a grant is
/// redeemed.
/// </summary>
public sealed class ConfirmEmailChangeCommandValidatorTests
{
    private const string ValidEmail = "ny.adress@example.se";
    private const string ValidGrant = "an-opaque-change-email-grant"; // gitleaks:allow

    private readonly ConfirmEmailChangeCommandValidator _confirm = new();

    [Fact]
    public void A_grant_and_a_well_formed_address_pass()
        => _confirm.Validate(new ConfirmEmailChangeCommand(ValidGrant, ValidEmail)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_grant_fails(string? grant)
    {
        var result = _confirm.Validate(new ConfirmEmailChangeCommand(grant, ValidEmail));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmEmailChangeCommand.ChangeEmailGrant));
    }

    [Fact]
    public void A_grant_longer_than_any_minted_one_fails()
    {
        var result = _confirm.Validate(new ConfirmEmailChangeCommand(new string('a', 65), ValidEmail));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmEmailChangeCommand.ChangeEmailGrant));
        _confirm.Validate(new ConfirmEmailChangeCommand(new string('a', 64), ValidEmail)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("no-domain@")]
    public void A_missing_or_malformed_address_fails(string? email)
    {
        var result = _confirm.Validate(new ConfirmEmailChangeCommand(ValidGrant, email));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmEmailChangeCommand.NewEmail));
    }

    [Fact]
    public void An_address_over_the_one_email_bound_fails()
    {
        var local = new string('a', EmailAddressRules.MaximumLength - "@example.se".Length + 1);

        var result = _confirm.Validate(new ConfirmEmailChangeCommand(ValidGrant, local + "@example.se"));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmEmailChangeCommand.NewEmail));
    }
}
