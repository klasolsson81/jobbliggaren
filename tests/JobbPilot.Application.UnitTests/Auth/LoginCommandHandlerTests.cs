using JobbPilot.Application.Auth.Commands.Login;
using JobbPilot.Application.Auth.Dtos;
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
        ISessionStore? sessionStore = null,
        IAuthAuditLogger? auditLogger = null)
    {
        userAccountService ??= Substitute.For<IUserAccountService>();
        sessionStore ??= Substitute.For<ISessionStore>();
        auditLogger ??= Substitute.For<IAuthAuditLogger>();
        return new LoginCommandHandler(userAccountService, sessionStore, auditLogger);
    }

    [Fact]
    public async Task Handle_WithValidCredentials_ReturnsSessionId()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string> { "User" })));

        var sessionStore = Substitute.For<ISessionStore>();
        var sessionId = SessionId.Generate();
        sessionStore.CreateAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new Session(sessionId, userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));

        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SessionId.ShouldBe(sessionId.Reveal());
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
    public async Task Handle_WithValidCredentials_CreatesSession()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));

        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        await sessionStore.Received(1).CreateAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithValidCredentials_EmitsAuditLog()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));

        var auditLogger = Substitute.For<IAuthAuditLogger>();
        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore, auditLogger: auditLogger);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        auditLogger.Received(1).LoginSucceeded(userId, Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WithInvalidCredentials_EmitsLoginFailedAudit()
    {
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserCredentials>(
                DomainError.Validation("Auth.InvalidCredentials", "Fel.")));

        var auditLogger = Substitute.For<IAuthAuditLogger>();
        var handler = CreateHandler(userAccountService: userAccountService, auditLogger: auditLogger);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        auditLogger.Received(1).LoginFailed(Arg.Any<string>());
    }
}
