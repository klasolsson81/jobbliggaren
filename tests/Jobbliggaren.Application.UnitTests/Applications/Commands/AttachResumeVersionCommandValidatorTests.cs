using Jobbliggaren.Application.Applications.Commands.AttachResumeVersion;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

// RÖD svit (TDD) — F4-11 AttachResumeVersionCommandValidator.
// Spec: ApplicationId NotEmpty, ResumeVersionId NotEmpty (speglar
// TransitionToCommandValidator-mönstret för ApplicationId).
public class AttachResumeVersionCommandValidatorTests
{
    private readonly AttachResumeVersionCommandValidator _validator = new();

    [Fact]
    public void Validate_WithBothIdsPresent_IsValid()
    {
        var result = _validator.Validate(
            new AttachResumeVersionCommand(Guid.NewGuid(), Guid.NewGuid()));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithEmptyApplicationId_IsInvalid()
    {
        var result = _validator.Validate(
            new AttachResumeVersionCommand(Guid.Empty, Guid.NewGuid()));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptyResumeVersionId_IsInvalid()
    {
        var result = _validator.Validate(
            new AttachResumeVersionCommand(Guid.NewGuid(), Guid.Empty));

        result.IsValid.ShouldBeFalse();
    }
}
