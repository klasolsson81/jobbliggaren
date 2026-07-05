using Jobbliggaren.Application.Auth.Commands.Register;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// Pins the register password floor after adopting the shared <c>.Password()</c> rule: the
/// FluentValidation minimum now matches Identity's <c>RequiredLength = 12</c>, so an 8–11 char
/// password is rejected at validation (400) instead of slipping through to fail at
/// <c>UserManager.CreateAsync</c>.
/// </summary>
public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    private static RegisterCommand WithPassword(string password) =>
        new(Email: "klas@example.com", Password: password, DisplayName: "Klas Olsson");

    [Fact]
    public void Validate_TwelveCharPassword_Passes()
        => _validator.Validate(WithPassword(new string('a', 12))).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_ElevenCharPassword_Fails()
    {
        var result = _validator.Validate(WithPassword(new string('a', 11)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterCommand.Password));
    }

    [Fact]
    public void Validate_LegacyEightCharPassword_NowFails()
        => _validator.Validate(WithPassword("S3kret!p")).IsValid.ShouldBeFalse();

    [Fact]
    public void Validate_EmptyPassword_Fails()
        => _validator.Validate(WithPassword(string.Empty)).IsValid.ShouldBeFalse();
}
