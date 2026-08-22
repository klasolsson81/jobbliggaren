using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The credential-check body migrated out of <c>VerifyCredentialsQueryHandler</c> into the shared
/// <see cref="ReauthenticationService"/> (PR2c/C5, epik #481) — one enforcement path for both
/// <c>ReauthenticationBehavior</c> (throws on failure) and <c>POST /auth/verify</c> (returns the
/// Result). These tests pin the exact decision ORDER, which is security-load-bearing:
/// <list type="number">
/// <item>no <see cref="ICurrentUser.UserId"/> → InvalidCredentials (failsafe, no credential I/O)</item>
/// <item>empty email → InvalidCredentials (no ValidateCredentials call)</item>
/// <item><see cref="IUserAccountService.ValidateCredentialsAsync"/> fails → the credential error flows
///   through (wrong-password / locked), checked BEFORE the soft-delete gate so a wrong password never
///   reveals or acts on soft-delete state (M8 timing/response oracle-avoidance)</item>
/// <item>TOCTOU: resolved UserId ≠ session UserId → InvalidCredentials</item>
/// <item>Layer-1 profile gate, TWO grounds one outcome: <c>DeletedAt != null</c> (soft-deleted) OR no
///   <c>JobSeeker</c> row at all (#1349) → best-effort session self-heal
///   (<see cref="ISessionStore.InvalidateAllForUserAsync"/>) + InvalidCredentials; a Redis failure in
///   the self-heal must NOT change the reject outcome</item>
/// <item>else → Success</item>
/// </list>
/// <para>
/// Point 5's no-row ground is #1349 and it INVERTED an earlier rule. This class used to specify
/// "a missing seeker row is Success — no-row parity with LoginCommandHandler", and the parity was
/// real: both gates passed an orphan. The behaviour it mirrored was the defect, so both gates now
/// refuse and the parity holds again with the opposite outcome. The projection had to change for the
/// gate to see the case at all — <c>Select(js =&gt; (DateTimeOffset?)js.DeletedAt)</c> made
/// <c>FirstOrDefaultAsync</c> answer null for "no row" and "a live row" alike.
/// </para>
/// The gate keys <c>userId → JobSeeker.UserId</c> via <c>IgnoreQueryFilters()</c> (the global
/// DeletedAt==null filter would otherwise hide the soft-deleted row).
/// </summary>
public class ReauthenticationServiceTests
{
    private const string TestEmail = "klas@example.com";
    private const string TestPassword = "S3kret!pass";
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserAccountService _userAccount = Substitute.For<IUserAccountService>();
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();
    // Real InMemory AppDbContext (implements IAppDbContext) — same fake-DbContext pattern as
    // DeleteAccountCommandHandlerTests. Unique DB name per test-class instance (fresh per [Fact]).
    // Concrete type (not IAppDbContext) per CA1859; passed to the SUT via its IAppDbContext ctor param.
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();

    private ReauthenticationService CreateSut() =>
        new(_currentUser, _userAccount, _db, _sessionStore);

