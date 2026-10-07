using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
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
///   (<see cref="ISessionStore.InvalidateAsync"/>) + InvalidCredentials after the original
///   access read admitted it; an earlier stale-proof refusal mutates no sessions. A Redis failure in
///   the self-heal must NOT change the reject outcome</item>
/// <item>else → Success</item>
/// </list>
/// Every refusal after the user is known writes ONE <c>reauthentication_failed</c> ops-log line, and success
/// ONE <c>reauthentication_succeeded</c>; both carry the user id and the purpose and nothing else.
/// <para>
/// Point 4's no-row ground is #1349 and it INVERTED an earlier rule. This class used to specify
/// "a missing seeker row is Success — no-row parity with LoginCommandHandler", and the parity was
/// real: both gates passed an orphan. The behaviour it mirrored was the defect. The projection had to change for the
/// gate to see the case at all — <c>Select(js =&gt; (DateTimeOffset?)js.DeletedAt)</c> made
/// <c>FirstOrDefaultAsync</c> answer null for "no row" and "a live row" alike.
/// </para>
/// The gate keys <c>userId → JobSeeker.UserId</c> via <c>IgnoreQueryFilters()</c> (the global
/// DeletedAt==null filter would otherwise hide the soft-deleted row).
/// </summary>
public class ReauthenticationServiceTests
{
    private const string Grant = "AAECAwQFBgcICQoLDA0ODw"; // gitleaks:allow
    private static readonly SessionId CurrentSessionId = SessionId.FromRaw("reauthenticated-session-id");
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly ISessionStore _sessionStore = Substitute.For<ISessionStore>();
    private readonly IAuthAuditLogger _audit = Substitute.For<IAuthAuditLogger>();
    // Real InMemory AppDbContext (implements IAppDbContext) — same fake-DbContext pattern as
    // DeleteAccountCommandHandlerTests. Unique DB name per test-class instance (fresh per [Fact]).
    // Concrete type (not IAppDbContext) per CA1859; passed to the SUT via its IAppDbContext ctor param.
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly RecordingLogger<ReauthenticationService> _logger = new();

    private ReauthenticationService CreateSut(IAccountAccessReader? reader = null) =>
        new(_currentUser, _grants, _db, _sessionStore, _audit,
            reader ?? AccountAccessTestKit.ReaderFromProfiles(_db,
                userId => _currentUser.UserId == userId ? "person@example.com" : null), _logger);

    // Authenticated user whose grant the store redeems for exactly the binding the service must assert.
    private void AuthenticatedWithRedeemableGrant(Guid userId)
    {
        _currentUser.UserId.Returns(userId);
        _currentUser.AccessRevision.Returns(0L);
        _currentUser.SessionId.Returns(CurrentSessionId);
        _grants.RedeemAsync(
                GrantToken.FromRaw(Grant),
                GrantAssertion.Of(new GrantSubject.Reauthentication(userId)),
                Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.Reauthentication(userId) { Access = AccountAccessTestKit.Bound(userId) });
    }

