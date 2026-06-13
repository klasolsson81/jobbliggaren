using Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.SetResumeLanguage;

public class SetResumeLanguageCommandValidatorTests
{
    private readonly SetResumeLanguageCommandValidator _validator = new();

    [Fact]
    public void Validate_WithValidSv_IsValid()
    {
        var result = _validator.Validate(new SetResumeLanguageCommand(Guid.NewGuid(), "Sv"));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithValidEn_IsValid()
    {
        var result = _validator.Validate(new SetResumeLanguageCommand(Guid.NewGuid(), "En"));
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_LanguageNotSvOrEn_Fails()
    {
        var result = _validator.Validate(new SetResumeLanguageCommand(Guid.NewGuid(), "Fr"));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_LanguageNull_Fails()
    {
        var result = _validator.Validate(new SetResumeLanguageCommand(Guid.NewGuid(), null!));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_LanguageEmpty_Fails()
    {
        var result = _validator.Validate(new SetResumeLanguageCommand(Guid.NewGuid(), string.Empty));
        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_ResumeIdEmpty_Fails()
    {
        var result = _validator.Validate(new SetResumeLanguageCommand(Guid.Empty, "Sv"));
        result.IsValid.ShouldBeFalse();
    }
}
