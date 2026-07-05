using Jobbliggaren.Application.Resumes.Commands.SetFindingStatus;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.SetFindingStatus;

/// <summary>
/// Fas 4b PR-4 (#653) — input-shape validation for the finding-status command. ResumeId and
/// CriterionId must be present; the Status must parse fail-loud to a <see cref="Jobbliggaren.Domain.Resumes.ReviewFindingStatus"/>
/// name (case-sensitive exact match — a bad status is a client bug, never silently coerced). The
/// criterion-id shape itself is re-validated by the aggregate, so the validator only requires presence.
/// </summary>
public class SetFindingStatusCommandValidatorTests
{
    private readonly SetFindingStatusCommandValidator _validator = new();

    private static SetFindingStatusCommand Command(
        Guid? id = null, string criterionId = "A1", string status = "Resolved") =>
        new(id ?? Guid.NewGuid(), criterionId, status);

    [Theory]
    [InlineData("Open")]
    [InlineData("Resolved")]
    [InlineData("Ignored")]
    public void Validate_ValidCommand_Passes(string status)
    {
        var result = _validator.Validate(Command(status: status));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyResumeId_Fails()
    {
        var result = _validator.Validate(Command(id: Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SetFindingStatusCommand.ResumeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyCriterionId_Fails(string criterionId)
    {
        var result = _validator.Validate(Command(criterionId: criterionId));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SetFindingStatusCommand.CriterionId));
    }

    [Theory]
    [InlineData("resolved")]   // case-sensitive: lowercase is not an exact member name
    [InlineData("IGNORED")]
    [InlineData("open")]
    [InlineData("Bogus")]      // not a member at all
    [InlineData("")]           // empty
    [InlineData("1")]          // numeric string, not a name
    public void Validate_UnparseableStatus_Fails(string status)
    {
        var result = _validator.Validate(Command(status: status));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SetFindingStatusCommand.Status));
    }
}
