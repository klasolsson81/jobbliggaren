using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #679 (C5-email of epik #481) — pins the REQUEST-step validator. Parity with
/// <c>ChangePasswordCommandValidatorTests</c>: the re-auth grant is a credential (the shared
/// <c>ReauthGrantRules</c> rule — present and bounded, no format rule that could describe a real grant),
/// while the new email is a new value (NotEmpty + well-formed + 256-char cap so a malformed address
/// is a clean 400 before a token is minted).
/// </summary>
public class ChangeEmailCommandValidatorTests
{
    private readonly ChangeEmailCommandValidator _validator = new();

    private const string Grant = "AAECAwQFBgcICQoLDA0ODw"; // gitleaks:allow
    private const string ValidEmail = "ny.adress@example.se";

    [Fact]
    public void Validate_ValidChange_Passes()
        => _validator.Validate(new ChangeEmailCommand(Grant, ValidEmail)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingGrant_Fails(string? grant)
    {
        var result = _validator.Validate(new ChangeEmailCommand(grant, ValidEmail));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeEmailCommand.ReauthGrant));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingNewEmail_Fails(string? email)
    {
        var result = _validator.Validate(new ChangeEmailCommand(Grant, email));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeEmailCommand.NewEmail));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("plainaddress")]
    [InlineData("@no-local.se")]
    [InlineData("no-domain@")]
    public void Validate_MalformedNewEmail_Fails(string email)
    {
        var result = _validator.Validate(new ChangeEmailCommand(Grant, email));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeEmailCommand.NewEmail));
    }

    [Fact]
    public void Validate_NewEmailOver256Chars_Fails()
    {
        // 250-char local part + "@example.se" (11) = 262 chars — well-formed but over the 256 cap,
        // so the MaximumLength rule bounds the input before UserManager runs.
        var email = $"{new string('a', 250)}@example.se";

        var result = _validator.Validate(new ChangeEmailCommand(Grant, email));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeEmailCommand.NewEmail));
    }

    // The grant carries no format rule: a short-but-non-empty grant passes validation and is refused by the
    // store as unknown, so no validation message ever describes what a real grant looks like.
    [Fact]
    public void Validate_ShortButNonEmptyGrant_Passes()
        => _validator.Validate(new ChangeEmailCommand("x", ValidEmail)).IsValid.ShouldBeTrue();
}
