using JobbPilot.Application.Common.Abstractions;
using Mediator;

namespace JobbPilot.Application.Common.Behaviors;

public sealed class AuthorizationBehavior<TMessage, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        // TODO STEG 3: implementera riktig auth-logik efter auth-ADR.
        // ICurrentUser stub returnerar null/false (anonymous) — pass-through i STEG 2.
        _ = currentUser;
        return next(message, cancellationToken);
    }
}
