using Jobbliggaren.Application.Applications.Commands.CreateApplication;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

// Spec (#315 / ADR 0086 D2): den generiska CreateApplication-validatorn är
// FAIL-CLOSED på JobAdId — RuleFor(c => c.JobAdId).Null(). En JobAd-kopplad
// ansökan måste bära ett captured AdSnapshot, vilket BARA den dedikerade "Har
// ansökt"-vägen (CreateApplicationFromJobAdCommand) producerar. Den generiska
// create-vägen får aldrig skapa en o-snapshottad JobAd-länk.
//
// ÄNDRING mot tidigare kontrakt: Validate_WithJobAdIdAndNoManual_IsValid var
// tidigare giltig (degenererad JobAd-länk utan snapshot). Den vänds nu till
// ogiltig (fail-closed) — JobAdId tillåts inte längre på denna väg.
//
// Oförändrat: CoverLetter ≤ 10 000; manuell ansökan (JobAdId == null) kräver
// Title/Company.
public class CreateApplicationCommandValidatorTests
{
    private readonly CreateApplicationCommandValidator _validator = new();

    // ---------------------------------------------------------------
    // #315 — JobAdId är fail-closed (måste vara null på denna väg)
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_WithJobAdId_IsInvalid()
    {
        // Fail-closed-regeln slår till — en JobAd-länk skapas via "Har ansökt".
        var result = _validator.Validate(
            new CreateApplicationCommand(Guid.NewGuid(), null, null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e =>
            e.ErrorMessage ==
            "En ansökan kopplad till en annons skapas via knappen \"Har ansökt\" på annonsen.");
    }

    [Fact]
    public void Validate_WithJobAdIdAndManualSet_IsInvalid()
    {
        // Fortsatt ogiltig: JobAdId fail-closed slår till oavsett Manual.
        var result = _validator.Validate(
            new CreateApplicationCommand(
                Guid.NewGuid(), null,
                new ManualPostingInput("Backend-utvecklare", "Klarna", null, null)));

        result.IsValid.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // JobAdId == null — manuell / cover-letter-only förblir giltig
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_WithNoJobAdIdAndValidManual_IsValid()
    {
        var result = _validator.Validate(
            new CreateApplicationCommand(
                null, null,
                new ManualPostingInput("Backend-utvecklare", "Klarna", null, null)));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNoJobAdIdAndCoverLetterOnly_IsValid()
    {
        // Dagens cover-letter-only-flöde — oförändrat (degenererad ansökan).
        var result = _validator.Validate(
            new CreateApplicationCommand(null, "Personligt brev", null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNoJobAdIdAndNoManual_IsValid()
    {
        var result = _validator.Validate(
            new CreateApplicationCommand(null, null, null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNoJobAdIdAndEmptyManualTitle_IsInvalid()
    {
        var result = _validator.Validate(
            new CreateApplicationCommand(
                null, null, new ManualPostingInput("", "Klarna", null, null)));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithNoJobAdIdAndEmptyManualCompany_IsInvalid()
    {
        var result = _validator.Validate(
            new CreateApplicationCommand(
                null, null, new ManualPostingInput("Backend-utvecklare", "", null, null)));

        result.IsValid.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // CoverLetter — oförändrat
    // ---------------------------------------------------------------

    [Fact]
    public void Validate_WithCoverLetterExceedingMaxLength_IsInvalid()
    {
        var result = _validator.Validate(
            new CreateApplicationCommand(null, new string('A', 10_001), null));

        result.IsValid.ShouldBeFalse();
    }
}
