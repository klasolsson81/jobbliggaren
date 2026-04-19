using JobbPilot.Application.Auth.Commands.Refresh;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Auth;

public class RefreshCommandHandlerTests
{
    private static RefreshCommandHandler CreateHandler(
        IRefreshTokenStore? refreshTokenStore = null,
        IUserAccountService? userAccountService = null,
        IJwtTokenGenerator? tokenGenerator = null)
    {
        refreshTokenStore ??= Substitute.For<IRefreshTokenStore>();
        userAccountService ??= Substitute.For<IUserAccountService>();
        tokenGenerator ??= Substitute.For<IJwtTokenGenerator>();
        return new RefreshCommandHandler(refreshTokenStore, userAccountService, tokenGenerator);
    }

    [Fact]
    public async Task Handle_WithValidToken_ReturnsNewTokens()
    {
        var userId = Guid.NewGuid();
        var storedToken = new StoredRefreshToken(Guid.NewGuid(), userId, "hash",
            FakeDateTimeProvider.Default.UtcNow.AddDays(14), IsActive: true);

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken("old-token").Returns("hash");
        tokenGenerator.GenerateTokens(userId, Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("new-access", "new-refresh",
                FakeDateTimeProvider.Default.UtcNow.AddMinutes(15),
                FakeDateTimeProvider.Default.UtcNow.AddDays(14)));
        tokenGenerator.HashToken("new-refresh").Returns("new-hash");

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindActiveByHashAsync("hash", Arg.Any<CancellationToken>())
            .Returns(storedToken);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetRolesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "User" });

        var handler = CreateHandler(refreshTokenStore, userAccountService, tokenGenerator);

        var result = await handler.Handle(new RefreshCommand("old-token"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("new-access");
    }

    [Fact]
    public async Task Handle_WhenTokenNotFound_ReturnsFailure()
    {
        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken(Arg.Any<string>()).Returns("hash");

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindActiveByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((StoredRefreshToken?)null);

        var handler = CreateHandler(refreshTokenStore, tokenGenerator: tokenGenerator);

        var result = await handler.Handle(new RefreshCommand("expired-token"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidRefreshToken");
    }

    [Fact]
    public async Task Handle_WithEmptyToken_ReturnsFailure()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(new RefreshCommand(""), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidRefreshToken");
    }

    [Fact]
    public async Task Handle_WithValidToken_RevokesOldToken()
    {
        var storedId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var storedToken = new StoredRefreshToken(storedId, userId, "hash",
            DateTimeOffset.UtcNow.AddDays(14), IsActive: true);

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken(Arg.Any<string>()).Returns("hash", "new-hash");
        tokenGenerator.GenerateTokens(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("a", "r", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindActiveByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(storedToken);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var handler = CreateHandler(refreshTokenStore, userAccountService, tokenGenerator);

        await handler.Handle(new RefreshCommand("old-token"), CancellationToken.None);

        await refreshTokenStore.Received(1).RevokeAsync(storedId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
