using FluentValidation;
using Jobbliggaren.Domain.Applications;

namespace Jobbliggaren.Application.Applications.Commands.TransitionTo;

public sealed class TransitionToCommandValidator : AbstractValidator<TransitionToCommand>
{
    public TransitionToCommandValidator()
    {
        RuleFor(c => c.ApplicationId)
            .NotEmpty()
            .WithMessage("ApplicationId är obligatoriskt.");

        // ADR 0092 D3 (free transitions): any of the ten statuses is a valid
        // target, INCLUDING a manual Ghosted (the user marks "Inget svar" from the
        // status menu / kanban). The former "Ghosted sätts automatiskt av systemet"
        // block is gone — manual Ghosted now routes through TransitionTo (raising
        // the transition event + a StatusChange), while the system/job path stays
        // on MarkGhosted. The only input rule left is that the name is a known
        // status; the aggregate enforces the remaining invariants (soft-delete,
        // self-transition no-op).
        RuleFor(c => c.TargetStatus)
            .NotEmpty()
            .WithMessage("TargetStatus är obligatoriskt.")
            .Must(s => ApplicationStatus.TryFromName(s, out _))
            .WithMessage("Okänd status.");
    }
}
