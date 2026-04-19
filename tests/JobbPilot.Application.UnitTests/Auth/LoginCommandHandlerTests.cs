using JobbPilot.Application.Auth.Commands.Login;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Auth;

public class LoginCommandHandlerTests
{
    private static LoginCommand ValidCommand() => new(
        Email: "klas@example.com",
        Password: "S3kret!pass");

    private static LoginCommandHandler CreateHandler(
        IUserAccountService? userAccountService = null,
        IJwtTokenGenerator? tokenGenerator = null,
        IRefreshTokenStore? refreshTokenStore = null)
    {
        userAccountService ??= Substitute.For<IUserAccountService>();
        tokenGenerator ??= Substitute.For<IJwtTokenGenerator>();
        refreshTokenStore ??= Substitute.For<IRefreshTokenStore>();
        return new LoginCommandHandler(userAccountService, tokenGenerator, refreshTokenStore);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsAuthTokens()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string> { "User" })));

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.GenerateTokens(userId, Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("access", "refresh",
                FakeDateTimeProvider.Default.UtcNow.AddMinutes(15),
                FakeDateTimeProvider.Default.UtcNow.AddDays(14)));
        tokenGenerator.HashToken("refresh").Returns("hash");

        var handler = CreateHandler(userAccountService: userAccountService, tokenGenerator: tokenGenerator);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access");
        result.Value.RefreshToken.ShouldBe("refresh");
    }

    [Fact]
    public async Task Handle_WithInvalidCredentials_ReturnsFailure()
    {
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserCredentials>(
                DomainError.Validation("Auth.InvalidCredentials", "E-post eller lösenord är felaktigt.")));

        var handler = CreateHandler(userAccountService: userAccountService);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task Handle_WithValidCredentials_StoresRefreshToken()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.GenerateTokens(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("a", "r", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));
        tokenGenerator.HashToken("r").Returns("hash");

        var refreshTokenStore = Substitute.For<IRefreshTokenStore>();
        var handler = CreateHandler(userAccountService, tokenGenerator, refreshTokenStore);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        await refreshTokenStore.Received(1).StoreAsync(
            userId, "hash", Arg.Any<DateTimeOffset>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
