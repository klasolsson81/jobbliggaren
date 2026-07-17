using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.AutoPromoteParsedResume;

// Wire-shape floor only — the clean-predicate is handler-owned routing, never a 400
// (CTO-bind 2026-07-17 §2), so the validator checks exactly the id and the optional
// override's 200-cap (Resume.Rename/CreateFromParsed parity).
public class AutoPromoteParsedResumeCommandValidatorTests
{
    private readonly AutoPromoteParsedResumeCommandValidator _sut = new();

    [Fact]
    public void EmptyParsedResumeId_Fails()
    {
        var result = _sut.Validate(new AutoPromoteParsedResumeCommand(Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.PropertyName == nameof(AutoPromoteParsedResumeCommand.ParsedResumeId));
    }

    [Fact]
    public void AbsentNameOverride_IsValid()
    {
        _sut.Validate(new AutoPromoteParsedResumeCommand(Guid.NewGuid()))
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void NameOverrideAtCap_IsValid()
    {
        _sut.Validate(new AutoPromoteParsedResumeCommand(Guid.NewGuid(), new string('a', 200)))
            .IsValid.ShouldBeTrue();
    }

    [Fact]
    public void NameOverrideOverCap_Fails()
    {
        var result = _sut.Validate(
            new AutoPromoteParsedResumeCommand(Guid.NewGuid(), new string('a', 201)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.PropertyName == nameof(AutoPromoteParsedResumeCommand.NameOverride));
    }
}
