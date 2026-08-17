using Jobbliggaren.Application.Auth;
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
        // Default: an EMPTY InMemory context, so JobSeekers.FirstOrDefaultAsync returns null.
        //
        // #1349 — that premise changed meaning. It used to mean "the D5 soft-delete block is not
        // triggered" and a login over it succeeded; a null profile now REFUSES the login, because an
        // Identity row with no JobSeeker is the orphan this guard exists to stop. So the default context
        // is the refusing one, and every test that asserts a session is issued seeds a profile through
        // DbWithActiveJobSeekerAsync below. Tests that exercise the D5 block still build their own
        // context with a soft-deleted JobSeeker.
        db ??= TestAppDbContextFactory.Create();
        userAccountService ??= Substitute.For<IUserAccountService>();
        sessionStore ??= Substitute.For<ISessionStore>();
        auditLogger ??= Substitute.For<IAuthAuditLogger>();
        return new LoginCommandHandler(db, userAccountService, sessionStore, auditLogger);
    }

    // #1349 — the premise of a SUCCESSFUL login: the account owns a profile. Built through
    // JobSeeker.Register, the same factory RegisterCommandHandler calls, so the seeded row is one
    // production actually produces rather than a hand-built aggregate.
    private static async Task<IAppDbContext> DbWithActiveJobSeekerAsync(Guid userId, CancellationToken ct)
    {
        var db = TestAppDbContextFactory.Create();
        db.JobSeekers.Add(JobSeeker.Register(userId, "Aktiv användare", FakeDateTimeProvider.Default).Value);
        await db.SaveChangesAsync(ct);
        return db;
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

        var ct = TestContext.Current.CancellationToken;
        var handler = CreateHandler(
            db: await DbWithActiveJobSeekerAsync(userId, ct),
            userAccountService: userAccountService, sessionStore: sessionStore);

        var result = await handler.Handle(ValidCommand(), ct);

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

        var ct = TestContext.Current.CancellationToken;
        var handler = CreateHandler(
            db: await DbWithActiveJobSeekerAsync(userId, ct),
            userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand(), ct);

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

        var ct = TestContext.Current.CancellationToken;
        var handler = CreateHandler(
            db: await DbWithActiveJobSeekerAsync(userId, ct),
            userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand() with { RememberMe = true }, ct);

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

        var ct = TestContext.Current.CancellationToken;
        var handler = CreateHandler(
            db: await DbWithActiveJobSeekerAsync(userId, ct),
            userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand(), ct);

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
        var ct = TestContext.Current.CancellationToken;
        var handler = CreateHandler(
            db: await DbWithActiveJobSeekerAsync(userId, ct),
            userAccountService: userAccountService, sessionStore: sessionStore, auditLogger: auditLogger);

        await handler.Handle(ValidCommand(), ct);

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

    // ─── #1349: an Identity row with NO JobSeeker is refused at the capability seam ───

    [Fact]
    public async Task Handle_WithNoJobSeekerRow_ReturnsInvalidCredentials_AndCreatesNoSession()
    {
        // WHO PRODUCES THIS STATE (CLAUDE.md §5 Tests: — the premise is not produced by any src/ path
        // that this test can drive, so the actor is named rather than implied):
        //
        //   1. AccountHardDeleter.HardDeleteAccountAsync step 2h (AccountHardDeleter.cs:302-309). The
        //      domain transaction commits FIRST and the Identity DELETE is a separate boundary after it,
        //      so a failure there leaves an Identity row whose JobSeeker is already gone — and that row
        //      is EmailConfirmed with a working password. The actor's own code admits the state in
        //      writing: "Om denna failer plockas raden upp av Steg 0 (CleanupIdentityOrphansAsync) i
        //      nästa körning." Step 0 reaps it on the NEXT daily run, so the row is live until then.
        //   2. Rows written before this change — registration used to leave one on every failed
        //      confirmation send (#1349's measured reproduction on dev, 2026-08-16).
        //
        // Producer 2 is RETIRED by this same PR, and per §5 the pin that the current writer no longer
        // emits the shape lives elsewhere: RegisterCommandHandlerTests
        // .Handle_FlagOn_WhenConfirmationSendThrows_SwallowsAndKeepsTheAccount, and end to end in
        // OrphanedIdentityActivationTests. Producer 1 is not retired and is why this guard is permanent
        // rather than a migration.
        //
        // The account is deliberately NOT soft-deleted here — that is the sibling D5 case two tests up,
        // and reading this one as a variant of it misses the point: there is no profile row at all.
        var userId = Guid.NewGuid();

        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));

        // No JobSeeker for this userId — the orphan.
        var db = TestAppDbContextFactory.Create();

        var sessionStore = Substitute.For<ISessionStore>();
        var auditLogger = Substitute.For<IAuthAuditLogger>();
        var handler = CreateHandler(
            db: db, userAccountService: userAccountService, sessionStore: sessionStore, auditLogger: auditLogger);

        var result = await handler.Handle(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue(
            "credentials are valid, but the account owns no profile — granting a session here is what "
            + "tells the user their account is active while it owns nothing (#1349)");
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials,
            "the SAME code as an unknown address / wrong password and as the soft-delete arm — a distinct "
            + "code would open the account-status oracle that arm was written to close");

        await sessionStore.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>());
        auditLogger.DidNotReceive().LoginSucceeded(Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WithNoJobSeekerRow_AnswersIdenticallyToASoftDeletedProfile()
    {
        // The uniform answer stated DIRECTLY across the two refusal grounds rather than inferred from
        // two tests passing separately: an orphaned account and a soft-deleted one must be
        // indistinguishable to the caller. If a later change gives either a distinct code, this fails —
        // which is the whole reason the #1349 guard reuses InvalidCredentials instead of adding one.
        var ct = TestContext.Current.CancellationToken;
        var clock = FakeDateTimeProvider.Default;

        var orphanUserId = Guid.NewGuid();
        var deletedUserId = Guid.NewGuid();

        static IUserAccountService AccountsFor(Guid userId)
        {
            var svc = Substitute.For<IUserAccountService>();
            svc.ValidateCredentialsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(new UserCredentials(userId, new List<string>())));
            return svc;
        }

        var orphanDb = TestAppDbContextFactory.Create();

        var deletedSeeker = JobSeeker.Register(deletedUserId, "Soft Deleted User", clock).Value;
        deletedSeeker.SoftDelete(clock);
        var deletedDb = TestAppDbContextFactory.Create();
        deletedDb.JobSeekers.Add(deletedSeeker);
        await deletedDb.SaveChangesAsync(ct);

        var orphan = await CreateHandler(db: orphanDb, userAccountService: AccountsFor(orphanUserId))
            .Handle(ValidCommand(), ct);
        var deleted = await CreateHandler(db: deletedDb, userAccountService: AccountsFor(deletedUserId))
            .Handle(ValidCommand(), ct);

        orphan.IsFailure.ShouldBeTrue();
        deleted.IsFailure.ShouldBeTrue();
        orphan.Error.Code.ShouldBe(deleted.Error.Code);
        orphan.Error.Message.ShouldBe(deleted.Error.Message);
    }
}
