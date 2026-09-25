using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Commands.StartExternalLogin;

/// <summary>
/// #1744 — mints the flow (ADR 0142 D8): a PKCE verifier kept in the protected record, the state that keys it, and
/// the provider's authorization URL carrying the S256 challenge. Reads no account; a provider not registered here
/// is not found, and a start past <see cref="ExternalLoginPolicy.StartBudget"/> writes nothing.
/// </summary>
public sealed partial class StartExternalLoginCommandHandler(
    RegisteredProviders providers,
    IOAuthStateStore states,
    IRateBudget budget,
    ILogger<StartExternalLoginCommandHandler> logger)
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

        if (!await budget.TryConsumeAsync(
                ExternalLoginPolicy.StartBudget, ExternalLoginPolicy.StartBudgetSubject, cancellationToken))
        {
            LogStartsExhausted(logger, provider.Key.Value);
            return Result.Failure<ExternalLoginStart>(DomainError.Validation(
                AuthErrorCodes.ExternalLoginStartsExhausted, AuthErrorCodes.ExternalLoginStartsExhaustedMessage));
        }

        var verifier = PkceVerifier.Generate();
        var state = await states.PutAsync(
            new OAuthFlow(provider.Key, verifier, command.Next), cancellationToken);

        return Result.Success(new ExternalLoginStart(provider.BuildAuthorizeUrl(state, verifier.ToChallenge()), state));
    }

    [LoggerMessage(1027, LogLevel.Warning,
        "External login start refused: every start of the current window is spent (Provider={Provider})")]
    private static partial void LogStartsExhausted(ILogger logger, string provider);
}
