using Jobbliggaren.Application.Applications.Commands.TransitionTo;
using Jobbliggaren.Domain.Applications;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

// ADR 0092 D3 (free transitions): TransitionToCommandValidator blockerar inte
// längre Ghosted — den gamla "Ghosted sätts automatiskt av systemet"-regeln är
// borttagen. Kvar finns endast input-form-reglerna: ApplicationId NotEmpty +
// TargetStatus är ett KÄNT statusnamn. Legaliteten i state-machinen är inte längre
// validatorns ansvar; aggregatet äger de två kvarvarande invarianterna
// (soft-delete-guard, self-transition-no-op).
public class TransitionToCommandValidatorTests
{
    private readonly TransitionToCommandValidator _validator = new();

    [Theory]
    [InlineData("Draft")]
    [InlineData("Submitted")]
    [InlineData("Acknowledged")]
    [InlineData("InterviewScheduled")]
    [InlineData("Interviewing")]
    [InlineData("OfferReceived")]
    [InlineData("Accepted")]
    [InlineData("Rejected")]
    [InlineData("Withdrawn")]
    [InlineData("Ghosted")]
    public void Validate_WithKnownStatusName_IsValid(string status)
    {
        var result = _validator.Validate(new TransitionToCommand(Guid.NewGuid(), status));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithGhostedTarget_IsValid()
    {
        // Explicit regressions-/rename-vakt: manuell Ghosted är nu ett giltigt mål.
        var result = _validator.Validate(new TransitionToCommand(Guid.NewGuid(), "Ghosted"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithEveryKnownStatusName_IsValid()
    {
        // Binder validatorn mot HELA SmartEnum-listan så att inget statusnamn tyst
        // blir ogiltigt i validatorn (och att en framtida ny status fångas här).
        foreach (var status in ApplicationStatus.List)
        {
            var result = _validator.Validate(new TransitionToCommand(Guid.NewGuid(), status.Name));

            result.IsValid.ShouldBeTrue($"'{status.Name}' borde vara ett giltigt mål");
        }
    }

    [Fact]
    public void Validate_WithUnknownStatusName_IsInvalid()
    {
        var result = _validator.Validate(new TransitionToCommand(Guid.NewGuid(), "NotAStatus"));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptyTargetStatus_IsInvalid()
    {
        var result = _validator.Validate(new TransitionToCommand(Guid.NewGuid(), string.Empty));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithEmptyApplicationId_IsInvalid()
    {
        var result = _validator.Validate(new TransitionToCommand(Guid.Empty, "Submitted"));

        result.IsValid.ShouldBeFalse();
    }
}
