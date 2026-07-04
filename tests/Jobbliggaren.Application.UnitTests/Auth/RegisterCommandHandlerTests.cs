using Jobbliggaren.Application.Auth.Commands.Register;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class RegisterCommandHandlerTests
{
    private static RegisterCommand ValidCommand() => new(
        Email: "klas@example.com",
        Password: "S3kret!pass",
        DisplayName: "Klas Olsson");

    private static RegisterCommandHandler CreateHandler(
        IAppDbContext? db = null,
        IUserAccountService? userAccountService = null,
        ISessionStore? sessionStore = null,
        IAuthAuditLogger? auditLogger = null)
    {
        if (db is null)
        {
            db = Substitute.For<IAppDbContext>();
            db.JobSeekers.Returns(Substitute.For<DbSet<JobSeeker>>());
        }

        userAccountService ??= Substitute.For<IUserAccountService>();
        sessionStore ??= Substitute.For<ISessionStore>();
        auditLogger ??= Substitute.For<IAuthAuditLogger>();

        return new RegisterCommandHandler(db, userAccountService, sessionStore, auditLogger, FakeDateTimeProvider.Default);
    }

    private static ISessionStore DefaultSessionStore(Guid userId)
    {
        var store = Substitute.For<ISessionStore>();
        store.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));
        return store;
    }

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSessionId()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(userId));

        var sessionId = SessionId.Generate();
        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(new Session(sessionId, userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));

        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SessionId.ShouldBe(sessionId.Reveal());
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

        var handler = CreateHandler(db: db, userAccountService: userAccountService, sessionStore: DefaultSessionStore(userId));

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

        var result = await new RegisterCommandHandler(
            Substitute.For<IAppDbContext>(),
            userAccountService,
            Substitute.For<ISessionStore>(),
            Substitute.For<IAuthAuditLogger>(),
            FakeDateTimeProvider.Default)
            .Handle(new RegisterCommand("klas@example.com", "S3kret!pass", "   "), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await userAccountService.Received(1).DeleteUserAsync(userId, Arg.Any<CancellationToken>());
    }
}
