using JobbPilot.Application.Auth.Commands.Register;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Auth;

public class RegisterCommandHandlerTests
{
    private static RegisterCommand ValidCommand() => new(
        Email: "klas@example.com",
        Password: "S3kret!pass",
        DisplayName: "Klas Olsson");

    private static RegisterCommandHandler CreateHandler(
        IAppDbContext? db = null,
        IUserAccountService? userAccountService = null,
        IJwtTokenGenerator? tokenGenerator = null,
        IRefreshTokenStore? refreshTokenStore = null)
    {
        if (db is null)
        {
            db = Substitute.For<IAppDbContext>();
            db.JobSeekers.Returns(Substitute.For<DbSet<JobSeeker>>());
        }

        userAccountService ??= Substitute.For<IUserAccountService>();
        tokenGenerator ??= Substitute.For<IJwtTokenGenerator>();
        refreshTokenStore ??= Substitute.For<IRefreshTokenStore>();

        return new RegisterCommandHandler(db, userAccountService, tokenGenerator, refreshTokenStore, FakeDateTimeProvider.Default);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsAuthTokens()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(userId));
        userAccountService.GetRolesAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "User" });

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.GenerateTokens(userId, Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("access", "refresh", FakeDateTimeProvider.Default.UtcNow.AddMinutes(15), FakeDateTimeProvider.Default.UtcNow.AddDays(14)));
        tokenGenerator.HashToken("refresh").Returns("hash");

        var handler = CreateHandler(userAccountService: userAccountService, tokenGenerator: tokenGenerator);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access");
    }

    [Fact]
    public async Task Handle_WhenCreateUserFails_ReturnsFailure()
    {
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(DomainError.Validation("Auth.DuplicateEmail", "E-postadressen används redan.")));

        var handler = CreateHandler(userAccountService: userAccountService);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.DuplicateEmail");
    }

    [Fact]
    public async Task Handle_WithValidCommand_AddsJobSeekerToDb()
    {
        var userId = Guid.NewGuid();
        var db = Substitute.For<IAppDbContext>();
        var seekerSet = Substitute.For<DbSet<JobSeeker>>();
        db.JobSeekers.Returns(seekerSet);

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(userId));
        userAccountService.GetRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var tokenGenerator = Substitute.For<IJwtTokenGenerator>();
        tokenGenerator.GenerateTokens(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new GeneratedTokens("a", "r", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        tokenGenerator.HashToken(Arg.Any<string>()).Returns("hash");

        var handler = CreateHandler(db: db, userAccountService: userAccountService, tokenGenerator: tokenGenerator);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        seekerSet.Received(1).Add(Arg.Any<JobSeeker>());
    }

    [Fact]
    public async Task Handle_WhenJobSeekerCreationFails_DeletesUserAndReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(userId));
        userAccountService.GetRolesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        var result = await new RegisterCommandHandler(
            Substitute.For<IAppDbContext>(),
            userAccountService,
            Substitute.For<IJwtTokenGenerator>(),
            Substitute.For<IRefreshTokenStore>(),
            FakeDateTimeProvider.Default)
            .Handle(new RegisterCommand("klas@example.com", "S3kret!pass", "   "), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await userAccountService.Received(1).DeleteUserAsync(userId, Arg.Any<CancellationToken>());
    }
}
