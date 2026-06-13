using FluentValidation;

namespace Jobbliggaren.Application.Auth.Queries.VerifyCredentials;

public sealed class VerifyCredentialsQueryValidator : AbstractValidator<VerifyCredentialsQuery>
{
    public VerifyCredentialsQueryValidator()
    {
        RuleFor(q => q.Password).NotEmpty();
    }
}
