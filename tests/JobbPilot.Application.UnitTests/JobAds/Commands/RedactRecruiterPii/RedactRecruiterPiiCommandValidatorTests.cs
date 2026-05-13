using JobbPilot.Application.JobAds.Commands.RedactRecruiterPii;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Commands.RedactRecruiterPii;

public class RedactRecruiterPiiCommandValidatorTests
{
    private readonly RedactRecruiterPiiCommandValidator _validator = new();

    [Fact]
    public void ValidEmailType_Passes()
    {
        var cmd = new RedactRecruiterPiiCommand("alice@example.com", RecruiterIdentifierType.Email);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyIdentifier_Fails(string id)
    {
        var cmd = new RedactRecruiterPiiCommand(id, RecruiterIdentifierType.Email);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void EmailType_WithInvalidEmail_Fails()
    {
        var cmd = new RedactRecruiterPiiCommand("not-an-email", RecruiterIdentifierType.Email);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void NameType_WithoutEmailFormat_Passes_Validator()
    {
        // Validator släpper igenom Name-typ med valfri string (>0 tecken) — handlern
        // returnerar NameNotSupportedYet, men validator-yta ska inte vara strikt här.
        var cmd = new RedactRecruiterPiiCommand("Alice Anka", RecruiterIdentifierType.Name);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void IdentifierTooLong_Fails()
    {
        var longId = new string('a', 255);
        var cmd = new RedactRecruiterPiiCommand(longId, RecruiterIdentifierType.Email);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeFalse();
    }
}
