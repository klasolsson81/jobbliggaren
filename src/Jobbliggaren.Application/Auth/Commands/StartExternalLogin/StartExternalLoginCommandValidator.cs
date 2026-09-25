using FluentValidation;
using Jobbliggaren.Application.Auth.ExternalLogins;

namespace Jobbliggaren.Application.Auth.Commands.StartExternalLogin;

public sealed class StartExternalLoginCommandValidator : AbstractValidator<StartExternalLoginCommand>
{
    public StartExternalLoginCommandValidator()
    {
        RuleFor(c => c.Provider).NotEmpty().MaximumLength(ExternalProviderKey.MaximumLength);

        // The path lands in the volatile Redis, which refuses writes when full, and later in a redirect: a bounded
        // same-site path or nothing.
        RuleFor(c => c.Next)
            .MaximumLength(ExternalLoginPolicy.MaxNextLength)
            .Must(IsSameSitePath)
            .When(c => !string.IsNullOrEmpty(c.Next))
            .WithMessage("Sökvägen efter inloggningen är inte giltig.");
    }

    private static bool IsSameSitePath(string? next) =>
        next is not null
        && next.StartsWith('/')
        && !next.StartsWith("//", StringComparison.Ordinal)
        && next.All(ch => !char.IsControl(ch) && ch != (char)92);
}
