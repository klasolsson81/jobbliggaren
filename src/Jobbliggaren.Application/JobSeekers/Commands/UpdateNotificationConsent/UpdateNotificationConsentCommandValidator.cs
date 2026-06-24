using FluentValidation;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;

/// <summary>
/// Pre-handler defense-in-depth for <see cref="UpdateNotificationConsentCommand"/> (ADR 0080
/// PR-6): the cadence must be a defined <c>DigestCadence</c> value. The wire binds the enum by
/// NAME (JsonStringEnumConverter), so an unknown string already fails model-binding with a 400;
/// <c>IsInEnum</c> closes the numeric-coercion gap defensively. <c>Enabled</c> needs no rule (any
/// bool is valid — the Domain owns the consent-stamping semantics).
/// </summary>
public sealed class UpdateNotificationConsentCommandValidator
    : AbstractValidator<UpdateNotificationConsentCommand>
{
    public UpdateNotificationConsentCommandValidator()
    {
        RuleFor(c => c.Cadence).IsInEnum();
    }
}
