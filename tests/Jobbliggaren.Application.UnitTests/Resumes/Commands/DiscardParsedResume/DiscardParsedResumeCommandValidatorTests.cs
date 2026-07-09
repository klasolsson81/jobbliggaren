using Jobbliggaren.Application.Resumes.Commands.DiscardParsedResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.DiscardParsedResume;

// Fas 4b CV-motor v2 PR-8.1 (issue #657) — input-shape validation for the discard command: the only
// rule is a non-empty ParsedResumeId (parity PromoteParsedResumeCommandValidator's id rule). All
// ownership/existence checks are the handler's job. SPEC-DRIVEN — RED until the command + validator ship.
public class DiscardParsedResumeCommandValidatorTests
{
    private readonly DiscardParsedResumeCommandValidator _validator = new();

    [Fact]
    public void Validate_NonEmptyParsedResumeId_Passes()
    {
        var result = _validator.Validate(new DiscardParsedResumeCommand(Guid.NewGuid()));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyParsedResumeId_Fails()
    {
        var result = _validator.Validate(new DiscardParsedResumeCommand(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(DiscardParsedResumeCommand.ParsedResumeId));
    }
}
