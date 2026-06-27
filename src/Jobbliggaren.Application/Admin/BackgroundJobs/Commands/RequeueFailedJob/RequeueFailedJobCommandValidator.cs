using FluentValidation;

namespace Jobbliggaren.Application.Admin.BackgroundJobs.Commands.RequeueFailedJob;

/// <summary>
/// Shape validation only. The existence + Failed-state precondition is live state
/// and is resolved in the handler via the port (NotFound/Conflict), per the
/// "shape in validator, invariant in handler" doctrine.
/// </summary>
public sealed class RequeueFailedJobCommandValidator : AbstractValidator<RequeueFailedJobCommand>
{
    public RequeueFailedJobCommandValidator()
    {
        RuleFor(c => c.JobId)
            .NotEmpty()
            .WithMessage("JobId krävs.")
            .MaximumLength(64)
            .WithMessage("JobId får vara max 64 tecken.");
    }
}
