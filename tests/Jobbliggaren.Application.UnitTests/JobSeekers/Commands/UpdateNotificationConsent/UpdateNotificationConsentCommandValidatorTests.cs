using Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.UpdateNotificationConsent;

// ADR 0080 Vag 4 PR-6 — the validator is pre-handler defense-in-depth: the cadence must be a
// defined DigestCadence value (IsInEnum closes the numeric-coercion gap; the wire binds the
// enum by NAME so an unknown string already fails model-binding with a 400). Enabled needs no
// rule — any bool is valid (the Domain owns the consent-stamping semantics).
public class UpdateNotificationConsentCommandValidatorTests
{
    private readonly UpdateNotificationConsentCommandValidator _validator = new();

    [Theory]
    [InlineData(DigestCadence.Daily)]
    [InlineData(DigestCadence.Weekly)]
    public void Validate_WithDefinedCadence_Passes(DigestCadence cadence)
    {
        var result = _validator.Validate(
            new UpdateNotificationConsentCommand(Enabled: true, Cadence: cadence));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithDefinedCadence_Passes_RegardlessOfEnabledFlag()
    {
        // Enabled is unconstrained — a disable command with a defined cadence is still valid.
        var result = _validator.Validate(
            new UpdateNotificationConsentCommand(Enabled: false, Cadence: DigestCadence.Weekly));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithOutOfRangeCadence_IsInvalid()
    {
        // A numeric value with no defined enum member (e.g. coerced past the binder) fails IsInEnum.
        var result = _validator.Validate(
            new UpdateNotificationConsentCommand(Enabled: true, Cadence: (DigestCadence)99));

        result.IsValid.ShouldBeFalse();
    }
}
