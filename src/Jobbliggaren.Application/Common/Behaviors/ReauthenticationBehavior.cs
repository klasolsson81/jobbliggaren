using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Mediator;

namespace Jobbliggaren.Application.Common.Behaviors;

/// <summary>
/// Enforces server-side re-authentication for every <see cref="IReauthenticatingRequest"/> (C5,
/// epik #481). On a failed password it throws <see cref="ReauthenticationFailedException"/> — mapped
/// centrally to a byte-identical 401 (mirrors how <c>AuthorizationBehavior</c>/<c>ValidationBehavior</c>
/// gate) — so the handler, UnitOfWork commit and audit row are all skipped. Placed after
/// authorization (the actor is known) and before FieldEncryptionKeyPrefetch/UnitOfWork/Audit (a
/// failed re-auth prefetches no DEK, commits nothing, writes no audit row).
/// </summary>
public sealed class ReauthenticationBehavior<TMessage, TResponse>(
    IEnumerable<IReauthenticationService> reauthenticationServices)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    // IEnumerable, not a direct dependency: re-auth is an Api-only concern, but this behavior lives
    // in the SHARED pipeline (MediatorPipelineBehaviors.InOrder, used by both the Api and the
    // HTTP-free Worker) so it is constructed for every message in both hosts. The Api composition
    // registers exactly one IReauthenticationService (it has ISessionStore/ICurrentUser); the Worker
    // registers none (ADR 0023 — no session store, and no Worker message is an
    // IReauthenticatingRequest). Injecting the sequence lets the behavior construct in the Worker
    // (empty → the guard below never fires) without forcing a session store into it.
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (message is IReauthenticatingRequest reauthenticating)
        {
            var service = reauthenticationServices.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "A re-authenticating request reached a host with no IReauthenticationService " +
                    "registered. Re-auth is only supported in the Api composition.");

            var result = await service.VerifyCurrentUserPasswordAsync(
                reauthenticating.Password, cancellationToken);

            if (result.IsFailure)
                throw new ReauthenticationFailedException();
        }

        return await next(message, cancellationToken);
    }
}
