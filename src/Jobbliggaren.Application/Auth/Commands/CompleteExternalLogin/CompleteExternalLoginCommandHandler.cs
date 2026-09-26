using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Commands.CompleteExternalLogin;

/// <summary>
/// #1744 — completes a provider login (ADR 0142 D8), in this order: the provider must be registered, the state
/// must name a live flow started for THAT provider (taken once), and the provider must accept the code with the
/// flow's verifier. Only then is the identity read. An authoritative address reaches the outcome function the code
/// and the link share; an asserted one (#1745, Amendment (16)) opens a session only through a link a code bound, and
/// otherwise only chooses where a code is sent.
/// </summary>
public sealed partial class CompleteExternalLoginCommandHandler(
    RegisteredProviders providers,
    IOAuthStateStore states,
    LoginProofOutcome outcome,
    PendingLinkChallenge pendingLinks,
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
            ExternalExchange.Identified { Identity: var identity } => identity.Address switch
            {
                ExternalAddress.Authoritative authoritative => Decided(
                    await outcome.ResolveExternalAsync(
                        new ExternalLoginProof(authoritative.Email, identity.Provider, identity.Subject),
                        cancellationToken),
                    flow.Next),
                ExternalAddress.Asserted asserted => await AssertedAsync(
                    new AssertedLoginProof(asserted.Email, identity.Provider, identity.Subject),
                    flow.Next,
                    cancellationToken),
                _ => Unusable(),
            },

            // Nothing is linked and no grant is issued.
            ExternalExchange.AddressRefused => Result.Failure<ExternalLoginCompletion>(DomainError.Validation(
                AuthErrorCodes.ExternalEmailUnverified, AuthErrorCodes.ExternalEmailUnverifiedMessage)),
            ExternalExchange.Failed => Unusable(),
            _ => Unusable(),
        };
    }

    // A found link decides as Google's login does; without one, nothing about any account is read, and the answer is
    // the code step whether the address has an account or not.
    private async Task<Result<ExternalLoginCompletion>> AssertedAsync(
        AssertedLoginProof proof, string? next, CancellationToken ct)
    {
        if (await outcome.ResolveFoundLinkAsync(proof, ct) is { } decided)
            return Decided(decided, next);

        var requested = await pendingLinks.RequestAsync(proof, ct);
        return requested.IsFailure
            ? Result.Failure<ExternalLoginCompletion>(requested.Error)
            : Result.Success<ExternalLoginCompletion>(new ExternalLoginCompletion.CodeRequired(
                requested.Value.ChallengeId, requested.Value.LinkGrant, proof.Address, next));
    }

    private static Result<ExternalLoginCompletion> Decided(LoginOutcome decided, string? next) =>
        Result.Success<ExternalLoginCompletion>(new ExternalLoginCompletion.Decided(decided, next));

    private static Result<ExternalLoginCompletion> Unusable() => Result.Failure<ExternalLoginCompletion>(
        DomainError.Gone(AuthErrorCodes.ExternalLoginUnusable, AuthErrorCodes.ExternalLoginUnusableMessage));

    // #1744 — the provider only: never the state, which is the browser's binding value.
    [LoggerMessage(1021, LogLevel.Warning,
        "External login refused: the state names no live flow for this provider (Provider={Provider})")]
    private static partial void LogStateUnusable(ILogger logger, string provider);
}
