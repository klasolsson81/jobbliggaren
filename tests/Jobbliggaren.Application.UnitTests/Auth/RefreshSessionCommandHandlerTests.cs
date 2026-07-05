using Jobbliggaren.Application.Auth.Commands.RefreshSession;
using Jobbliggaren.Application.Common.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class RefreshSessionCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();

    private RefreshSessionCommandHandler CreateHandler() => new(_currentUser, _sessionStore);

    [Fact]
    public async Task Handle_ShouldReturnRotatedWithNewId_WhenRotationIsDue()
    {
        var sessionId = SessionId.FromRaw("current-id");
        var newId = SessionId.FromRaw("rotated-id");
        var expiresAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        _currentUser.SessionId.Returns(sessionId);
        _sessionStore.RotateAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new SessionRotation(newId, expiresAt));

        var result = await CreateHandler().Handle(
            new RefreshSessionCommand(), TestContext.Current.CancellationToken);

        result.Value.Rotated.ShouldBeTrue();
        result.Value.SessionId.ShouldBe("rotated-id");
        result.Value.ExpiresAt.ShouldBe(expiresAt);
    }

    [Fact]
    public async Task Handle_ShouldReturnNotRotated_WhenRotationNotDue()
    {
        var sessionId = SessionId.FromRaw("current-id");
        _currentUser.SessionId.Returns(sessionId);
        _sessionStore.RotateAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns((SessionRotation?)null);

        var result = await CreateHandler().Handle(
            new RefreshSessionCommand(), TestContext.Current.CancellationToken);

        result.Value.Rotated.ShouldBeFalse();
        result.Value.SessionId.ShouldBeNull();
        result.Value.ExpiresAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNotRotatedAndNotCallStore_WhenNoSession()
    {
        _currentUser.SessionId.Returns((SessionId?)null);

        var result = await CreateHandler().Handle(
            new RefreshSessionCommand(), TestContext.Current.CancellationToken);

        result.Value.Rotated.ShouldBeFalse();
        await _sessionStore.DidNotReceive().RotateAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }
}
