using JobbPilot.Application.Auth.Commands.Logout;
using JobbPilot.Application.Common.Abstractions;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Auth;

public class LogoutCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenAuthenticated_RevokesJtiAndAllRefreshTokens()
    {
        var userId = Guid.NewGuid();
        var jti = "some-jti";

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Jti.Returns(jti);

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        var revocationStore = Substitute.For<IAccessTokenRevocationStore>();

        var handler = new LogoutCommandHandler(currentUser, refreshTokenStore, revocationStore);

        var result = await handler.Handle(new LogoutCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await revocationStore.Received(1).RevokeAsync(jti, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await refreshTokenStore.Received(1).RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenJtiIsNull_SkipsAccessTokenRevocation()
    {
        var userId = Guid.NewGuid();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Jti.Returns((string?)null);

        var revocationStore = Substitute.For<IAccessTokenRevocationStore>();
        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();

        var handler = new LogoutCommandHandler(currentUser, refreshTokenStore, revocationStore);

        await handler.Handle(new LogoutCommand(), CancellationToken.None);

        await revocationStore.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }
}
