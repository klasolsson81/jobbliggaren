using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;

/// <summary>
/// #1744 — completes a provider login (ADR 0142 D8), in this order: the provider must be registered, the state
/// must name a live flow started for THAT provider (taken once), and the provider must accept the code with the
/// flow's verifier. Only then is the identity read, and only a <see cref="VerifiedEmail"/> reaches the outcome
/// function the code and the link share.
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

        // The adapter logs why a null came back; every cause is one answer here.
        var identity = await provider.ExchangeAsync(
            AuthorizationCode.FromRaw(command.Code!), flow.Verifier, cancellationToken);
        if (identity is null)
            return Unusable();

        // The adapter logged which rule refused the address. Nothing is linked and no grant is issued.
        if (identity.Email is not { } email)
        {
            return Result.Failure<ExternalLoginCompletion>(DomainError.Validation(
                AuthErrorCodes.ExternalEmailUnverified, AuthErrorCodes.ExternalEmailUnverifiedMessage));
        }

        var result = await outcome.ResolveExternalAsync(
            new ExternalLoginProof(email, identity.Provider, identity.Subject), cancellationToken);
        return Result.Success(new ExternalLoginCompletion(result, flow.Next));
    }

    private static Result<ExternalLoginCompletion> Unusable() => Result.Failure<ExternalLoginCompletion>(
        DomainError.Gone(AuthErrorCodes.ExternalLoginUnusable, AuthErrorCodes.ExternalLoginUnusableMessage));

    // #1744 — the provider only: never the state, which is the browser's binding value.
    [LoggerMessage(1021, LogLevel.Warning,
        "External login refused: the state names no live flow for this provider (Provider={Provider})")]
    private static partial void LogStateUnusable(ILogger logger, string provider);
}
