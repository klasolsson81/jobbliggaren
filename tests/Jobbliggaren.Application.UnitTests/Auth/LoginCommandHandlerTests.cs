using Jobbliggaren.Application.Auth.Commands.Login;
using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

public class LoginCommandHandlerTests
{
    private static LoginCommand ValidCommand() => new(
        Email: "klas@example.com",
        Password: "S3kret!pass");

    private static LoginCommandHandler CreateHandler(
        IAppDbContext? db = null,
        IUserAccountService? userAccountService = null,
        ISessionStore? sessionStore = null,
        IAuthAuditLogger? auditLogger = null)
    {
        // Default: tom InMemory-context — JobSeekers.FirstOrDefaultAsync returnerar
        // null → D5-blockering (Auth.AccountPendingDeletion) inte triggad.
        // Tester som verifierar D5-blockering skapar egen context med soft-deletad
        // JobSeeker.
        db ??= TestAppDbContextFactory.Create();
        userAccountService ??= Substitute.For<IUserAccountService>();
        sessionStore ??= Substitute.For<ISessionStore>();
        auditLogger ??= Substitute.For<IAuthAuditLogger>();
        return new LoginCommandHandler(db, userAccountService, sessionStore, auditLogger);
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
        sessionStore.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
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
        sessionStore.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));

        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        await sessionStore.Received(1).CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>());
    }

    // #2b2 / #2b3b activation: "Håll mig inloggad" checked → Persistent; unchecked/absent →
    // the short session-scoped Session (the safe-default flip — no login lands on Legacy any
    // more; existing Legacy sessions are untouched).
    [Fact]
    public async Task Handle_WithRememberMe_CreatesPersistentSession()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30)));

        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand() with { RememberMe = true }, CancellationToken.None);

        await sessionStore.Received(1).CreateAsync(userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutRememberMe_CreatesSessionScopedSession()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));

        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        // Activation flip: unticked "Håll mig inloggad" no longer lands on Legacy — it is the
        // short, session-scoped Session profile (dies on browser close, Art. 25(2) safe default).
        await sessionStore.Received(1).CreateAsync(userId, SessionLifetime.Session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithValidCredentials_EmitsAuditLog()
    {
        var userId = Guid.NewGuid();
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
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
        // #503 G3(b): an ordinary wrong password is NOT a lockout — the dedicated
        // account_locked_out event must not be emitted here.
        auditLogger.DidNotReceive().AccountLockedOut(Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WithLockedOutAccount_EmitsAccountLockedOutAudit_NotLoginFailed()
    {
        // #503 G3(b): a locked account (ValidateCredentialsAsync -> Auth.AccountLocked)
        // must emit the dedicated attack signal, NOT the generic login_failed.
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserCredentials>(
                DomainError.Validation("Auth.AccountLocked", "E-post eller lösenord är felaktigt.")));

        var auditLogger = Substitute.For<IAuthAuditLogger>();
        var handler = CreateHandler(userAccountService: userAccountService, auditLogger: auditLogger);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        auditLogger.Received(1).AccountLockedOut(Arg.Any<string>());
        auditLogger.DidNotReceive().LoginFailed(Arg.Any<string>());
    }

    // ─── ADR 0024 D5: D5-blockering vid soft-deletad JobSeeker ───

    [Fact]
    public async Task Handle_WithSoftDeletedJobSeeker_ReturnsInvalidCredentials_NotPendingDeletion()
    {
        // Sec-1-fix (security-auditor STEG 10b): soft-deletad konto returnerar
        // SAMMA fel som okänd email / fel lösen för att undvika information disclosure.
        var userId = Guid.NewGuid();

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var clock = FakeDateTimeProvider.Default;
        var seeker = JobSeeker.Register(userId, "Soft Deleted User", clock).Value;
        seeker.SoftDelete(clock);

        var db = TestAppDbContextFactory.Create();
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sessionStore = Substitute.For<ISessionStore>();
        var handler = CreateHandler(db: db, userAccountService: userAccountService, sessionStore: sessionStore);

        var result = await handler.Handle(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidCredentials",
            "soft-deletad konto ska returnera samma felkod som okänd email/fel lösen — undviker info disclosure");

        // Session ska INTE skapas för soft-deletad konto
        await sessionStore.DidNotReceive().CreateAsync(Arg.Any<Guid>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithActiveJobSeeker_AllowsLoginAndCreatesSession()
    {
        // Verifierar att D5-checken inte blockerar normal login (regression-skydd
        // mot att vi blockerar för aggressivt).
        var userId = Guid.NewGuid();

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        var clock = FakeDateTimeProvider.Default;
        var seeker = JobSeeker.Register(userId, "Active User", clock).Value;
        // INTE soft-deletad

        var db = TestAppDbContextFactory.Create();
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));

        var handler = CreateHandler(db: db, userAccountService: userAccountService, sessionStore: sessionStore);

        var result = await handler.Handle(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await sessionStore.Received(1).CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>());
    }
}
