using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
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
        if (message is IAuthenticatedRequest && !currentUser.IsAuthenticated)
            throw new UnauthorizedException();

        return next(message, cancellationToken);
    }
}
