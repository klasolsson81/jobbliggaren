using Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;
using Jobbliggaren.Application.Resumes.Queries;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.PromoteParsedResume;

// Fas 4 STEG A PR-2 — input-shape validation for the promote command. Parity with
// UpdateMasterContentCommandValidator (Content/FullName/Summary rules) + CreateResumeCommandValidator
// (Name rules). The personnummer re-scan is the HANDLER's job (it needs the concatenated free
// text) — NOT a validator rule. SPEC-DRIVEN. RED until the command + validator ship.
public class PromoteParsedResumeCommandValidatorTests
{
    private readonly PromoteParsedResumeCommandValidator _validator = new();

    private static ResumeContentDto ValidContent(
        string fullName = "Anna Andersson", string? summary = null) =>
        new(
            new PersonalInfoDto(fullName, "anna@example.com", "0701234567", "Stockholm"),
            Experiences: [],
            Educations: [],
            Skills: [],
            Summary: summary);

    private static PromoteParsedResumeCommand Command(
        Guid? parsedResumeId = null,
        string name = "Mitt importerade CV",
        ResumeContentDto? content = null) =>
        new(parsedResumeId ?? Guid.NewGuid(), name, content ?? ValidContent());

    [Fact]
    public void Validate_WellFormedCommand_Passes()
    {
        var result = _validator.Validate(Command());

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyParsedResumeId_Fails()
    {
        var result = _validator.Validate(Command(parsedResumeId: Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(PromoteParsedResumeCommand.ParsedResumeId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyName_Fails(string name)
    {
        var result = _validator.Validate(Command(name: name));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(PromoteParsedResumeCommand.Name));
    }

    [Fact]
    public void Validate_NameTooLong_Fails()
    {
        var result = _validator.Validate(Command(name: new string('A', 201)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(PromoteParsedResumeCommand.Name));
    }

    [Fact]
    public void Validate_NullContent_Fails()
    {
        // Construct directly — the Command() helper coalesces null content to ValidContent(),
        // so the null path must bypass it.
        var result = _validator.Validate(
            new PromoteParsedResumeCommand(Guid.NewGuid(), "Mitt CV", null!));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(PromoteParsedResumeCommand.Content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyFullName_Fails(string fullName)
    {
        var result = _validator.Validate(Command(content: ValidContent(fullName: fullName)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.PropertyName.Contains(nameof(ResumeContentDto.PersonalInfo))
            && e.PropertyName.Contains(nameof(PersonalInfoDto.FullName)));
    }

    [Fact]
    public void Validate_FullNameTooLong_Fails()
    {
        var result = _validator.Validate(Command(content: ValidContent(fullName: new string('A', 201))));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_SummaryTooLong_Fails()
    {
        var result = _validator.Validate(Command(content: ValidContent(summary: new string('A', 2_001))));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_SummaryAtMax_Passes()
    {
        var result = _validator.Validate(Command(content: ValidContent(summary: new string('A', 2_000))));

        result.IsValid.ShouldBeTrue();
    }
}
