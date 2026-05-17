using JobbPilot.Application.Applications.Commands.RecordFollowUpOutcome;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Commands;

/// <summary>
/// RecordFollowUpOutcomeCommandValidator — paritet med
/// AddFollowUpCommandValidator. RÖD tills validator implementerats.
/// </summary>
public class RecordFollowUpOutcomeCommandValidatorTests
{
    private readonly RecordFollowUpOutcomeCommandValidator _validator = new();

    private static RecordFollowUpOutcomeCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Responded");

    [Fact]
    public void Validate_WithValidCommand_IsValid()
    {
        var result = _validator.Validate(Valid());

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Responded")]
    [InlineData("NoResponse")]
    public void Validate_WithKnownOutcomeName_IsValid(string outcome)
    {
        var result = _validator.Validate(
            new RecordFollowUpOutcomeCommand(Guid.NewGuid(), Guid.NewGuid(), outcome));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithEmptyApplicationId_IsInvalid()
    {
        var result = _validator.Validate(
            new RecordFollowUpOutcomeCommand(Guid.Empty, Guid.NewGuid(), "Responded"));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptyFollowUpId_IsInvalid()
    {
        var result = _validator.Validate(
            new RecordFollowUpOutcomeCommand(Guid.NewGuid(), Guid.Empty, "Responded"));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Replied")]
    [InlineData("responded")]
    [InlineData("Okänt")]
    public void Validate_WithInvalidOutcomeName_IsInvalid(string outcome)
    {
        var result = _validator.Validate(
            new RecordFollowUpOutcomeCommand(Guid.NewGuid(), Guid.NewGuid(), outcome));

        result.IsValid.ShouldBeFalse();
    }
}
