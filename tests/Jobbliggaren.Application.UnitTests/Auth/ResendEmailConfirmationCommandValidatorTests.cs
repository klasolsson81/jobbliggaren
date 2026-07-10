using Jobbliggaren.Application.Auth.Commands.ResendEmailConfirmation;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #733 — pins the resend validator. A well-formed address funnels to the uniform 202 in the handler; a
/// malformed one is a clean 400 BEFORE any lookup — and that 400 is existence-INDEPENDENT (identical for
/// a taken and a fresh address, format-only) so it is not an enumeration oracle. Same email rule as
/// <c>RegisterCommandValidator</c>.
/// </summary>
public class ResendEmailConfirmationCommandValidatorTests
{
    private readonly ResendEmailConfirmationCommandValidator _validator = new();

    [Fact]
    public void Validate_WellFormedEmail_Passes()
        => _validator.Validate(new ResendEmailConfirmationCommand("klas@example.com"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-an-email")]
    public void Validate_MissingOrMalformedEmail_Fails(string? email)
    {
        var result = _validator.Validate(new ResendEmailConfirmationCommand(email));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ResendEmailConfirmationCommand.Email));
    }

    [Fact]
    public void Validate_TooLongEmail_Fails()
    {
        // MaximumLength(256): a well-formed but over-long address is still a clean, existence-independent
        // 400 (never an enumeration oracle).
        var tooLong = new string('a', 250) + "@example.com"; // 262 chars

        var result = _validator.Validate(new ResendEmailConfirmationCommand(tooLong));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ResendEmailConfirmationCommand.Email));
    }
}
