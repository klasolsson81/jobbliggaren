using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #679 (C5-email of epik #481) — pins the REQUEST-step validator. Parity with
/// <c>ChangePasswordCommandValidatorTests</c>: the current password is a re-auth credential
/// (NotEmpty ONLY — no length/complexity rule that could fail on and echo a supplied credential),
/// while the new email is a new value (NotEmpty + well-formed + 256-char cap so a malformed address
/// is a clean 400 before a token is minted).
/// </summary>
public class ChangeEmailCommandValidatorTests
{
    private readonly ChangeEmailCommandValidator _validator = new();

    private const string ValidEmail = "ny.adress@example.se";

    [Fact]
    public void Validate_ValidChange_Passes()
        => _validator.Validate(new ChangeEmailCommand("current-pw", ValidEmail)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingCurrentPassword_Fails(string? current)
    {
        var result = _validator.Validate(new ChangeEmailCommand(current, ValidEmail));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeEmailCommand.CurrentPassword));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingNewEmail_Fails(string? email)
    {
        var result = _validator.Validate(new ChangeEmailCommand("current-pw", email));

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
        var result = _validator.Validate(new ChangeEmailCommand("current-pw", email));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeEmailCommand.NewEmail));
    }

    [Fact]
    public void Validate_NewEmailOver256Chars_Fails()
    {
        // 250-char local part + "@example.se" (11) = 262 chars — well-formed but over the 256 cap,
        // so the MaximumLength rule bounds the input before UserManager runs.
        var email = $"{new string('a', 250)}@example.se";

        var result = _validator.Validate(new ChangeEmailCommand("current-pw", email));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangeEmailCommand.NewEmail));
    }

    // Security-note pin (parity with ChangePassword / DeleteAccount): the current-password (re-auth)
    // field carries NO length rule — NotEmpty only. A non-empty supplied credential therefore never
    // fails validation and can never be echoed via the ValidationException path; the strength of an
    // EXISTING password is irrelevant. A short-but-non-empty current password with a valid new email
    // must pass.
    [Fact]
    public void Validate_ShortButNonEmptyCurrentPassword_Passes()
        => _validator.Validate(new ChangeEmailCommand("x", ValidEmail)).IsValid.ShouldBeTrue();
}
