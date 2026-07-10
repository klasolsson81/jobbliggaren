using FluentValidation;

namespace Jobbliggaren.Application.Auth.Commands.VerifyEmail;

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        // Uid.NotEmpty so a malformed request is a clean 400 and never reaches the post-success
        // AuditLogEntry.Create empty-aggregateId throw (ExtractAggregateId returns Uid).
        RuleFor(c => c.Uid).NotEmpty();

        // The token is carried by the confirmation link. Present so a malformed link is a clean 400
        // before UserManager runs.
        RuleFor(c => c.Token).NotEmpty();
    }
}
