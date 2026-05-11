using FluentValidation;

namespace JobbPilot.Application.Auth.Queries.VerifyCredentials;

public sealed class VerifyCredentialsQueryValidator : AbstractValidator<VerifyCredentialsQuery>
{
    public VerifyCredentialsQueryValidator()
    {
        RuleFor(q => q.Password).NotEmpty();
    }
}
