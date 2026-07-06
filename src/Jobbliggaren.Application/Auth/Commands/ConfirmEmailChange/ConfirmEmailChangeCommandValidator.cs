using FluentValidation;

namespace Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    private const int MaxEmailLength = 256;

    public ConfirmEmailChangeCommandValidator()
    {
        // UserId.NotEmpty so a malformed request is a clean 400 and never reaches the post-success
        // AuditLogEntry.Create empty-aggregateId throw (CTO note; ExtractAggregateId returns UserId).
        RuleFor(c => c.UserId).NotEmpty();

        // The new email + token are carried by the confirmation link. Well-formed / present so a
        // malformed link is a clean 400 before UserManager runs.
        RuleFor(c => c.NewEmail).NotEmpty().EmailAddress().MaximumLength(MaxEmailLength);
        RuleFor(c => c.Token).NotEmpty();
    }
}
