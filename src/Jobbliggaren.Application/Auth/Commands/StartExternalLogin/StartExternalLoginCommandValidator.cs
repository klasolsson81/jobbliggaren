using FluentValidation;
using Jobbliggaren.Application.Auth.ExternalLogins;

namespace Jobbliggaren.Application.Auth.Commands.StartExternalLogin;

public sealed class StartExternalLoginCommandValidator : AbstractValidator<StartExternalLoginCommand>
{
    public StartExternalLoginCommandValidator()
    {
        RuleFor(c => c.Provider).NotEmpty().MaximumLength(32);

        // The path lands in the volatile Redis, which refuses writes when full, and later in a redirect: a bounded
        // same-site path or nothing. The web has already made it safe; this refuses anything that is not.
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
        && next.All(ch => ch >= ' ' && ch != (char)127 && ch != (char)92);
}
