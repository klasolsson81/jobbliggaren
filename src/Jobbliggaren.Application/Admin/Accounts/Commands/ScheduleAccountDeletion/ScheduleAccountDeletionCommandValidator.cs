using FluentValidation;
using Jobbliggaren.Application.Common.Validation;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.ScheduleAccountDeletion;

public sealed class ScheduleAccountDeletionCommandValidator : AbstractValidator<ScheduleAccountDeletionCommand>
{
    public ScheduleAccountDeletionCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.ReauthGrant).ReauthGrant();
    }
}
