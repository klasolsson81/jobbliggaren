using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.Register;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        AuthEmailCooldownOptions? emailCooldownOptions = null,
        bool requireEmailConfirmation = false,
        // ADR 0083 Amendment 2026-08-03. Production default is CLOSED; this helper defaults it OPEN so
        // every pre-existing test in this file still exercises the path it was written for. Only the
        // kill-switch tests pass false.
        bool registrationsOpen = true)
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
        var options = Options.Create(new AuthOptions
        {
            RequireEmailConfirmation = requireEmailConfirmation,
            RegistrationsOpen = registrationsOpen,
        });
        var cooldownOptions = Options.Create(emailCooldownOptions ?? new AuthEmailCooldownOptions());

        return new RegisterCommandHandler(
            db, userAccountService, sessionStore, auditLogger, emailSender, cooldown, options, cooldownOptions,
            FakeDateTimeProvider.Default, NullLogger<RegisterCommandHandler>.Instance);
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
            Arg.Any<string>(), Arg.Any<CancellationToken>());
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
            Arg.Is<EmailConfirmationEmail>(c => c != null && c.UserId == userId && c.UrlSafeToken == "url-safe-token"),
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
            "klas@example.com", Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendEmailConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
            Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FlagOn_FreshSignup_DoesNotBeginCooldown()
    {
        // #703: the cooldown lives ONLY in the duplicate-swallow branch (under
        // `requireConfirmation && DuplicateAccount`). A FRESH signup must never begin a window — the address
        // is the registrant's own, and starting an account-exists window on it would silently suppress a
        // legitimate notice on a later re-register. (The legacy flag-OFF paths cannot reach the branch at all.)
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);
        userAccountService.GenerateEmailConfirmationTokenAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success("tok"));
        var cooldown = Substitute.For<ICooldownGate>();
        cooldown.TryBeginAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = CreateHandler(
            userAccountService: userAccountService, cooldown: cooldown, requireEmailConfirmation: true);

        (await handler.Handle(ValidCommand(), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await cooldown.DidNotReceive().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FlagOn_WhenDuplicate_BeginsAccountExistsCooldownWithConfiguredWindow()
    {
        // Pins SCOPE + SUBJECT + WINDOW-source: the duplicate branch begins the AccountExists scope keyed on
        // the recipient address, using AccountExistsNoticeWindowSeconds. A cross-wire to the ResendConfirm
        // scope (which CooldownScopes forbids) or to ChangeEmailWindowSeconds would be invisible otherwise —
        // a fresh gate answers uniformly and both windows default to 60.
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage)));
        var emailSender = Substitute.For<IEmailSender>();
        var cooldown = Substitute.For<ICooldownGate>();
        cooldown.TryBeginAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = CreateHandler(
            userAccountService: userAccountService, emailSender: emailSender, cooldown: cooldown,
            emailCooldownOptions: new AuthEmailCooldownOptions { AccountExistsNoticeWindowSeconds = 77 },
            requireEmailConfirmation: true);

        (await handler.Handle(ValidCommand(), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        await cooldown.Received(1).TryBeginAsync(
            CooldownScopes.AccountExists, "klas@example.com",
            TimeSpan.FromSeconds(77), Arg.Any<CancellationToken>());
        await emailSender.Received(1).SendAccountExistsNoticeAsync(
            "klas@example.com", Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await emailSender.DidNotReceive().SendEmailConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
            Arg.Any<CancellationToken>());
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
            Arg.Any<CancellationToken>());
    }

    // ---------- Send-failure symmetry (CTO-bind Risk 1) ----------
    // Both the fresh branch (SendEmailConfirmationAsync) and the duplicate-swallow branch
    // (SendAccountExistsNoticeAsync) send as their FINAL action and propagate the exception uncaught, so
    // a transport fault surfaces identically (an unhandled exception → the same 500 at the endpoint).
    // Pinned here (unit) rather than via an extra WebApplicationFactory host (which would trip EF's
    // process-wide ManyServiceProvidersCreatedWarning across the shared integration [Collection]).

    [Fact]
    public async Task Handle_FlagOn_WhenConfirmationSendThrows_SwallowsAndKeepsTheAccount()
    {
        // #1349 — INVERTED from …PropagatesUncaught (senior-cto-advisor 2026-08-17). Propagating was the
        // defect's producer, not a safety property: UnitOfWorkBehavior calls SaveChangesAsync
        // unconditionally after the handler returns, so a throw meant the tracked JobSeeker was never
        // committed while the Identity user — created in its own boundary — survived. That orphan is
        // then activatable and is swept at 04:00 UTC with no notice.
        //
        // Swallowing keeps the account whole. The user loses the mail, not the account, and the recovery
        // for exactly that is the #733 resend already mounted on the screen they are looking at.
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);
        userAccountService.GenerateEmailConfirmationTokenAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success("tok"));
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendEmailConfirmationAsync(
                Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("send failed")));

        var db = Substitute.For<IAppDbContext>();
        var jobSeekers = Substitute.For<DbSet<JobSeeker>>();
        db.JobSeekers.Returns(jobSeekers);

        var handler = CreateHandler(
            db: db, userAccountService: userAccountService, emailSender: emailSender,
            requireEmailConfirmation: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue("a transport fault must not fell a registration that succeeded");
        result.Value.Session.ShouldBeNull("email-confirmation-first still mints no session");
        // The JobSeeker is still tracked, so UnitOfWorkBehavior commits it — no orphan is produced.
        jobSeekers.Received(1).Add(Arg.Any<JobSeeker>());
        // The account is NOT compensated away: deleting a correctly created account because a third
        // party's transport blinked is the alternative the CTO rejected (its own failure mode is another
        // orphan when the delete fails in the same outage).
        await userAccountService.DidNotReceive().DeleteUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FlagOn_WhenNoticeSendThrows_SwallowsToTheSameUniformOutcome()
    {
        // #1349 — INVERTED alongside its sibling, and the symmetry is the load-bearing part. The two arms
        // must agree on the fault policy or the fix itself becomes an enumeration oracle: swallow only
        // the confirmation send and, during a transport outage, a FRESH address answers 202 while a TAKEN
        // one answers 500 — the exact 200-vs-400 channel #714 was built to close, re-opened by a change
        // whose stated purpose was to fix a defect. The parity assertion lives in
        // Handle_FlagOn_SendFaultIsIndistinguishableBetweenFreshAndTakenAddresses below.
        var userAccountService = Substitute.For<IUserAccountService>();
        userAccountService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage)));
        var emailSender = Substitute.For<IEmailSender>();
        emailSender.SendAccountExistsNoticeAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("send failed")));

        var handler = CreateHandler(
            userAccountService: userAccountService, emailSender: emailSender, requireEmailConfirmation: true);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Session.ShouldBeNull("the duplicate branch has always answered the uniform 202");
    }

    [Fact]
    public async Task Handle_FlagOn_SendFaultIsIndistinguishableBetweenFreshAndTakenAddresses()
    {
        // #1349 — the anti-enumeration invariant stated DIRECTLY over the two arms, rather than inferred
        // from the two tests above passing separately. Under one transport outage, a fresh and a taken
        // address must produce the same outcome. This is the assertion that fails if a later change
        // repairs one arm and forgets the other.
        var userId = Guid.NewGuid();

        var freshAccounts = UserAccountServiceCreating(userId);
        freshAccounts.GenerateEmailConfirmationTokenAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success("tok"));

        var takenAccounts = Substitute.For<IUserAccountService>();
        takenAccounts.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage)));

        // ONE sender whose every send fails — the shape a provider outage actually has (it does not
        // discriminate by email kind).
        var outage = Substitute.For<IEmailSender>();
        outage.SendEmailConfirmationAsync(
                Arg.Any<string>(), Arg.Any<EmailConfirmationEmail>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("outage")));
        outage.SendAccountExistsNoticeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("outage")));

        var fresh = await CreateHandler(
                userAccountService: freshAccounts, emailSender: outage, requireEmailConfirmation: true)
            .Handle(ValidCommand(), CancellationToken.None);
        var taken = await CreateHandler(
                userAccountService: takenAccounts, emailSender: outage, requireEmailConfirmation: true)
            .Handle(ValidCommand(), CancellationToken.None);

        fresh.IsSuccess.ShouldBe(taken.IsSuccess, "a send fault must not split the two branches");
        fresh.IsSuccess.ShouldBeTrue();
        fresh.Value.Session.ShouldBe(taken.Value.Session, "both answer the same session-less 202");
    }

    // ---------- Public-registration kill-switch (ADR 0083 Amendment 2026-08-03) ----------

    [Fact]
    public async Task Handle_RegistrationsClosed_FailsWithRegistrationsClosedCode()
    {
        var handler = CreateHandler(registrationsOpen: false);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.RegistrationsClosed);
        result.Error.Message.ShouldBe(AuthErrorCodes.RegistrationsClosedMessage);
    }

    /// <summary>
    /// The assertion that actually pins "nothing is written". A refused registration must not leave an
    /// Identity user behind for the #508 orphan-sweep to collect, so the gate has to sit BEFORE
    /// CreateUserAsync — not after it with a compensating delete.
    /// </summary>
    [Fact]
    public async Task Handle_RegistrationsClosed_CreatesNoAccountAndNoSession()
    {
        var userId = Guid.NewGuid();
        var userAccountService = UserAccountServiceCreating(userId);
        var sessionStore = DefaultSessionStore(userId);
        var emailSender = Substitute.For<IEmailSender>();
        var auditLogger = Substitute.For<IAuthAuditLogger>();

        var handler = CreateHandler(
            userAccountService: userAccountService,
            sessionStore: sessionStore,
            auditLogger: auditLogger,
            emailSender: emailSender,
            registrationsOpen: false);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        await userAccountService.DidNotReceive().CreateUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sessionStore.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>());
        auditLogger.DidNotReceive().LoginSucceeded(Arg.Any<Guid>(), Arg.Any<string>());
        emailSender.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Anti-enumeration by CONSTRUCTION, not by care: the gate never reads the submitted address, so a
    /// fresh and a taken address cannot diverge. Stronger than #714's uniform 202, which holds only as
    /// long as two branches stay byte-identical. Also true with the confirmation flag ON — the gate
    /// precedes that branch entirely.
    /// <para>
    /// The fixture models a genuinely TAKEN address (the adapter's real DuplicateAccount failure, the
    /// same stub Handle_FlagOff_WhenDuplicate_ReturnsFailure_NoNotice uses) rather than two unknown
    /// ones — otherwise "taken" is a word in a comment that the assertions cannot observe. Both are
    /// asserted against the constant ABSOLUTELY, not against each other: comparing one refusal to
    /// another passes even if the handler returns the wrong error entirely.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_RegistrationsClosed_RefusesIdentically_ForTakenAndFreshAddress(
        bool requireEmailConfirmation)
    {
        const string takenEmail = "redan.tagen@example.com";
        var userAccountService = UserAccountServiceCreating(Guid.NewGuid());
        userAccountService.CreateUserAsync(takenEmail, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage)));

        var handler = CreateHandler(
            userAccountService: userAccountService,
            requireEmailConfirmation: requireEmailConfirmation,
            registrationsOpen: false);

        var fresh = await handler.Handle(ValidCommand(), CancellationToken.None);
        var taken = await handler.Handle(
            new RegisterCommand(
                Email: takenEmail,
                Password: "S3kret!pass",
                DisplayName: "Någon Annan"),
            CancellationToken.None);

        fresh.IsFailure.ShouldBeTrue();
        taken.IsFailure.ShouldBeTrue();
        fresh.Error.Code.ShouldBe(AuthErrorCodes.RegistrationsClosed);
        taken.Error.Code.ShouldBe(AuthErrorCodes.RegistrationsClosed);
        fresh.Error.Message.ShouldBe(AuthErrorCodes.RegistrationsClosedMessage);
        taken.Error.Message.ShouldBe(AuthErrorCodes.RegistrationsClosedMessage);
    }

    /// <summary>
    /// The fail-safe default itself, pinned past the helper. Every other test in this file goes
    /// through <c>CreateHandler</c>, which defaults <c>registrationsOpen: true</c> — so none of them
    /// ever sees a default-constructed <see cref="AuthOptions"/>, and the property the whole change
    /// rests on would survive its own removal.
    /// <para>
    /// This is not hypothetical. The defect this feature exists to fix is exactly a missing
    /// initialiser: <c>RequireEmailConfirmation</c> is declared without one and absent from every
    /// non-Development appsettings file, so the Production configuration resolves it to the
    /// permissive branch. An absent key, a typo or an undeployed config must not open the gate.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Handle_WithDefaultAuthOptions_RefusesRegistration()
    {
        var userAccountService = UserAccountServiceCreating(Guid.NewGuid());
        var db = Substitute.For<IAppDbContext>();
        db.JobSeekers.Returns(Substitute.For<DbSet<JobSeeker>>());

        var handler = new RegisterCommandHandler(
            db,
            userAccountService,
            Substitute.For<ISessionStore>(),
            Substitute.For<IAuthAuditLogger>(),
            Substitute.For<IEmailSender>(),
            Substitute.For<ICooldownGate>(),
            Options.Create(new AuthOptions()),
            Options.Create(new AuthEmailCooldownOptions()),
            FakeDateTimeProvider.Default,
            NullLogger<RegisterCommandHandler>.Instance);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.RegistrationsClosed);
        await userAccountService.DidNotReceive().CreateUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
