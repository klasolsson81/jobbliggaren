using Jobbliggaren.Application.Applications.Commands.CreateApplication;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Commands;

// RÖD svit (TDD). Spec: architect-design §7 steg 8 — validator-lagret
// speglar domän-invarianten (defense-in-depth):
//   JobAdId == null ⇒ Manual.Title/Company required (manuell ansökan)
//   JobAdId != null ⇒ Manual måste vara null (motstridigt annars)
//   JobAdId != null + Manual == null ⇒ OK (oförändrat JobAd-flöde)
//   CoverLetter ≤ 10 000 (oförändrat)
public class CreateApplicationCommandValidatorTests
{
    private readonly CreateApplicationCommandValidator _validator = new();

    [Fact]
    public void Validate_WithJobAdIdAndNoManual_IsValid()
    {
        var result = _validator.Validate(
            new CreateApplicationCommand(Guid.NewGuid(), null, null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNoJobAdIdAndNoManual_IsValid()
    {
        // Dagens cover-letter-only-flöde — oförändrat (degenererad ansökan).
        var result = _validator.Validate(
            new CreateApplicationCommand(null, "Personligt brev", null));

        result.IsValid.ShouldBeTrue();
    }

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

    [Fact]
    public void Validate_WithJobAdIdAndManualSet_IsInvalid()
    {
        // Motstridigt: JobAd-kopplad ansökan får inte ha manuell metadata
        // (speglar aggregat-invarianten i validator-lagret).
        var result = _validator.Validate(
            new CreateApplicationCommand(
                Guid.NewGuid(), null,
                new ManualPostingInput("Backend-utvecklare", "Klarna", null, null)));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_WithCoverLetterExceedingMaxLength_IsInvalid()
    {
        var result = _validator.Validate(
            new CreateApplicationCommand(null, new string('A', 10_001), null));

        result.IsValid.ShouldBeFalse();
    }
}
