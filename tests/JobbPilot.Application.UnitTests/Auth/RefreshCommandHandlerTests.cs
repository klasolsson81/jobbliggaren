using JobbPilot.Application.Auth.Commands.Refresh;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

#pragma warning disable JOBBPILOT0001 // JWT refresh bevaras tills Fas 1, ADR 0017
namespace JobbPilot.Application.UnitTests.Auth;

public class RefreshCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private static RefreshCommandHandler CreateHandler(
        IRefreshTokenStore? refreshTokenStore = null,
        IUserAccountService? userAccountService = null,
        IJwtTokenGenerator? tokenGenerator = null)
    {
        refreshTokenStore ??= Substitute.For<IRefreshTokenStore>();
        userAccountService ??= Substitute.For<IUserAccountService>();
        tokenGenerator ??= Substitute.For<IJwtTokenGenerator>();
        return new RefreshCommandHandler(
            refreshTokenStore, userAccountService, tokenGenerator,
            Clock, NullLogger<RefreshCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithValidToken_ReturnsNewTokens()
    {
        var userId = Guid.NewGuid();
        var storedToken = new StoredRefreshToken(Guid.NewGuid(), userId, "hash",
            Clock.UtcNow.AddDays(14), RevokedAt: null);

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken("old-token").Returns("hash");
        tokenGenerator.GenerateTokens(userId, Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("new-access", "new-refresh",
                Clock.UtcNow.AddMinutes(15),
                Clock.UtcNow.AddDays(14)));
        tokenGenerator.HashToken("new-refresh").Returns("new-hash");

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByHashAsync("hash", Arg.Any<CancellationToken>())
            .Returns(storedToken);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetRolesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "User" });
        userAccountService.GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns("user@example.com");

        var handler = CreateHandler(refreshTokenStore, userAccountService, tokenGenerator);

        var result = await handler.Handle(new RefreshCommand("old-token"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("new-access");
    }

    [Fact]
    public async Task Handle_WithValidToken_PassesEmailToGenerateTokens()
    {
        var userId = Guid.NewGuid();
        const string email = "test@example.com";
        var storedToken = new StoredRefreshToken(Guid.NewGuid(), userId, "hash",
            Clock.UtcNow.AddDays(14), RevokedAt: null);

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken(Arg.Any<string>()).Returns("hash", "new-hash");
        tokenGenerator.GenerateTokens(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("a", "r", Clock.UtcNow.AddMinutes(15), Clock.UtcNow.AddDays(14)));

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(storedToken);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        userAccountService.GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns(email);

        var handler = CreateHandler(refreshTokenStore, userAccountService, tokenGenerator);

        await handler.Handle(new RefreshCommand("any-token"), CancellationToken.None);

        tokenGenerator.Received(1).GenerateTokens(userId, email, Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task Handle_WhenTokenNotFound_ReturnsFailure()
    {
        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken(Arg.Any<string>()).Returns("hash");

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
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
            Clock.UtcNow.AddDays(14), RevokedAt: null);

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken(Arg.Any<string>()).Returns("hash", "new-hash");
        tokenGenerator.GenerateTokens(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("a", "r", Clock.UtcNow.AddMinutes(15), Clock.UtcNow.AddDays(14)));

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(storedToken);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.GetRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        userAccountService.GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("u@example.com");

        var handler = CreateHandler(refreshTokenStore, userAccountService, tokenGenerator);

        await handler.Handle(new RefreshCommand("old-token"), CancellationToken.None);

        await refreshTokenStore.Received(1).RevokeAsync(storedId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithRevokedToken_RevokesEntireChainAndReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var revokedToken = new StoredRefreshToken(Guid.NewGuid(), userId, "hash",
            Clock.UtcNow.AddDays(14), RevokedAt: Clock.UtcNow.AddMinutes(-5));

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken("revoked-token").Returns("hash");

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByHashAsync("hash", Arg.Any<CancellationToken>())
            .Returns(revokedToken);

        var handler = CreateHandler(refreshTokenStore, tokenGenerator: tokenGenerator);

        var result = await handler.Handle(new RefreshCommand("revoked-token"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidRefreshToken");
        await refreshTokenStore.Received(1).RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithExpiredToken_ReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var expiredToken = new StoredRefreshToken(Guid.NewGuid(), userId, "hash",
            Clock.UtcNow.AddDays(-1), RevokedAt: null);

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.HashToken("expired-token").Returns("hash");

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        refreshTokenStore.FindByHashAsync("hash", Arg.Any<CancellationToken>())
            .Returns(expiredToken);

        var handler = CreateHandler(refreshTokenStore, tokenGenerator: tokenGenerator);

        var result = await handler.Handle(new RefreshCommand("expired-token"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidRefreshToken");
        await refreshTokenStore.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
