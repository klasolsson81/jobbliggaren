using Jobbliggaren.Application.Applications.Commands.LogFollowUp;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

/// <summary>
/// LogFollowUpCommandValidator — ADR 0092 D4/D5. ApplicationId NotEmpty; Note
/// optional (null valid) but max 2 000 tecken. Paritet med
/// RecordFollowUpOutcomeCommandValidatorTests.
/// </summary>
public class LogFollowUpCommandValidatorTests
{
    private readonly LogFollowUpCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidCommand_IsValid()
    {
        var result = _validator.Validate(new LogFollowUpCommand(Guid.NewGuid(), "Ringde rekryteraren"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNullNote_IsValid()
    {
        var result = _validator.Validate(new LogFollowUpCommand(Guid.NewGuid(), null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNoteAtMaxLength_IsValid()
    {
        var result = _validator.Validate(
            new LogFollowUpCommand(Guid.NewGuid(), new string('A', 2000)));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNoteTooLong_IsInvalid()
    {
        var result = _validator.Validate(
            new LogFollowUpCommand(Guid.NewGuid(), new string('A', 2001)));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptyApplicationId_IsInvalid()
    {
        var result = _validator.Validate(new LogFollowUpCommand(Guid.Empty, "Kontakt"));

        result.IsValid.ShouldBeFalse();
    }
}
