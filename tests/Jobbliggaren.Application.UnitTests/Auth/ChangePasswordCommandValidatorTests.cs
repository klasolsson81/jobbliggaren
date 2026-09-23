using Jobbliggaren.Application.Auth.Commands.ChangePassword;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class ChangePasswordCommandValidatorTests
{
    private readonly ChangePasswordCommandValidator _validator = new();

    private const string Grant = "AAECAwQFBgcICQoLDA0ODw"; // gitleaks:allow

    private static string TwelveChars => new('a', 12);

    [Fact]
    public void Validate_ValidChange_Passes()
        => _validator.Validate(new ChangePasswordCommand(Grant, "current-pw", TwelveChars)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_EmptyGrant_Fails(string? grant)
    {
        var result = _validator.Validate(new ChangePasswordCommand(grant, "current-pw", TwelveChars));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangePasswordCommand.ReauthGrant));
    }

    [Fact]
    public void Validate_EmptyCurrentPassword_Fails()
    {
        var result = _validator.Validate(new ChangePasswordCommand(Grant, string.Empty, TwelveChars));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangePasswordCommand.CurrentPassword));
    }

    [Fact]
    public void Validate_NewPasswordBelowFloor_Fails()
    {
        var result = _validator.Validate(new ChangePasswordCommand(Grant, "current-pw", new string('a', 11)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void Validate_NewPasswordAtFloor_Passes()
        => _validator.Validate(new ChangePasswordCommand(Grant, "current-pw", TwelveChars)).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_EmptyNewPassword_Fails()
        => _validator.Validate(new ChangePasswordCommand(Grant, "current-pw", string.Empty)).IsValid.ShouldBeFalse();

    // Security-note pin: the current-password field carries NO length rule, so a non-empty current
    // password never fails validation and can never be echoed via the ValidationException path. A
    // short-but-non-empty current password with a valid new password must pass.
    [Fact]
    public void Validate_ShortButNonEmptyCurrentPassword_Passes()
        => _validator.Validate(new ChangePasswordCommand(Grant, "short", TwelveChars)).IsValid.ShouldBeTrue();
}
