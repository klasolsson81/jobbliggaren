using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;

/// <summary>
/// Wire-shape floor only. The clean-predicate is deliberately NOT here — "not clean" is a
/// routing outcome (<c>LeftPending</c>), never a 400, so it belongs to the handler, not the
/// validation pipeline (CTO-bind 2026-07-17 §2). <c>NameOverride</c> is optional (absent →
/// the handler resolves the account display name); when present it obeys the same 200-cap
/// as every CV-name write surface (<c>Resume.Rename</c>/<c>CreateFromParsed</c> parity).
/// </summary>
public sealed class AutoPromoteParsedResumeCommandValidator
    : AbstractValidator<AutoPromoteParsedResumeCommand>
{
    public AutoPromoteParsedResumeCommandValidator()
    {
        RuleFor(c => c.ParsedResumeId)
            .NotEmpty().WithMessage("ParsedResumeId krävs.");

        RuleFor(c => c.NameOverride)
            .MaximumLength(200).WithMessage("Namn får vara max 200 tecken.");
    }
}
