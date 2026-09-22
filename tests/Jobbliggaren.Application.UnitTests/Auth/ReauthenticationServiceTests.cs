using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The re-auth check in ONE place, <see cref="ReauthenticationService"/> (PR2c/C5, epik #481; a grant since
/// #1739, ADR 0142 D5), consumed by <c>ReauthenticationBehavior</c>. These tests pin the exact decision ORDER,
/// which is security-load-bearing:
/// <list type="number">
/// <item>no <see cref="ICurrentUser.UserId"/> → InvalidCredentials (failsafe, no store I/O, no log line)</item>
/// <item>empty grant → InvalidCredentials, and the store is never asked</item>
/// <item>the grant is redeemed with <c>GrantAssertion.Of(new Reauthentication(sessionUserId))</c> — the store
///   asserts purpose AND user, so the handler passes the binding and never compares — and a null subject is
///   InvalidCredentials, checked BEFORE the soft-delete gate so an unusable grant never acts on soft-delete
///   state</item>
/// <item>Layer-1 profile gate, TWO grounds one outcome: <c>DeletedAt != null</c> (soft-deleted) OR no
///   <c>JobSeeker</c> row at all (#1349) → best-effort session self-heal
///   (<see cref="ISessionStore.InvalidateAllForUserAsync"/>) + InvalidCredentials; a Redis failure in
///   the self-heal must NOT change the reject outcome</item>
/// <item>else → Success</item>
/// </list>
/// Every refusal after the user is known writes ONE <c>reauthentication_failed</c> ops-log line, and success
/// ONE <c>reauthentication_succeeded</c>; both carry the user id and the purpose and nothing else.
/// <para>
/// Point 4's no-row ground is #1349 and it INVERTED an earlier rule. This class used to specify
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
    private const string Grant = "AAECAwQFBgcICQoLDA0ODw"; // gitleaks:allow
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();
    private readonly IAuthAuditLogger _audit = Substitute.For<IAuthAuditLogger>();
    // Real InMemory AppDbContext (implements IAppDbContext) — same fake-DbContext pattern as
    // DeleteAccountCommandHandlerTests. Unique DB name per test-class instance (fresh per [Fact]).
    // Concrete type (not IAppDbContext) per CA1859; passed to the SUT via its IAppDbContext ctor param.
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();

    private ReauthenticationService CreateSut() =>
        new(_currentUser, _grants, _db, _sessionStore, _audit);

    // Authenticated user whose grant the store redeems for exactly the binding the service must assert.
    private void AuthenticatedWithRedeemableGrant(Guid userId)
    {
        _currentUser.UserId.Returns(userId);
        _grants.RedeemAsync(
                GrantToken.FromRaw(Grant),
                GrantAssertion.Of(new GrantSubject.Reauthentication(userId)),
                Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.Reauthentication(userId));
    }

    private async Task SeedSeekerAsync(Guid userId, bool softDeleted, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Test User", TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
        if (softDeleted)
            seeker.SoftDelete(Clock);
        _db.JobSeekers.Add(seeker);
        await _db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenNoUserId_ReturnsInvalidCredentialsAndTouchesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        _currentUser.UserId.Returns((Guid?)null);

        var result = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        // Failsafe returns before ANY store I/O, and there is no user to write a log line about.
        await _grants.DidNotReceive().RedeemAsync(
            Arg.Any<GrantToken>(), Arg.Any<GrantAssertion>(), Arg.Any<CancellationToken>());
        _audit.DidNotReceive().ReauthenticationFailed(Arg.Any<Guid>(), Arg.Any<GrantPurpose>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task VerifyCurrentUserGrant_WhenGrantIsEmpty_RefusesWithoutAskingTheStore(string? grant)
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);

        var result = await CreateSut().VerifyCurrentUserGrantAsync(grant, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _grants.DidNotReceive().RedeemAsync(
            Arg.Any<GrantToken>(), Arg.Any<GrantAssertion>(), Arg.Any<CancellationToken>());
        _audit.Received(1).ReauthenticationFailed(userId, GrantPurpose.Reauthentication);
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_RedeemsWithTheSessionUsersBinding_AndNothingElse()
    {
        // The whole user check is the assertion the service hands the store (ADR 0142 D3: "no handler
        // compares"). This pins the exact argument, so a service that asserted Bearer, or another user's
        // binding, fails here rather than in a store the unit test does not run.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithRedeemableGrant(userId);
        await SeedSeekerAsync(userId, softDeleted: false, ct);

        await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        await _grants.Received(1).RedeemAsync(
            GrantToken.FromRaw(Grant),
            GrantAssertion.Of(new GrantSubject.Reauthentication(userId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenTheStoreAnswersNoSubject_FailsWithoutTouchingSoftDeleteGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        _currentUser.UserId.Returns(userId);
        _grants.RedeemAsync(Arg.Any<GrantToken>(), Arg.Any<GrantAssertion>(), Arg.Any<CancellationToken>())
            .Returns((GrantSubject?)null);

        // Seed a SOFT-DELETED seeker for this user. If the soft-delete gate ran, it would call
        // InvalidateAllForUserAsync on this row. It must NOT — the grant check comes FIRST, so an unusable
        // grant can never reveal (or act on) soft-delete state. The un-fired self-heal is the observable
        // proof that the JobSeekers gate was never reached (grant-first ordering).
        await SeedSeekerAsync(userId, softDeleted: true, ct);

        var result = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.DidNotReceive().InvalidateAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _audit.Received(1).ReauthenticationFailed(userId, GrantPurpose.Reauthentication);
        _audit.DidNotReceive().ReauthenticationSucceeded(Arg.Any<Guid>(), Arg.Any<GrantPurpose>());
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenSeekerSoftDeleted_FailsAndSelfHealsSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithRedeemableGrant(userId);
        await SeedSeekerAsync(userId, softDeleted: true, ct);

        var result = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        // A redeemable grant, but the account is soft-deleted-not-hard-deleted → reject + tear down its
        // surviving sessions (Layer-1 gate).
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.Received(1).InvalidateAllForUserAsync(userId, Arg.Any<CancellationToken>());
        _audit.Received(1).ReauthenticationFailed(userId, GrantPurpose.Reauthentication);
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenSoftDeletedAndSelfHealThrows_StillReturnsInvalidCredentials()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithRedeemableGrant(userId);
        await SeedSeekerAsync(userId, softDeleted: true, ct);
        _sessionStore.InvalidateAllForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Redis nere"));

        var result = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        // Self-heal is best-effort (try/catch): a Redis failure must not turn the gate into a 500 or
        // change the security-relevant reject. Layer 2's tombstone still fail-closes the read path.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.Received(1).InvalidateAllForUserAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenSeekerLiveAndGrantRedeems_ReturnsSuccessAndLogsOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithRedeemableGrant(userId);
        await SeedSeekerAsync(userId, softDeleted: false, ct);

        var result = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        result.IsSuccess.ShouldBeTrue();
        // No session mutation on the success path.
        await _sessionStore.DidNotReceive().InvalidateAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _audit.Received(1).ReauthenticationSucceeded(userId, GrantPurpose.Reauthentication);
        _audit.DidNotReceive().ReauthenticationFailed(Arg.Any<Guid>(), Arg.Any<GrantPurpose>());
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenNoSeekerRow_ReturnsInvalidCredentials()
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
        AuthenticatedWithRedeemableGrant(userId);
        // No seeker seeded — that IS the orphan.

        var result = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        result.IsFailure.ShouldBeTrue(
            "re-auth must not grant a sensitive operation to an account that owns no profile");
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials,
            "uniform with the soft-delete arm and with login — a distinct code would be an "
            + "account-status oracle on an authenticated but hostile-reachable surface");
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_NoSeekerRowAndSoftDeletedAndNoSubjectAnswerIdentically()
    {
        // The three refusal grounds must be indistinguishable to the caller, stated directly rather than
        // inferred from three tests passing. Same discipline as the login-side sibling.
        var ct = TestContext.Current.CancellationToken;

        var orphanUserId = Guid.NewGuid();
        AuthenticatedWithRedeemableGrant(orphanUserId);
        var orphan = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        var deletedUserId = Guid.NewGuid();
        AuthenticatedWithRedeemableGrant(deletedUserId);
        await SeedSeekerAsync(deletedUserId, softDeleted: true, ct);
        var deleted = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        var strangerUserId = Guid.NewGuid();
        _currentUser.UserId.Returns(strangerUserId);
        await SeedSeekerAsync(strangerUserId, softDeleted: false, ct);
        var stranger = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        orphan.IsFailure.ShouldBeTrue();
        deleted.IsFailure.ShouldBeTrue();
        stranger.IsFailure.ShouldBeTrue("the substitute redeems nothing for a binding it was not told about");
        orphan.Error.ShouldBe(deleted.Error);
        deleted.Error.ShouldBe(stranger.Error);
    }
}