    // Authenticated user whose password validates and resolves back to the same userId (TOCTOU passes).
    private void AuthenticatedWithValidCredentials(Guid userId)
    {
        _currentUser.UserId.Returns(userId);
        _userAccount.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns(TestEmail);
        _userAccount.ValidateCredentialsAsync(TestEmail, TestPassword, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(userId, new List<string>())));
    }

    private async Task SeedSeekerAsync(Guid userId, bool softDeleted, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Test User", Clock).Value;
        if (softDeleted)
            seeker.SoftDelete(Clock);
        _db.JobSeekers.Add(seeker);
        await _db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenNoUserId_ReturnsInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        _currentUser.UserId.Returns((Guid?)null);

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        // Failsafe returns before ANY credential I/O.
        await _userAccount.DidNotReceive().GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _userAccount.DidNotReceive().ValidateCredentialsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenEmailLookupReturnsEmpty_ReturnsInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _userAccount.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        // No email → we never touch the credential validator.
        await _userAccount.DidNotReceive().ValidateCredentialsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenPasswordInvalid_FailsWithoutTouchingSoftDeleteGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _userAccount.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns(TestEmail);
        _userAccount.ValidateCredentialsAsync(TestEmail, "wrong", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserCredentials>(
                DomainError.Validation(AuthErrorCodes.InvalidCredentials, "E-post eller lösenord är felaktigt.")));

        // Seed a SOFT-DELETED seeker for this user. If the soft-delete gate ran, it would call
        // InvalidateAllForUserAsync on this row. It must NOT — the password check comes FIRST, so a
        // wrong password can never reveal (or act on) soft-delete state. The un-fired self-heal is the
        // observable proof that the JobSeekers gate was never reached (password-first ordering).
        await SeedSeekerAsync(userId, softDeleted: true, ct);

        var result = await CreateSut().VerifyCurrentUserPasswordAsync("wrong", ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.DidNotReceive().InvalidateAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenValidateCredentialsReturnsEmailNotConfirmed_NormalizesToInvalidCredentials()
    {
        // #714 CTO-bind Risk 2 (defense-in-depth): the shared ValidateCredentialsAsync can now emit
        // EmailNotConfirmed (the login gate). On the re-auth surface that MUST be normalized back to the
        // uniform InvalidCredentials 401 — the distinct 403 arm is reachable ONLY via LoginCommandHandler.
        // Unreachable in practice (only confirmed users hold sessions), but this pins the branch so a
        // future refactor can never leak the confirmation-gate 403 onto /auth/verify.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _userAccount.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns(TestEmail);
        _userAccount.ValidateCredentialsAsync(TestEmail, TestPassword, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserCredentials>(DomainError.Validation(
                AuthErrorCodes.EmailNotConfirmed, AuthErrorCodes.EmailNotConfirmedMessage)));

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        result.IsFailure.ShouldBeTrue();
        // Normalized: the caller sees InvalidCredentials, never EmailNotConfirmed.
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        result.Error.Code.ShouldNotBe(AuthErrorCodes.EmailNotConfirmed);
        // Failed credential check → never reaches the soft-delete gate.
        await _sessionStore.DidNotReceive().InvalidateAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenValidateCredentialsReturnsOtherError_PassesItThrough()
    {
        // The normalization is SPECIFIC to EmailNotConfirmed — any OTHER credential error (here
        // AccountLocked) flows through unchanged (the wire mapper normalizes AccountLocked→401 centrally).
        // This guards against a regression that blanket-normalizes every failure to InvalidCredentials.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _userAccount.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns(TestEmail);
        _userAccount.ValidateCredentialsAsync(TestEmail, TestPassword, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<UserCredentials>(DomainError.Validation(
                AuthErrorCodes.AccountLocked, "Kontot är låst.")));

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.AccountLocked);
        await _sessionStore.DidNotReceive().InvalidateAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenResolvedUserIdMismatchesSession_ReturnsInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionUserId = Guid.NewGuid();
        var resolvedUserId = Guid.NewGuid();
        _currentUser.UserId.Returns(sessionUserId);
        _userAccount.GetEmailAsync(sessionUserId, Arg.Any<CancellationToken>()).Returns(TestEmail);
        // Password validates but resolves to a DIFFERENT userId than the session (e.g. email re-pointed).
        _userAccount.ValidateCredentialsAsync(TestEmail, TestPassword, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new UserCredentials(resolvedUserId, new List<string>())));

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        // TOCTOU rejects BEFORE the soft-delete gate — no self-heal.
        await _sessionStore.DidNotReceive().InvalidateAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenSeekerSoftDeleted_FailsAndSelfHealsSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithValidCredentials(userId);
        await SeedSeekerAsync(userId, softDeleted: true, ct);

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        // Correct password, but the account is soft-deleted-not-hard-deleted → reject + tear down its
        // surviving sessions (Layer-1 gate).
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.Received(1).InvalidateAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenSoftDeletedAndSelfHealThrows_StillReturnsInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithValidCredentials(userId);
        await SeedSeekerAsync(userId, softDeleted: true, ct);
        _sessionStore.InvalidateAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Redis nere"));

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        // Self-heal is best-effort (try/catch): a Redis failure must not turn the gate into a 500 or
        // change the security-relevant reject. Layer 2's tombstone still fail-closes the read path.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.Received(1).InvalidateAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenSeekerLiveAndPasswordValid_ReturnsSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithValidCredentials(userId);
        await SeedSeekerAsync(userId, softDeleted: false, ct);

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        result.IsSuccess.ShouldBeTrue();
        // No session mutation on the success path.
        await _sessionStore.DidNotReceive().InvalidateAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_WhenNoSeekerRow_ReturnsInvalidCredentials()
    {
        // #1349 — INVERTED, and the old version is worth reading before this one. It asserted success
        // and justified it as "Parity with LoginCommandHandler: a user without a seeker row is not
        // blocked by the gate". The parity was real; the behaviour it mirrored was the defect. Both
        // gates now refuse, so the sentence is true again with the opposite outcome.
        //
        // The gate could not even SEE this case before: Select(js => (DateTimeOffset?)js.DeletedAt)
        // made FirstOrDefaultAsync answer null for "no row" and for "a live row" alike. An orphan
        // holding a live session therefore passed re-auth, changed its password, and was handed a
        // FRESH session by the /change-password re-issue — renewing the capability indefinitely
        // without ever crossing the login guard (security-auditor M-1).
        //
        // WHO PRODUCES THE PREMISE (CLAUDE.md §5 Tests:): AccountHardDeleter step 2h
        // commits the domain transaction before the Identity DELETE,
        // so a failure there leaves exactly this row — and the transient window every registration
        // passes through (the #508 grace filter, "presumed mid-registration") produces it too.
        // The actor's own predicate is pinned against the real AccountHardDeleter in
        // HardDeleteAccountsJobIntegrationTests.CleanupIdentityOrphans_DoesNotSweepIdentityUserWithinGraceWindow.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithValidCredentials(userId);
        // No seeker seeded — that IS the orphan.

        var result = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        result.IsFailure.ShouldBeTrue(
            "re-auth must not grant a sensitive operation to an account that owns no profile");
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials,
            "uniform with the soft-delete arm and with login — a distinct code would be an "
            + "account-status oracle on an authenticated but hostile-reachable surface");
    }

    [Fact]
    public async Task VerifyCurrentUserPassword_NoSeekerRowAndSoftDeletedAnswerIdentically()
    {
        // The two refusal grounds must be indistinguishable to the caller, stated directly rather than
        // inferred from two tests passing. Same discipline as the login-side sibling.
        var ct = TestContext.Current.CancellationToken;

        var orphanUserId = Guid.NewGuid();
        AuthenticatedWithValidCredentials(orphanUserId);
        var orphan = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        var deletedUserId = Guid.NewGuid();
        AuthenticatedWithValidCredentials(deletedUserId);
        await SeedSeekerAsync(deletedUserId, softDeleted: true, ct);
        var deleted = await CreateSut().VerifyCurrentUserPasswordAsync(TestPassword, ct);

        orphan.IsFailure.ShouldBeTrue();
        deleted.IsFailure.ShouldBeTrue();
        orphan.Error.Code.ShouldBe(deleted.Error.Code);
        orphan.Error.Message.ShouldBe(deleted.Error.Message);
    }
}
