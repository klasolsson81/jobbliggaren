using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.ImportResume;

// Fas 4 STEG 8 (F4-8) — input-shape validation for the CV import command (the
// magic-byte/format gate is CvFileSignature in the handler; this validator owns the
// cheap structural rules + the DoS input-size cap, ADR 0045).
//
// SPEC-DRIVEN.
public class ImportResumeCommandValidatorTests
{
    private readonly ImportResumeCommandValidator _validator = new();

    private static ImportResumeCommand Command(
        string fileName = "cv.pdf",
        string contentType = "application/pdf",
        int byteLength = 1024) =>
        new(fileName, contentType, new byte[byteLength]);

    [Fact]
    public void Validate_ValidSmallCommand_Passes()
    {
        var result = _validator.Validate(Command());

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyFileName_Fails(string fileName)
    {
        var result = _validator.Validate(Command(fileName: fileName));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ImportResumeCommand.FileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyContentType_Fails(string contentType)
    {
        var result = _validator.Validate(Command(contentType: contentType));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ImportResumeCommand.ContentType));
    }

    [Fact]
    public void Validate_EmptyFileBytes_Fails()
    {
        var result = _validator.Validate(Command(byteLength: 0));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ImportResumeCommand.FileBytes));
    }

    [Fact]
    public void Validate_OversizeFileBytes_Fails()
    {
        var result = _validator.Validate(
            Command(byteLength: ImportResumeCommandValidator.MaxFileBytes + 1));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ImportResumeCommand.FileBytes));
    }

    [Fact]
    public void Validate_AtMaxFileBytes_Passes()
    {
        var result = _validator.Validate(
            Command(byteLength: ImportResumeCommandValidator.MaxFileBytes));

        result.IsValid.ShouldBeTrue();
    }
}
