using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.RefreshSession;

public sealed class RefreshSessionCommandHandler(
    ICurrentUser currentUser,
    ISessionStore sessionStore)
    : ICommandHandler<RefreshSessionCommand, Result<RefreshSessionResult>>
{
    public async ValueTask<Result<RefreshSessionResult>> Handle(
        RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        // The endpoint requires authentication, so a validated session already slid via
        // the auth pipeline's GetAsync. If SessionId is somehow absent, there is nothing
        // to rotate.
        if (currentUser.SessionId is not { } sessionId)
            return Result.Success(new RefreshSessionResult(false, null, null));

        var rotation = await sessionStore.RotateAsync(sessionId, cancellationToken);

        return Result.Success(rotation is { } r
            ? new RefreshSessionResult(true, r.NewId.Reveal(), r.ExpiresAt)
            : new RefreshSessionResult(false, null, null));
    }
}
