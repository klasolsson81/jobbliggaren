using JobbPilot.Application.Auth.Commands.Logout;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Configuration;
using Microsoft.Extensions.Options;
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

        var jwtSettings = Options.Create(new JwtSettings { AccessTokenLifetimeMinutes = 15 });
        var handler = new LogoutCommandHandler(currentUser, refreshTokenStore, revocationStore, jwtSettings);

        var result = await handler.Handle(new LogoutCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await revocationStore.Received(1).RevokeAsync(jti, TimeSpan.FromMinutes(15), Arg.Any<CancellationToken>());
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
        var jwtSettings = Options.Create(new JwtSettings { AccessTokenLifetimeMinutes = 15 });

        var handler = new LogoutCommandHandler(currentUser, refreshTokenStore, revocationStore, jwtSettings);

        await handler.Handle(new LogoutCommand(), CancellationToken.None);

        await revocationStore.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }
}
