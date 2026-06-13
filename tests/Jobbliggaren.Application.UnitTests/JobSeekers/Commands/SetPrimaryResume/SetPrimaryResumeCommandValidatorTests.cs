using Jobbliggaren.Application.JobSeekers.Commands.SetPrimaryResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetPrimaryResume;

public class SetPrimaryResumeCommandValidatorTests
{
    private readonly SetPrimaryResumeCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidResumeId_IsValid()
    {
        var result = _validator.Validate(new SetPrimaryResumeCommand(Guid.NewGuid()));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ResumeIdEmpty_Fails()
    {
        var result = _validator.Validate(new SetPrimaryResumeCommand(Guid.Empty));
        result.IsValid.ShouldBeFalse();
    }
}
