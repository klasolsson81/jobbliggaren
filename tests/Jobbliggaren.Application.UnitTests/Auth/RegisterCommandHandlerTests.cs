using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.Register;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
        IAuthAuditLogger? auditLogger = null,
        IEmailSender? emailSender = null,
        ICooldownGate? cooldown = null,
        bool requireEmailConfirmation = false)
    {
        if (db is null)
        {
            db = Substitute.For<IAppDbContext>();
            db.JobSeekers.Returns(Substitute.For<DbSet<JobSeeker>>());
        }

        userAccountService ??= Substitute.For<IUserAccountService>();
        sessionStore ??= Substitute.For<ISessionStore>();
        auditLogger ??= Substitute.For<IAuthAuditLogger>();
        emailSender ??= Substitute.For<IEmailSender>();
        if (cooldown is null)
        {
            // Default: NOT cooling — the account-exists notice behavioural tests assert the send, so the
            // #703 cooldown must pass unless a test explicitly injects a cooling gate.
            cooldown = Substitute.For<ICooldownGate>();
            cooldown.TryBeginAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                .Returns(true);
        }
        var options = Options.Create(new AuthOptions { RequireEmailConfirmation = requireEmailConfirmation });
        var cooldownOptions = Options.Create(new AuthEmailCooldownOptions());

        return new RegisterCommandHandler(
            db, userAccountService, sessionStore, auditLogger, emailSender, cooldown, options, cooldownOptions,
            FakeDateTimeProvider.Default);
    }

    private static IUserAccountService UserAccountServiceCreating(Guid userId)
    {
        var svc = Substitute.For<IUserAccountService>();
        svc.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(userId));
        return svc;
    }

    private static ISessionStore DefaultSessionStore(Guid userId)
    {
        var store = Substitute.For<ISessionStore>();
        store.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(new Session(SessionId.Generate(), userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));
        return store;
    }

    // ---------- Legacy instant-login path (flag OFF) ----------

    [Fact]
    public async Task Handle_FlagOff_WithValidCommand_ReturnsSessionId()
    {
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);

        var sessionId = SessionId.Generate();
        var sessionStore = Substitute.For<ISessionStore>();
        sessionStore.CreateAsync(userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(new Session(sessionId, userId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(14)));

        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Session.ShouldNotBeNull();
        result.Value.Session!.SessionId.ShouldBe(sessionId.Reveal());
    }

    // #2b2 / #2b3b activation: rememberMe at registration mirrors login — checked →
    // Persistent, unchecked/absent → the short session-scoped Session (not Legacy).
    [Fact]
    public async Task Handle_FlagOff_WithRememberMe_CreatesPersistentSession()
    {
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);
        var sessionStore = DefaultSessionStore(userId);
        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand() with { RememberMe = true }, CancellationToken.None);

        await sessionStore.Received(1).CreateAsync(userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FlagOff_WithoutRememberMe_CreatesSessionScopedSession()
    {
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);
        var sessionStore = DefaultSessionStore(userId);
        var handler = CreateHandler(userAccountService: userAccountService, sessionStore: sessionStore);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        // Activation flip: unticked → the short session-scoped Session, not Legacy.
        await sessionStore.Received(1).CreateAsync(userId, SessionLifetime.Session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FlagOff_WhenDuplicate_ReturnsFailure_NoNotice()
    {
        // Legacy path keeps the distinct 400 duplicate (the status oracle is acknowledged-deferred and
        // the confirmation-first feature is not enabled). The swallow-to-202 only happens flag ON.
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage)));
        var emailSender = Substitute.For<IEmailSender>();

        var handler = CreateHandler(userAccountService: userAccountService, emailSender: emailSender);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.DuplicateAccount);
        await emailSender.DidNotReceive().SendAccountExistsNoticeAsync(
            Arg.Any<string>(), Arg.Any<AccountExistsNoticeIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    // ---------- Email-confirmation-first path (flag ON) ----------

    [Fact]
    public async Task Handle_FlagOn_WithValidCommand_SendsConfirmationAndMintsNoSession()
    {
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);
        userAccountService.GenerateEmailConfirmationTokenAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success("url-safe-token"));
        var sessionStore = Substitute.For<ISessionStore>();
        var auditLogger = Substitute.For<IAuthAuditLogger>();
        var emailSender = Substitute.For<IEmailSender>();

        var handler = CreateHandler(
            userAccountService: userAccountService, sessionStore: sessionStore,
            auditLogger: auditLogger, emailSender: emailSender, requireEmailConfirmation: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Session.ShouldBeNull("no session is minted on the confirmation-first path");

        await emailSender.Received(1).SendEmailConfirmationAsync(
            "klas@example.com",
            Arg.Is<EmailConfirmationEmail>(c => c.UserId == userId && c.UrlSafeToken == "url-safe-token"),
            Arg.Any<EmailConfirmationIdempotencyKey>(),
            Arg.Any<CancellationToken>());
        await sessionStore.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>());
        auditLogger.DidNotReceive().LoginSucceeded(Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_FlagOn_StillAddsJobSeekerToDb()
    {
        var userId = Guid.NewGuid();
        var db = Substitute.For<IAppDbContext>();
        var seekerSet = Substitute.For<DbSet<JobSeeker>>();
        db.JobSeekers.Returns(seekerSet);
        var userAccountService = UserAccountServiceCreating(userId);
        userAccountService.GenerateEmailConfirmationTokenAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success("tok"));

        var handler = CreateHandler(db: db, userAccountService: userAccountService, requireEmailConfirmation: true);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        seekerSet.Received(1).Add(Arg.Any<JobSeeker>());
    }

    [Fact]
    public async Task Handle_FlagOn_WhenDuplicate_SwallowsToNoSessionAndSendsNotice()
    {
        // The anti-enumeration core: a taken address must NOT 400 — it returns the SAME 202 outcome as a
        // fresh signup (Session = null) and emails an out-of-band account-exists notice. No JobSeeker is
        // added, no session minted, no confirmation link sent.
        var db = Substitute.For<IAppDbContext>();
        var seekerSet = Substitute.For<DbSet<JobSeeker>>();
        db.JobSeekers.Returns(seekerSet);
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage)));
        var sessionStore = Substitute.For<ISessionStore>();
        var emailSender = Substitute.For<IEmailSender>();

        var handler = CreateHandler(
            db: db, userAccountService: userAccountService, sessionStore: sessionStore,
            emailSender: emailSender, requireEmailConfirmation: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue("a duplicate is swallowed to the same 202 outcome as a fresh signup");
        result.Value.Session.ShouldBeNull();

        await emailSender.Received(1).SendAccountExistsNoticeAsync(
            "klas@example.com", Arg.Any<AccountExistsNoticeIdempotencyKey>(), Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendEmailConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
            Arg.Any<EmailConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
        seekerSet.DidNotReceive().Add(Arg.Any<JobSeeker>());
        await sessionStore.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FlagOn_WhenDuplicateAndCooling_SwallowsButSendsNoNotice()
    {
        // #703: a within-cooldown duplicate on the SAME address must still swallow to the SAME uniform 202
        // (Session = null) — but the per-target throttle suppresses a second account-exists notice, so a
        // taken address cannot be email-bombed by repeated registration. Silent (no 429): a visible throttle
        // on this UNAUTHENTICATED surface would itself be an enumeration channel.
        var db = Substitute.For<IAppDbContext>();
        db.JobSeekers.Returns(Substitute.For<DbSet<JobSeeker>>());
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage)));
        var emailSender = Substitute.For<IEmailSender>();
        // A fresh (unconfigured) gate returns false for every scope → the account-exists window is active.
        var cooling = Substitute.For<ICooldownGate>();

        var handler = CreateHandler(
            db: db, userAccountService: userAccountService, emailSender: emailSender,
            cooldown: cooling, requireEmailConfirmation: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue("a cooled duplicate is still swallowed to the same 202 outcome");
        result.Value.Session.ShouldBeNull();
        await emailSender.DidNotReceive().SendAccountExistsNoticeAsync(
            Arg.Any<string>(), Arg.Any<AccountExistsNoticeIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FlagOn_WhenBreachedPassword_StaysFailure_NotSwallowed()
    {
        // A non-duplicate CreateUserAsync failure (breached password #616) is credential-dependent, not
        // existence-dependent, so it must NOT be swallowed to a 202 — it stays a genuine failure and no
        // email is sent. This preserves the anti-enumeration invariant: for a FIXED password, a taken
        // and a fresh address are identical (both 202 for a strong password, both this failure for a
        // breached one — Identity validates the password before uniqueness).
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation("Auth.PwnedPassword", "Lösenordet har förekommit i kända dataläckor.")));
        var emailSender = Substitute.For<IEmailSender>();

        var handler = CreateHandler(
            userAccountService: userAccountService, emailSender: emailSender, requireEmailConfirmation: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.PwnedPassword");
        await emailSender.DidNotReceive().SendAccountExistsNoticeAsync(
            Arg.Any<string>(), Arg.Any<AccountExistsNoticeIdempotencyKey>(), Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendEmailConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
            Arg.Any<EmailConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    // ---------- Shared: JobSeeker creation failure (both paths) ----------

    [Fact]
    public async Task Handle_FlagOn_WhenJobSeekerCreationFails_DeletesUserAndSendsNoEmail()
    {
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);
        var emailSender = Substitute.For<IEmailSender>();

        var handler = CreateHandler(
            userAccountService: userAccountService, emailSender: emailSender, requireEmailConfirmation: true);

        // Blank display name → JobSeeker.Register fails AFTER the user is created but BEFORE any email.
        var result = await handler.Handle(
            new RegisterCommand("klas@example.com", "S3kret!pass", "   "), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await userAccountService.Received(1).DeleteUserAsync(userId, Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendEmailConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
            Arg.Any<EmailConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    // ---------- Send-failure symmetry (CTO-bind Risk 1) ----------
    // Both the fresh branch (SendEmailConfirmationAsync) and the duplicate-swallow branch
    // (SendAccountExistsNoticeAsync) send as their FINAL action and propagate the exception uncaught, so
    // a transport fault surfaces identically (an unhandled exception → the same 500 at the endpoint).
    // Pinned here (unit) rather than via an extra WebApplicationFactory host (which would trip EF's
    // process-wide ManyServiceProvidersCreatedWarning across the shared integration [Collection]).

    [Fact]
    public async Task Handle_FlagOn_WhenConfirmationSendThrows_PropagatesUncaught()
    {
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);
        userAccountService.GenerateEmailConfirmationTokenAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success("tok"));
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendEmailConfirmationAsync(
                Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
                Arg.Any<EmailConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("send failed")));

        var handler = CreateHandler(
            userAccountService: userAccountService, emailSender: emailSender, requireEmailConfirmation: true);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.Handle(ValidCommand(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_FlagOn_WhenNoticeSendThrows_PropagatesUncaught()
    {
        // Duplicate-swallow branch: the same fault class must propagate the same way (symmetry).
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage)));
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAccountExistsNoticeAsync(
                Arg.Any<string>(), Arg.Any<AccountExistsNoticeIdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("send failed")));

        var handler = CreateHandler(
            userAccountService: userAccountService, emailSender: emailSender, requireEmailConfirmation: true);

        await Should.ThrowAsync<InvalidOperationException>(
            () => handler.Handle(ValidCommand(), CancellationToken.None).AsTask());
    }
}