    private async Task SeedSeekerAsync(Guid userId, bool softDeleted, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
        if (softDeleted)
            seeker.SoftDelete(Clock);
        _db.JobSeekers.Add(seeker);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<IAccountAccessReader> SoftDeleteAfterCommittedAccessReadAsync(Guid userId, CancellationToken ct)
    {
        await SeedSeekerAsync(userId, softDeleted: false, ct);
        var authoritative = AccountAccessTestKit.ReaderFromProfiles(_db,
            id => id == userId ? "person@example.com" : null);
        var reader = Substitute.For<IAccountAccessReader>();
        reader.ReadEpochAsync(Arg.Any<CancellationToken>()).Returns(0L);
        reader.ReadAsync(userId, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            // A real primary read may finish before the response is delivered while self-deletion
            // commits meanwhile. Capture that read, then invoke and validate the actual transform.
            var snapshot = await authoritative.ReadAsync(userId, call.Arg<CancellationToken>());
            if (snapshot?.HasLiveProfile == true)
            {
                var profile = await _db.JobSeekers.SingleAsync(seeker => seeker.UserId == userId, ct);
                profile.DeletedAt.ShouldBeNull();
                profile.SoftDelete(Clock);
                profile.DeletedAt.ShouldBe(Clock.UtcNow);
                await _db.SaveChangesAsync(ct);
            }
            return snapshot;
        });
        return reader;
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

        // A bad grant cannot act on the account's deletion state or its current session.
        await SeedSeekerAsync(userId, softDeleted: true, ct);

        var result = await CreateSut().VerifyCurrentUserGrantAsync(Grant, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.DidNotReceive().InvalidateAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _sessionStore.DidNotReceiveWithAnyArgs().InvalidateBeforeRevisionAsync(default, default, ct);
        await _sessionStore.DidNotReceiveWithAnyArgs().InvalidateAsync(default, ct);
        _audit.Received(1).ReauthenticationFailed(userId, GrantPurpose.Reauthentication);
        _audit.DidNotReceive().ReauthenticationSucceeded(Arg.Any<Guid>(), Arg.Any<GrantPurpose>());
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenProfileDeletesAfterAccessRead_FailsAndInvalidatesOnlyItsCurrentSession()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithRedeemableGrant(userId);
        var reader = await SoftDeleteAfterCommittedAccessReadAsync(userId, ct);

        var result = await CreateSut(reader).VerifyCurrentUserGrantAsync(Grant, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.Received(1).InvalidateAsync(CurrentSessionId, Arg.Any<CancellationToken>());
        await _sessionStore.DidNotReceiveWithAnyArgs().InvalidateBeforeRevisionAsync(default, default, ct);
        await _sessionStore.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, ct);
        _audit.Received(1).ReauthenticationFailed(userId, GrantPurpose.Reauthentication);
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenLateDeletionCleanupIsUnavailable_StillRefusesAndLogsTypedFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        AuthenticatedWithRedeemableGrant(userId);
        var reader = await SoftDeleteAfterCommittedAccessReadAsync(userId, ct);
        _sessionStore.InvalidateAsync(CurrentSessionId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new SessionStoreUnavailableException("session cleanup unavailable", new TimeoutException()));

        var result = await CreateSut(reader).VerifyCurrentUserGrantAsync(Grant, ct);

        // Self-heal is best-effort (try/catch): a Redis failure must not turn the gate into a 500 or
        // change the security-relevant reject. Layer 2's tombstone still fail-closes the read path.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _sessionStore.Received(1).InvalidateAsync(CurrentSessionId, Arg.Any<CancellationToken>());
        await _sessionStore.DidNotReceiveWithAnyArgs().InvalidateBeforeRevisionAsync(default, default, ct);
        await _sessionStore.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, ct);
        _logger.Records.ShouldHaveSingleItem().EventId.Id.ShouldBe(1032);
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
        await _sessionStore.DidNotReceiveWithAnyArgs().InvalidateBeforeRevisionAsync(default, default, ct);
        await _sessionStore.DidNotReceiveWithAnyArgs().InvalidateAsync(default, ct);
        _audit.Received(1).ReauthenticationSucceeded(userId, GrantPurpose.Reauthentication);
        _audit.DidNotReceive().ReauthenticationFailed(Arg.Any<Guid>(), Arg.Any<GrantPurpose>());
    }

    [Fact]
    public async Task VerifyCurrentUserGrant_WhenNoSeekerRow_ReturnsInvalidCredentials()
    {
        // #1349 — INVERTED, and the old version is worth reading before this one. It asserted success
        // and justified it as "Parity with LoginCommandHandler: a user without a seeker row is not
        // blocked by the gate". The parity was real; the behaviour it mirrored was the defect.
        //
        // The gate could not even SEE this case before: Select(js => (DateTimeOffset?)js.DeletedAt)
        // made FirstOrDefaultAsync answer null for "no row" and for "a live row" alike. An orphan
        // holding a live session therefore passed re-auth, changed its password, and was handed a
        // FRESH session by the /change-password re-issue — renewing the capability indefinitely
        // without ever crossing the login guard (security-auditor M-1).
        //
        // Historical actor: the predecessor AccountHardDeleter at 22aefd8db could commit the profile
        // removal before Identity deletion. The current writer's atomic deletion is pinned by
        // DeleteMeTests.POST_me_delete_whose_erasure_fails_rolls_back_profile_and_keeps_session_and_links;
        // registration's current pin is AccountRegistrationAtomicityTests.
        // OpenAsync_ShouldLeaveNoIdentityOrProfile_WhenAuditSaveFails. This legacy orphan stays refused.
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
