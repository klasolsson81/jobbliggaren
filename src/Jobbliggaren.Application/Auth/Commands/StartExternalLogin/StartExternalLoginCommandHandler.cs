using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.StartExternalLogin;

/// <summary>
/// #1744 — mints the flow (ADR 0142 D8): a PKCE verifier kept in the protected record, the state that keys it, and
/// the provider's authorization URL carrying the S256 challenge. Reads no account; a provider not registered here
/// is not found.
/// </summary>
public sealed class StartExternalLoginCommandHandler(RegisteredProviders providers, IOAuthStateStore states)
    : ICommandHandler<StartExternalLoginCommand, Result<ExternalLoginStart>>
{
    public async ValueTask<Result<ExternalLoginStart>> Handle(
        StartExternalLoginCommand command, CancellationToken cancellationToken)
    {
        if (providers.Find(command.Provider) is not { } provider)
        {
            return Result.Failure<ExternalLoginStart>(DomainError.NotFound(
                AuthErrorCodes.ExternalProviderUnknown, AuthErrorCodes.ExternalProviderUnknownMessage));
        }

        var verifier = PkceVerifier.Generate();
        var state = await states.PutAsync(
            new OAuthFlow(provider.Key, verifier, command.Next), cancellationToken);

        return Result.Success(new ExternalLoginStart(provider.BuildAuthorizeUrl(state, verifier.ToChallenge()), state));
    }
}
