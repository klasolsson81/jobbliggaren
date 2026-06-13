using Jobbliggaren.Application.Auth.Commands.Logout;
using Jobbliggaren.Application.Common.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class LogoutCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenSessionExists_InvalidatesSession()
    {
        var userId = Guid.NewGuid();
        var sessionId = SessionId.Generate();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.SessionId.Returns(sessionId);

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.InvalidateAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new LogoutCommandHandler(currentUser, sessionStore, Substitute.For<IAuthAuditLogger>());

        var result = await handler.Handle(new LogoutCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await sessionStore.Received(1).InvalidateAsync(sessionId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSessionAlreadyGone_StillReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var sessionId = SessionId.Generate();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.SessionId.Returns(sessionId);

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.InvalidateAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(false); // Already gone — race condition

        var handler = new LogoutCommandHandler(currentUser, sessionStore, Substitute.For<IAuthAuditLogger>());

        var result = await handler.Handle(new LogoutCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WhenAuthenticated_EmitsLogoutAudit()
    {
        var userId = Guid.NewGuid();
        var sessionId = SessionId.Generate();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.SessionId.Returns(sessionId);

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.InvalidateAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>()).Returns(true);

        var auditLogger = Substitute.For<IAuthAuditLogger>();
        var handler = new LogoutCommandHandler(currentUser, sessionStore, auditLogger);

        await handler.Handle(new LogoutCommand(), CancellationToken.None);

        auditLogger.Received(1).LogoutSucceeded(userId, Arg.Any<string>());
    }
}
