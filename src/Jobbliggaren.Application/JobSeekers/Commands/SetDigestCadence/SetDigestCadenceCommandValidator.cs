using FluentValidation;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetDigestCadence;

/// <summary>
/// Pre-handler defense-in-depth for <see cref="SetDigestCadenceCommand"/>: the cadence must be a
/// defined <c>DigestCadence</c> value. The wire binds the enum by NAME (JsonStringEnumConverter), so
/// an unknown string already fails model-binding with a 400; <c>IsInEnum</c> closes the
/// numeric-coercion gap.
/// </summary>
public sealed class SetDigestCadenceCommandValidator : AbstractValidator<SetDigestCadenceCommand>
{
    public SetDigestCadenceCommandValidator()
    {
        RuleFor(c => c.Cadence).IsInEnum();
    }
}
