using FluentValidation;
using Jobbliggaren.Application.BackgroundJobs;

namespace Jobbliggaren.Application.Admin.BackgroundJobs.Commands.TriggerRecurringJob;

/// <summary>
/// Enforces the closed trigger allowlist (security-auditor T7 — fan-out/RCE
/// prevention): only a member of <see cref="RecurringJobIds.All"/> may be
/// triggered. A non-allowlisted id is a 400 shape error regardless of caller;
/// the action itself is additionally Admin-gated (IAdminRequest + endpoint policy).
/// </summary>
public sealed class TriggerRecurringJobCommandValidator : AbstractValidator<TriggerRecurringJobCommand>
{
    public TriggerRecurringJobCommandValidator()
    {
        RuleFor(c => c.RecurringJobId)
            .NotEmpty()
            .WithMessage("RecurringJobId krävs.")
            .Must(RecurringJobIds.All.Contains)
            .WithMessage("Okänt recurring-jobb-id. Bara registrerade schemalagda jobb kan triggas.");
    }
}
