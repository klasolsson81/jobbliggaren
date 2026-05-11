using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Authorization;
using JobbPilot.Application.Common.Exceptions;
using Mediator;

namespace JobbPilot.Application.Common.Behaviors;

/// <summary>
/// Pipeline-behavior som verifierar Admin-roll för commands/queries markerade
/// med <see cref="IAdminRequest"/>. Defense-in-depth bakom HTTP-policyn
/// <c>RequireRole("Admin")</c> — fångar Mediator-anrop som inte går via
/// HTTP (Worker-jobb, integration-tester, framtida CLI). Per ADR 0008
/// pipeline-disciplin: roll-check körs INNAN UnitOfWork öppnas så att
/// 403-fall inte rör databasen.
///
/// Kastar <see cref="ForbiddenException"/> — Api-middleware mappar till 403.
/// </summary>
public sealed class AdminAuthorizationBehavior<TMessage, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (message is IAdminRequest && !currentUser.IsInRole(Roles.Admin))
            throw new ForbiddenException();

        return next(message, cancellationToken);
    }
}
