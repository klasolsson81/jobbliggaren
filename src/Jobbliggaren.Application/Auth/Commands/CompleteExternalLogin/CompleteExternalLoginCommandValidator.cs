using FluentValidation;
using Jobbliggaren.Application.Auth.ExternalLogins;

namespace Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;

public sealed class CompleteExternalLoginCommandValidator : AbstractValidator<CompleteExternalLoginCommand>
{
    public CompleteExternalLoginCommandValidator()
    {
        RuleFor(c => c.Provider).NotEmpty().MaximumLength(ExternalProviderKey.MaximumLength);
        RuleFor(c => c.Code).NotEmpty().MaximumLength(ExternalLoginPolicy.MaxCodeLength);

        // Exactly the shape a minted state has, checked before anything reaches Redis.
        RuleFor(c => c.State).NotEmpty().Length(OAuthState.EncodedLength).Must(IsBase64Url);
    }

    private static bool IsBase64Url(string? value) =>
        value is not null && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
}
