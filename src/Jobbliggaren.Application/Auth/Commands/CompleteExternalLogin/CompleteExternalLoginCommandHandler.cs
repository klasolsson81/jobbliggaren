using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;

/// <summary>
/// #1744 — completes a provider login (ADR 0142 D8), in this order: the provider must be registered, the state
/// must name a live flow started for THAT provider (taken once), and the provider must accept the code. Only then is
/// the identity read, and only the <see cref="VerifiedEmail"/> the adapter admitted
/// reaches the outcome function the code and the link share.
/// </summary>
public sealed partial class CompleteExternalLoginCommandHandler(
    RegisteredProviders providers,
    IOAuthStateStore states,
    LoginProofOutcome outcome,
    ILogger<CompleteExternalLoginCommandHandler> logger)
    : ICommandHandler<CompleteExternalLoginCommand, Result<ExternalLoginCompletion>>
{
    public async ValueTask<Result<ExternalLoginCompletion>> Handle(
        CompleteExternalLoginCommand command, CancellationToken cancellationToken)
    {
        // An unregistered provider leaves the state untouched: nothing was started for it on this host.
        if (providers.Find(command.Provider) is not { } provider)
        {
            return Result.Failure<ExternalLoginCompletion>(DomainError.NotFound(
                AuthErrorCodes.ExternalProviderUnknown, AuthErrorCodes.ExternalProviderUnknownMessage));
        }

        var flow = await states.TakeAsync(OAuthState.FromRaw(command.State!), provider.Key, cancellationToken);
        if (flow is null)
        {
            LogStateUnusable(logger, provider.Key.Value);
            return Unusable();
        }

        // The adapter logged the cause of a refusal; every cause of each kind is one answer here.
        var exchange = await provider.ExchangeAsync(
            AuthorizationCode.FromRaw(command.Code!), flow.Verifier, cancellationToken);

        return exchange switch
        {
            ExternalExchange.Identified { Identity: var identity } => Result.Success(new ExternalLoginCompletion(
                await outcome.ResolveExternalAsync(
                    new ExternalLoginProof(identity.Email, identity.Provider, identity.Subject), cancellationToken),
                flow.Next)),

            // Nothing is linked and no grant is issued.
            ExternalExchange.AddressRefused => Result.Failure<ExternalLoginCompletion>(DomainError.Validation(
                AuthErrorCodes.ExternalEmailUnverified, AuthErrorCodes.ExternalEmailUnverifiedMessage)),
            ExternalExchange.Failed => Unusable(),
            _ => Unusable(),
        };
    }

    private static Result<ExternalLoginCompletion> Unusable() => Result.Failure<ExternalLoginCompletion>(
        DomainError.Gone(AuthErrorCodes.ExternalLoginUnusable, AuthErrorCodes.ExternalLoginUnusableMessage));

    // #1744 — the provider only: never the state, which is the browser's binding value.
    [LoggerMessage(1021, LogLevel.Warning,
        "External login refused: the state names no live flow for this provider (Provider={Provider})")]
    private static partial void LogStateUnusable(ILogger logger, string provider);
}
